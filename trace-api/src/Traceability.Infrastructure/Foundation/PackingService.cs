using Microsoft.Data.Sqlite;
using Traceability.Application;
using Traceability.Domain;

namespace Traceability.Infrastructure.Foundation;

public sealed record PackingBox(string Qr, string Kind, string Wo, string Sku,
    string Client, string Shipping, string? ClosedAt);

public sealed class PackingService(TraceDb db, INgWorkflowService workflow)
{
    public object Mutate(CallerPrincipal caller, ProductionContextDto context, string kind,
        string action, string qr, string? member)
    {
        var capacity = kind == "INNER" ? 10 : 12;
        ValidateBoxQr(qr, kind);
        if (kind == "INNER") member = member?.ToUpperInvariant();
        if (context.ActiveWorkOrderNumber is not { } wo)
            throw FoundationErrors.Conflict("NO_ACTIVE_WO", "Pilih WO aktif.");

        using var connection = db.OpenConnection();
        using var transaction = connection.BeginTransaction();
        var now = DateTimeOffset.UtcNow.ToString("O");
        var actor = caller.AccountId;
        var client = context.WorkOrderClient ?? "-";
        var shipping = context.ShippingReference ?? "-";
        if (action == "open")
        {
            Sql.Execute(connection, """
                INSERT INTO packing_boxes (qr,kind,wo,sku,client,shipping,opened_by,opened_at)
                VALUES ($qr,$kind,$wo,$sku,$client,$shipping,$actor,$now)
                ON CONFLICT(qr) DO NOTHING;
                """, ("$qr", qr), ("$kind", kind), ("$wo", wo), ("$sku", context.SkuCode),
                ("$client", client), ("$shipping", shipping), ("$actor", actor), ("$now", now));
        }

        var box = FindBox(connection, qr);
        if (box.Kind != kind || box.Wo != wo || box.Sku != context.SkuCode ||
            box.Client != client || box.Shipping != shipping)
            throw FoundationErrors.Conflict("PACK_CONTEXT", "Box tercatat untuk WO, SKU, atau pengiriman lain.");

        var members = Members(connection, box);
        if (action == "close" && box.ClosedAt is null)
        {
            if (members.Count != capacity)
                throw FoundationErrors.Conflict("BOX_INCOMPLETE", $"Box harus berisi {capacity} anggota.");
            Sql.Execute(connection, "UPDATE packing_boxes SET closed_by=$actor, closed_at=$now WHERE qr=$qr;",
                ("$actor", actor), ("$now", now), ("$qr", qr));
            box = box with { ClosedAt = now };
        }

        var reused = action == "add" && member is not null && members.Contains(member);
        if (action == "add" && !reused)
        {
            if (string.IsNullOrWhiteSpace(member) || member.Length > 128)
                throw FoundationErrors.BadRequest("MEMBER_REQUIRED", "Pindai anggota box.");
            if (box.ClosedAt is not null || members.Count >= capacity)
                throw FoundationErrors.Conflict("BOX_FULL", "Box sudah penuh atau ditutup.");
            if (kind == "INNER") AddUnit(connection, box, member, actor, now);
            else AddInner(connection, box, member, actor, now);
            members = Members(connection, box);
        }

        transaction.Commit();
        return new { box, members, count = members.Count, capacity, reused };
    }

    private static void AddUnit(SqliteConnection connection, PackingBox box, string serial, string actor, string now)
    {
        var unit = Sql.ReadOne(connection, """
            SELECT u.id,u.status,u.sku_code,u.target_wo_number,u.target_client,w.shipping_reference
            FROM units u LEFT JOIN work_orders w ON w.id=u.target_wo_id WHERE u.serial=$serial;
            """, r => new { Id = r.GetString(0), Status = r.GetString(1), Sku = r.GetString(2),
                Wo = Sql.NullableText(r, "target_wo_number"), Client = Sql.NullableText(r, "target_client"),
                Shipping = Sql.NullableText(r, "shipping_reference") }, ("$serial", serial));
        if (unit is null || unit.Status != "OK_FINAL")
            throw FoundationErrors.Conflict("UNIT_NOT_READY", "Serial harus terdaftar sebagai OK akhir.");
        if (unit.Wo != box.Wo || unit.Sku != box.Sku || unit.Client != box.Client || unit.Shipping != box.Shipping)
            throw FoundationErrors.Conflict("PACK_CONTEXT", "Unit tercatat untuk WO, SKU, atau pengiriman lain.");
        var existing = Sql.ScalarText(connection, "SELECT inner_qr FROM packing_units WHERE unit_id=$id;", ("$id", unit.Id));
        EnsureUnassigned(existing);
        Sql.Execute(connection, "INSERT INTO packing_units VALUES ($id,$box,$actor,$now);",
            ("$id", unit.Id), ("$box", box.Qr), ("$actor", actor), ("$now", now));
    }

    private static void AddInner(SqliteConnection connection, PackingBox box, string qr, string actor, string now)
    {
        ValidateBoxQr(qr, "INNER");
        var inner = FindBox(connection, qr);
        if (inner.ClosedAt is null)
            throw FoundationErrors.Conflict("INNER_OPEN", "Tutup inner sebelum memasukkannya ke outer.");
        if (inner.Wo != box.Wo || inner.Sku != box.Sku || inner.Client != box.Client || inner.Shipping != box.Shipping)
            throw FoundationErrors.Conflict("PACK_CONTEXT", "Inner tercatat untuk WO, SKU, atau pengiriman lain.");
        var existing = Sql.ScalarText(connection, "SELECT outer_qr FROM packing_inners WHERE inner_qr=$id;", ("$id", qr));
        EnsureUnassigned(existing);
        Sql.Execute(connection, "INSERT INTO packing_inners VALUES ($id,$box,$actor,$now);",
            ("$id", qr), ("$box", box.Qr), ("$actor", actor), ("$now", now));
    }

    private static void EnsureUnassigned(string? existing)
    {
        if (existing is not null)
            throw FoundationErrors.Conflict("PACK_ASSIGNED", $"Anggota sudah terdaftar di {existing}.");
    }

    private static void ValidateBoxQr(string qr, string kind)
    {
        var prefix = kind + ":";
        if (!qr.StartsWith(prefix, StringComparison.Ordinal) || qr.Length <= prefix.Length || qr.Length > 128)
            throw FoundationErrors.BadRequest("QR_TYPE", $"Pindai QR {kind} dengan awalan {prefix}.");
    }

    private static PackingBox FindBox(SqliteConnection connection, string qr) =>
        Sql.ReadOne(connection, "SELECT * FROM packing_boxes WHERE qr=$qr;",
            r => new PackingBox(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4),
                r.GetString(5), Sql.NullableText(r, "closed_at")), ("$qr", qr))
        ?? throw FoundationErrors.NotFound("Box belum terdaftar.");

    private static List<string> Members(SqliteConnection connection, PackingBox box) =>
        Sql.ReadList(connection, box.Kind == "INNER"
            ? "SELECT u.serial FROM packing_units p JOIN units u ON u.id=p.unit_id WHERE p.inner_qr=$qr ORDER BY p.scanned_at,u.serial;"
            : "SELECT inner_qr FROM packing_inners WHERE outer_qr=$qr ORDER BY scanned_at,inner_qr;",
            r => r.GetString(0), ("$qr", box.Qr));

    public object Detail(string reference)
    {
        var history = workflow.GetUnit(reference);
        using var connection = db.OpenConnection();
        var chain = Sql.ReadOne(connection, """
            SELECT p.inner_qr,i.outer_qr FROM packing_units p
            LEFT JOIN packing_inners i ON i.inner_qr=p.inner_qr WHERE p.unit_id=$id;
            """, r => new { Inner = r.GetString(0), Outer = Sql.NullableText(r, "outer_qr") }, ("$id", history.Unit.Id));
        var destination = Sql.ReadOne(connection, """
            SELECT u.target_wo_number,u.target_client,w.shipping_reference
            FROM units u LEFT JOIN work_orders w ON w.id=u.target_wo_id WHERE u.id=$id;
            """, r => new { WorkOrder = Sql.NullableText(r, "target_wo_number"),
                Client = Sql.NullableText(r, "target_client"), ShippingReference = Sql.NullableText(r, "shipping_reference") },
            ("$id", history.Unit.Id));
        return new { history, chain, destination };
    }

    public object Search(string? search, string? status, string? from, string? to, int page)
    {
        using var connection = db.OpenConnection();
        var size = 25;
        var offset = (long)(Math.Max(1, page) - 1) * size;
        var entries = Sql.ReadList(connection, """
            SELECT u.serial,u.label_id,u.status,u.sku_code,u.target_wo_number,u.origin_wo_number,
                p.inner_qr,i.outer_qr,u.last_event_at_utc,u.target_client
            FROM units u
            LEFT JOIN packing_units p ON p.unit_id=u.id
            LEFT JOIN packing_inners i ON i.inner_qr=p.inner_qr
            LEFT JOIN ng_labels l ON l.id=u.label_id
            WHERE ($q='' OR u.serial=UPPER($q) OR u.label_id=$q OR l.payload=$q OR
                u.target_wo_number=$q OR u.origin_wo_number=$q OR p.inner_qr=$q OR i.outer_qr=$q)
              AND ($status='' OR u.status=$status)
              AND ($from IS NULL OR u.last_event_at_utc >= $from)
              AND ($to IS NULL OR u.last_event_at_utc < $to)
            ORDER BY u.last_event_at_utc DESC,u.id LIMIT $limit OFFSET $offset;
            """, r => new { Serial = Sql.NullableText(r,"serial"), Label = Sql.NullableText(r,"label_id"),
                Status = r.GetString(2), Sku = r.GetString(3), WorkOrder = Sql.NullableText(r,"target_wo_number"),
                OriginWorkOrder = Sql.NullableText(r,"origin_wo_number"),
                Inner = Sql.NullableText(r,"inner_qr"), Outer = Sql.NullableText(r,"outer_qr"), Time = r.GetString(8),
                Client = Sql.NullableText(r,"target_client") },
            ("$q", search?.Trim() ?? ""), ("$status", status ?? ""), ("$from", DateBoundary(from, false)),
            ("$to", DateBoundary(to, true)), ("$limit", size + 1), ("$offset", offset));
        return new { items = entries.Take(size), hasNext = entries.Count > size };
    }

    private static string? DateBoundary(string? value, bool nextDay)
    {
        if (string.IsNullOrEmpty(value)) return null;
        if (!DateOnly.TryParseExact(value, "yyyy-MM-dd", out var date) || date == DateOnly.MaxValue || date == DateOnly.MinValue)
            throw FoundationErrors.BadRequest("PACK_DATE", "Gunakan tanggal yyyy-MM-dd.");
        if (nextDay) date = date.AddDays(1);
        return new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(7))
            .ToUniversalTime().ToString("O");
    }

    public object ProductionReport(string? from, string? to, string? sku, string? wo)
    {
        var start = DateBoundary(from, false);
        var end = DateBoundary(to, true);
        if (start is not null && end is not null && string.CompareOrdinal(start, end) >= 0)
            throw FoundationErrors.BadRequest("REPORT_RANGE", "Tanggal awal harus sebelum atau sama dengan tanggal akhir.");

        using var connection = db.OpenConnection();
        using var transaction = connection.BeginTransaction(deferred: true);
        const string filters = """
            ($sku='' OR u.sku_code=$sku COLLATE NOCASE)
              AND ($wo='' OR COALESCE(u.target_wo_number,u.origin_wo_number)=$wo COLLATE NOCASE)
            """;
        const string period = "($from IS NULL OR {0}>=$from) AND ($to IS NULL OR {0}<$to)";
        var parameters = new (string, object?)[] { ("$from", start), ("$to", end),
            ("$sku", sku?.Trim() ?? ""), ("$wo", wo?.Trim() ?? "") };
        var totals = Sql.ReadOne(connection, """
            SELECT COUNT(*),
                COUNT(CASE WHEN u.status='OK_FINAL' THEN 1 END),
                COUNT(CASE WHEN u.first_ng_at_utc IS NOT NULL THEN 1 END),
                COUNT(CASE WHEN u.status='NG_QUARANTINE' THEN 1 END),
                COUNT(CASE WHEN u.status='REWORK_RETEST' THEN 1 END),
                COUNT(CASE WHEN u.status='QC_HOLD' THEN 1 END),
                COUNT(CASE WHEN u.status='OK_FINAL' AND p.unit_id IS NULL THEN 1 END)
            FROM units u LEFT JOIN packing_units p ON p.unit_id=u.id
            WHERE
            """ + filters + " AND " + string.Format(period, "u.created_at_utc"),
            r => new { Total = r.GetInt64(0), Ok = r.GetInt64(1), NgUnits = r.GetInt64(2),
                Quarantine = r.GetInt64(3), Retest = r.GetInt64(4), Hold = r.GetInt64(5), AwaitingInner = r.GetInt64(6) }, parameters);
        var daily = Sql.ReadList(connection, """
            SELECT date(u.created_at_utc,'+7 hours') AS day,COUNT(*),
                COUNT(CASE WHEN u.first_ng_at_utc IS NOT NULL THEN 1 END)
            FROM units u WHERE
            """ + filters + " AND " + string.Format(period, "u.created_at_utc") + " GROUP BY day ORDER BY day;",
            r => new { Day = r.GetString(0), Total = r.GetInt64(1), Ng = r.GetInt64(2) }, parameters);
        var defects = Sql.ReadList(connection, """
            SELECT e.stage_code,e.defect_group,e.defect_name,COUNT(*) AS total
            FROM unit_ng_events e JOIN units u ON u.id=e.unit_id WHERE
            """ + filters + " AND " + string.Format(period, "e.occurred_at_utc") +
            " GROUP BY e.stage_code,e.defect_group,e.defect_name ORDER BY total DESC,e.stage_code,e.defect_name LIMIT 8;",
            r => new { Stage = r.GetString(0), Group = r.GetString(1), Name = r.GetString(2), Count = r.GetInt64(3) }, parameters);
        var products = Sql.ReadList(connection,
            "SELECT DISTINCT sku_code FROM units WHERE sku_code<>'' ORDER BY sku_code;", r => r.GetString(0));
        transaction.Commit();
        return new { totals, daily, defects, products };
    }

    public object WorkOrder(ProductionContextDto context)
    {
        using var connection = db.OpenConnection();
        return Sql.ReadOne(connection, """
            SELECT COUNT(*) AS ok,COUNT(p.unit_id) AS inner_count,COUNT(i.inner_qr) AS outer_count
            FROM units u JOIN work_orders w ON w.id=u.target_wo_id
            LEFT JOIN packing_units p ON p.unit_id=u.id
            LEFT JOIN packing_inners i ON i.inner_qr=p.inner_qr
            WHERE u.status='OK_FINAL' AND u.target_wo_number=$wo AND u.sku_code=$sku
                AND u.target_client=$client AND w.shipping_reference=$shipping;
            """, r => new { workOrder = context.ActiveWorkOrderNumber, context.TargetQty,
                ok = r.GetInt64(0), inner = r.GetInt64(1), outer = r.GetInt64(2) },
            ("$wo", context.ActiveWorkOrderNumber), ("$sku", context.SkuCode),
            ("$client", context.WorkOrderClient), ("$shipping", context.ShippingReference))!;
    }
}
