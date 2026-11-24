using Microsoft.Data.Sqlite;
using Traceability.Application;
using Traceability.Domain;

namespace Traceability.Infrastructure.Foundation;

/// <summary>Hasil satu operasi tulis NG/rework/OK; Reused = retry memakai kunci idempotensi lama.</summary>
public sealed class NgWriteOutcome
{
    public bool Reused { get; set; }
    public string UnitId { get; set; } = string.Empty;
    /// <summary>ID record yang dibuat: kejadian NG, catatan rework, atau hasil OK.</summary>
    public string RecordId { get; set; } = string.Empty;
}

/// <summary>
/// Perintah tulis yang sudah divalidasi service: snapshot master, konteks sesi, dan
/// kunci idempotensi. Store hanya menegakkan invariant transisi di dalam transaksi.
/// </summary>
public sealed class NgWriteCommand
{
    public string Operation { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string Hash { get; set; } = string.Empty;
    public string NowUtc { get; set; } = string.Empty;
    public string ActorAccountId { get; set; } = string.Empty;
    public string ActorName { get; set; } = string.Empty;
    public string ShiftCode { get; set; } = string.Empty;
    public string LineCode { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string SkuCode { get; set; } = string.Empty;
    public string FamilyCode { get; set; } = string.Empty;
    public int MaxAttempts { get; set; } = NgAttemptRules.DefaultMaxAttempts;
    public string Note { get; set; } = string.Empty;

    /// <summary>ID label internal (NG-000123) untuk operasi NG/rework/OK pasangan.</summary>
    public string LabelId { get; set; } = string.Empty;
    /// <summary>Serial hasil ketik/scan operator; null pada NG pertama.</summary>
    public string? Serial { get; set; }
    /// <summary>ID unit, serial, atau label untuk operasi rework.</summary>
    public string UnitRef { get; set; } = string.Empty;
    /// <summary>Aksi perbaikan singkat yang disimpan pada catatan rework.</summary>
    public string ReworkAction { get; set; } = string.Empty;

    public string StageId { get; set; } = string.Empty;
    public string StageCode { get; set; } = string.Empty;
    public string StageName { get; set; } = string.Empty;
    public string DefectId { get; set; } = string.Empty;
    public string DefectGroup { get; set; } = string.Empty;
    public string DefectName { get; set; } = string.Empty;

    /// <summary>WO aktif pelaku saat transaksi; menjadi tujuan pada OK akhir.</summary>
    public string? WorkOrderId { get; set; }
    public string? WorkOrderNumber { get; set; }
    public string? WorkOrderClient { get; set; }
}

/// <summary>Hitungan layar karantina: antrean status, OK hari ini, dan volume kejadian.</summary>
public sealed class NgWorkloadCounts
{
    public int Quarantine { get; set; }
    public int Retest { get; set; }
    public int QcHold { get; set; }
    public int OkFinalToday { get; set; }
    public int NgEventsToday { get; set; }
    public int ReworkToday { get; set; }
}

/// <summary>
/// Akses data alur NG → rework → OK akhir (Fase 3). Setiap operasi tulis berjalan
/// dalam satu transaksi: kunci idempotensi ditulis lebih dulu, label diikat lewat
/// UPDATE bersyarat, dan perpindahan status dijaga WHERE status/attempts sehingga dua
/// operator yang memindai bersamaan hanya menghasilkan satu hasil sah.
/// </summary>
public sealed class NgWorkflowStore
{
    private const string UnitSelect =
        "SELECT u.id AS id, u.serial AS serial, u.label_id AS label_id, u.status AS status, " +
        "u.attempts AS attempts, u.family_code AS family_code, u.sku_code AS sku_code, u.stage_code AS stage_code, " +
        "u.origin_wo_id AS origin_wo_id, u.origin_wo_number AS origin_wo_number, " +
        "u.origin_client AS origin_client, u.origin_session_id AS origin_session_id, " +
        "u.target_wo_id AS target_wo_id, u.target_wo_number AS target_wo_number, " +
        "u.target_client AS target_client, u.target_session_id AS target_session_id, " +
        "u.first_ng_at_utc AS first_ng_at_utc, u.last_event_at_utc AS last_event_at_utc, " +
        "u.ok_at_utc AS ok_at_utc, u.ok_actor_name AS ok_actor_name, " +
        "u.created_at_utc AS created_at_utc, u.updated_at_utc AS updated_at_utc, " +
        "(SELECT l.payload FROM ng_labels l WHERE l.id = u.label_id) AS label_payload " +
        "FROM units u";

    private const string NgEventSelect =
        "SELECT id, unit_id, seq, label_id, stage_id, stage_code, stage_name, defect_id, defect_group, defect_name, " +
        "attempt_no, disposition, wo_origin_id, wo_origin_number, actor_account_id, actor_name, shift_code, " +
        "line_code, sku_code, family_code, session_id, note, occurred_at_utc FROM unit_ng_events";

    private const string ReworkSelect =
        "SELECT id, unit_id, attempt_no, ng_event_id, action, actor_account_id, actor_name, shift_code, " +
        "line_code, session_id, note, performed_at_utc FROM unit_rework_events";

    private const string OkSelect =
        "SELECT id, unit_id, serial, label_id, mode, wo_target_id, wo_target_number, client, actor_account_id, " +
        "actor_name, shift_code, line_code, sku_code, session_id, occurred_at_utc FROM unit_ok_scans";

    private readonly TraceDb _db;

    public NgWorkflowStore(TraceDb db) => _db = db;

    // -----------------------------------------------------------------------
    // Baca
    // -----------------------------------------------------------------------

    public ScanRequestKeyRecord? FindKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        using var connection = _db.OpenConnection();
        return FindKeyCore(connection, key);
    }

    /// <summary>Unit berdasarkan ID, serial, atau label internal ("NG:000123"/"NG-000123").</summary>
    public UnitRecord? ResolveUnit(string reference)
    {
        using var connection = _db.OpenConnection();
        return ResolveUnitCore(connection, reference);
    }

    public UnitRecord? FindUnit(string unitId)
    {
        if (string.IsNullOrWhiteSpace(unitId)) return null;
        using var connection = _db.OpenConnection();
        return Sql.ReadOne(connection, $"{UnitSelect} WHERE u.id = $id;", MapUnit, ("$id", unitId));
    }

    public UnitRecord? FindUnitByLabel(string labelId)
    {
        using var connection = _db.OpenConnection();
        return FindUnitByLabelCore(connection, labelId);
    }

    public UnitRecord? FindUnitBySerial(string serial)
    {
        using var connection = _db.OpenConnection();
        return FindUnitBySerialCore(connection, serial);
    }

    public NgEventRecord? FindNgEvent(string eventId)
    {
        if (string.IsNullOrWhiteSpace(eventId)) return null;
        using var connection = _db.OpenConnection();
        return Sql.ReadOne(connection, $"{NgEventSelect} WHERE id = $id;", MapNgEvent, ("$id", eventId));
    }

    public ReworkEventRecord? FindReworkEvent(string eventId)
    {
        if (string.IsNullOrWhiteSpace(eventId)) return null;
        using var connection = _db.OpenConnection();
        return Sql.ReadOne(connection, $"{ReworkSelect} WHERE id = $id;", MapRework, ("$id", eventId));
    }

    public OkScanRecord? FindOkScan(string unitId)
    {
        using var connection = _db.OpenConnection();
        return FindOkScanCore(connection, unitId);
    }

    public List<NgEventRecord> NgEvents(string unitId)
    {
        using var connection = _db.OpenConnection();
        return NgEventsCore(connection, unitId);
    }

    public List<ReworkEventRecord> ReworkEvents(string unitId)
    {
        using var connection = _db.OpenConnection();
        return ReworkEventsCore(connection, unitId);
    }

    /// <summary>Antrean unit per status untuk layar karantina (terbaru lebih dulu).</summary>
    public List<UnitRecord> UnitsByStatus(string status, int limit)
    {
        using var connection = _db.OpenConnection();
        var sql = $"{UnitSelect} WHERE u.status = $status ORDER BY u.last_event_at_utc DESC, u.id DESC";
        if (limit > 0) sql += $" LIMIT {limit}";
        return Sql.ReadList(connection, sql + ";", MapUnit, ("$status", status));
    }

    public (List<UnitRecord> Items, int Total) QueryUnits(string? search, string? status, int page, int pageSize)
    {
        using var connection = _db.OpenConnection();
        var where = "WHERE ($status IS NULL OR u.status = $status)";
        var like = LikeTerm(search);
        if (like is not null)
        {
            where += " AND (u.serial LIKE $like ESCAPE '\\' OR u.label_id LIKE $like ESCAPE '\\' " +
                     "OR u.origin_wo_number LIKE $like ESCAPE '\\' OR u.target_wo_number LIKE $like ESCAPE '\\' " +
                     "OR u.id LIKE $like ESCAPE '\\')";
        }

        var parameters = new (string, object?)[] { ("$status", status), ("$like", like) };
        var total = Sql.ScalarLong(connection, $"SELECT COUNT(1) FROM units u {where};", parameters);
        var rows = Sql.ReadList(connection,
            $"{UnitSelect} {where} ORDER BY u.last_event_at_utc DESC, u.id DESC LIMIT $limit OFFSET $offset;",
            MapUnit, parameters
                .Append(("$limit", pageSize))
                .Append(("$offset", (page - 1) * pageSize))
                .ToArray());
        return (rows, (int)total);
    }

    /// <summary>Hitungan layar; rentang UTC sudah dihitung service dari hari WIB.</summary>
    public NgWorkloadCounts Counts(string fromUtc, string toUtc)
    {
        using var connection = _db.OpenConnection();
        return new NgWorkloadCounts
        {
            Quarantine = (int)Sql.ScalarLong(connection,
                "SELECT COUNT(1) FROM units WHERE status = 'NG_QUARANTINE';"),
            Retest = (int)Sql.ScalarLong(connection,
                "SELECT COUNT(1) FROM units WHERE status = 'REWORK_RETEST';"),
            QcHold = (int)Sql.ScalarLong(connection,
                "SELECT COUNT(1) FROM units WHERE status = 'QC_HOLD';"),
            OkFinalToday = (int)Sql.ScalarLong(connection,
                "SELECT COUNT(1) FROM unit_ok_scans WHERE occurred_at_utc >= $from AND occurred_at_utc < $to;",
                ("$from", fromUtc), ("$to", toUtc)),
            NgEventsToday = (int)Sql.ScalarLong(connection,
                "SELECT COUNT(1) FROM unit_ng_events WHERE occurred_at_utc >= $from AND occurred_at_utc < $to;",
                ("$from", fromUtc), ("$to", toUtc)),
            ReworkToday = (int)Sql.ScalarLong(connection,
                "SELECT COUNT(1) FROM unit_rework_events WHERE performed_at_utc >= $from AND performed_at_utc < $to;",
                ("$from", fromUtc), ("$to", toUtc)),
        };
    }

    // -----------------------------------------------------------------------
    // Tulis transaksional
    // -----------------------------------------------------------------------

    /// <summary>
    /// NG pertama: ikat label stok AVAILABLE ke unit baru, simpan kejadian seq 1,
    /// status Karantina. Label yang sudah terikat tidak dapat dipakai unit lain.
    /// </summary>
    public NgWriteOutcome RecordFirstNg(NgWriteCommand cmd)
    {
        using var connection = _db.OpenConnection();
        using var transaction = connection.BeginTransaction();
        try
        {
            InsertKey(connection, cmd);

            var labelStatus = Sql.ScalarText(connection,
                "SELECT status FROM ng_labels WHERE id = $id;", ("$id", cmd.LabelId));
            if (labelStatus is null)
            {
                throw FoundationErrors.Conflict("LABEL_UNKNOWN",
                    $"Label {cmd.LabelId} belum pernah diterbitkan.");
            }

            if (string.Equals(labelStatus, LabelStatus.Void, StringComparison.Ordinal))
            {
                throw FoundationErrors.Conflict("LABEL_VOID", $"Label {cmd.LabelId} sudah dibatalkan.");
            }

            if (!string.Equals(labelStatus, LabelStatus.Available, StringComparison.Ordinal))
            {
                throw FoundationErrors.Conflict("LABEL_ALREADY_BOUND",
                    $"Label {cmd.LabelId} sudah terikat ke unit lain.");
            }

            // UPDATE bersyarat lebih dulu: lock writer SQLite membuat dua pemindaian
            // paralel tidak mungkin sama-sama berhasil mengikat label yang sama.
            var bound = Sql.ExecuteCount(connection,
                "UPDATE ng_labels SET status = 'BOUND', bound_serial = $serial, bound_at_utc = $now " +
                "WHERE id = $id AND status = 'AVAILABLE';",
                ("$serial", cmd.Serial), ("$now", cmd.NowUtc), ("$id", cmd.LabelId));
            if (bound != 1)
            {
                throw FoundationErrors.Conflict("LABEL_ALREADY_BOUND",
                    $"Label {cmd.LabelId} baru saja dipakai operator lain; pindai label berikutnya.");
            }

            var unitId = NewId();
            var eventId = NewId();
            Sql.Execute(connection,
                "INSERT INTO units (id, serial, label_id, status, attempts, family_code, sku_code, stage_code, " +
                "origin_wo_id, origin_wo_number, origin_client, origin_session_id, " +
                "first_ng_at_utc, last_event_at_utc, created_at_utc, updated_at_utc) " +
                "VALUES ($id, $serial, $labelId, $status, 0, $family, $sku, $stage, " +
                "$woId, $woNumber, $client, $session, $now, $now, $now, $now);",
                ("$id", unitId), ("$serial", cmd.Serial), ("$labelId", cmd.LabelId),
                ("$status", UnitStatus.Quarantine), ("$family", cmd.FamilyCode), ("$sku", cmd.SkuCode),
                ("$stage", cmd.StageCode), ("$woId", cmd.WorkOrderId), ("$woNumber", cmd.WorkOrderNumber),
                ("$client", cmd.WorkOrderClient), ("$session", cmd.SessionId), ("$now", cmd.NowUtc));

            InsertNgEvent(connection, eventId, unitId, 1, 0, NgDisposition.ReworkRequired,
                cmd.LabelId, cmd);

            SealKey(connection, cmd.Key, unitId, eventId);
            transaction.Commit();
            return new NgWriteOutcome { UnitId = unitId, RecordId = eventId };
        }
        catch (SqliteException exception) when (IsKeyCollision(exception))
        {
            SafeRollback(transaction);
            return Replay(connection, cmd);
        }
        catch
        {
            SafeRollback(transaction);
            throw;
        }
    }

    /// <summary>
    /// NG ulang: hanya dari status Uji ulang (rework selesai). Kejadian baru tetap
    /// baris terpisah; disposition menjadi Tahan QC bila percobaan sudah habis.
    /// </summary>
    public NgWriteOutcome RecordRepeatNg(NgWriteCommand cmd)
    {
        using var connection = _db.OpenConnection();
        using var transaction = connection.BeginTransaction();
        try
        {
            InsertKey(connection, cmd);

            var reference = cmd.LabelId.Length > 0 ? cmd.LabelId : cmd.UnitRef;
            var unit = ResolveUnitCore(connection, reference)
                ?? throw FoundationErrors.Conflict("UNIT_UNKNOWN",
                    "Label ini belum terikat ke unit; catat sebagai NG pertama.");

            if (string.Equals(unit.Status, UnitStatus.OkFinal, StringComparison.Ordinal))
            {
                throw FoundationErrors.Conflict("UNIT_FINISHED",
                    "Unit ini sudah lulus OK akhir; tidak dapat dicatat NG lagi.");
            }

            if (string.Equals(unit.Status, UnitStatus.QcHold, StringComparison.Ordinal))
            {
                throw FoundationErrors.Conflict("QC_HOLD",
                    "Unit ini dalam Tahan QC; kejadian NG baru menunggu disposisi QC.");
            }

            if (!string.Equals(unit.Status, UnitStatus.Retest, StringComparison.Ordinal))
            {
                throw FoundationErrors.Conflict("NG_DUPLICATE",
                    "Kejadian NG sebelumnya belum ditutup dengan rework selesai.");
            }

            var status = NgAttemptRules.StatusAfterNg(unit.Attempts, cmd.MaxAttempts);
            var disposition = NgAttemptRules.DispositionAfterNg(unit.Attempts, cmd.MaxAttempts);
            var seq = (int)Sql.ScalarLong(connection,
                "SELECT COALESCE(MAX(seq), 0) + 1 FROM unit_ng_events WHERE unit_id = $unitId;",
                ("$unitId", unit.Id));

            var eventId = NewId();
            InsertNgEvent(connection, eventId, unit.Id, seq, unit.Attempts, disposition,
                unit.LabelId ?? cmd.LabelId, cmd);

            var changed = Sql.ExecuteCount(connection,
                "UPDATE units SET status = $status, stage_code = $stage, last_event_at_utc = $now, updated_at_utc = $now " +
                "WHERE id = $id AND status = 'REWORK_RETEST';",
                ("$status", status), ("$stage", cmd.StageCode), ("$now", cmd.NowUtc), ("$id", unit.Id));
            if (changed != 1)
            {
                throw FoundationErrors.Conflict("STATE_CHANGED",
                    "Status unit berubah saat transaksi berjalan; pindai ulang sekali lagi.");
            }

            SealKey(connection, cmd.Key, unit.Id, eventId);
            transaction.Commit();
            return new NgWriteOutcome { UnitId = unit.Id, RecordId = eventId };
        }
        catch (SqliteException exception) when (IsKeyCollision(exception))
        {
            SafeRollback(transaction);
            return Replay(connection, cmd);
        }
        catch
        {
            SafeRollback(transaction);
            throw;
        }
    }

    /// <summary>
    /// Rework selesai: satu percobaan ditambahkan, unit kembali ke Uji ulang. Percobaan
    /// yang sudah habis (batas tiga) ditolak — unit tetap Tahan QC.
    /// </summary>
    public NgWriteOutcome RecordRework(NgWriteCommand cmd)
    {
        using var connection = _db.OpenConnection();
        using var transaction = connection.BeginTransaction();
        try
        {
            InsertKey(connection, cmd);

            var unit = ResolveUnitCore(connection, cmd.UnitRef)
                ?? throw FoundationErrors.Conflict("UNIT_UNKNOWN",
                    $"Unit {cmd.UnitRef} tidak ditemukan.");

            if (string.Equals(unit.Status, UnitStatus.OkFinal, StringComparison.Ordinal))
            {
                throw FoundationErrors.Conflict("UNIT_FINISHED",
                    "Unit ini sudah lulus OK akhir; tidak ada rework yang perlu dicatat.");
            }

            if (string.Equals(unit.Status, UnitStatus.QcHold, StringComparison.Ordinal))
            {
                throw FoundationErrors.Conflict("QC_HOLD",
                    "Unit ini dalam Tahan QC; percobaan rework sudah habis.");
            }

            if (string.Equals(unit.Status, UnitStatus.Retest, StringComparison.Ordinal))
            {
                throw FoundationErrors.Conflict("REWORK_ALREADY",
                    "Perbaikan unit ini sudah tercatat dan menunggu scan OK.");
            }

            if (!NgAttemptRules.CanRecordRework(unit.Attempts, cmd.MaxAttempts))
            {
                throw FoundationErrors.Conflict("QC_HOLD",
                    $"Percobaan rework sudah {unit.Attempts} dari batas {cmd.MaxAttempts}; unit menunggu disposisi QC.");
            }

            var lastNg = LastNgEventCore(connection, unit.Id)
                ?? throw FoundationErrors.Conflict("NG_EVENT_MISSING",
                    "Unit ini belum memiliki kejadian NG; catat NG terlebih dahulu.");

            var attemptNo = unit.Attempts + 1;
            var reworkId = NewId();
            Sql.Execute(connection,
                "INSERT INTO unit_rework_events (id, unit_id, attempt_no, ng_event_id, action, " +
                "actor_account_id, actor_name, shift_code, line_code, session_id, note, performed_at_utc) " +
                "VALUES ($id, $unitId, $attempt, $ngEventId, $action, $accountId, $actor, $shift, $line, " +
                "$session, $note, $now);",
                ("$id", reworkId), ("$unitId", unit.Id), ("$attempt", attemptNo),
                ("$ngEventId", lastNg.Id), ("$action", cmd.ReworkAction),
                ("$accountId", cmd.ActorAccountId), ("$actor", cmd.ActorName),
                ("$shift", cmd.ShiftCode), ("$line", cmd.LineCode), ("$session", cmd.SessionId),
                ("$note", cmd.Note), ("$now", cmd.NowUtc));

            var changed = Sql.ExecuteCount(connection,
                "UPDATE units SET attempts = $attempt, status = 'REWORK_RETEST', " +
                "last_event_at_utc = $now, updated_at_utc = $now " +
                "WHERE id = $id AND status = 'NG_QUARANTINE' AND attempts = $current;",
                ("$attempt", attemptNo), ("$now", cmd.NowUtc), ("$id", unit.Id),
                ("$current", unit.Attempts));
            if (changed != 1)
            {
                throw FoundationErrors.Conflict("STATE_CHANGED",
                    "Status unit berubah saat transaksi berjalan; pindai ulang sekali lagi.");
            }

            SealKey(connection, cmd.Key, unit.Id, reworkId);
            transaction.Commit();
            return new NgWriteOutcome { UnitId = unit.Id, RecordId = reworkId };
        }
        catch (SqliteException exception) when (IsKeyCollision(exception))
        {
            SafeRollback(transaction);
            return Replay(connection, cmd);
        }
        catch
        {
            SafeRollback(transaction);
            throw;
        }
    }

    /// <summary>
    /// Scan OK biasa: serial baru, belum dikenal sebagai unit NG dan belum pernah OK.
    /// Unit langsung tercatat OK akhir tanpa label karantina.
    /// </summary>
    public NgWriteOutcome RecordOkSerial(NgWriteCommand cmd)
    {
        using var connection = _db.OpenConnection();
        using var transaction = connection.BeginTransaction();
        try
        {
            InsertKey(connection, cmd);

            var serial = cmd.Serial ?? string.Empty;
            var existing = FindUnitBySerialCore(connection, serial);
            if (existing is not null)
            {
                if (string.Equals(existing.Status, UnitStatus.OkFinal, StringComparison.Ordinal))
                {
                    throw FoundationErrors.Conflict("SERIAL_FINISHED",
                        $"Serial {serial} sudah lulus OK akhir; tidak dapat dipindai ulang.");
                }

                throw FoundationErrors.Conflict("SERIAL_NG_UNIT",
                    $"Serial {serial} sudah tercatat sebagai unit NG; selesaikan lewat mode rework.");
            }

            if (FindOkScanBySerialCore(connection, serial) is not null)
            {
                throw FoundationErrors.Conflict("SERIAL_FINISHED",
                    $"Serial {serial} sudah pernah tercatat OK akhir.");
            }

            var unitId = NewId();
            var okId = NewId();
            Sql.Execute(connection,
                "INSERT INTO units (id, serial, label_id, status, attempts, family_code, sku_code, stage_code, " +
                "target_wo_id, target_wo_number, target_client, target_session_id, " +
                "first_ng_at_utc, last_event_at_utc, ok_at_utc, ok_actor_name, created_at_utc, updated_at_utc) " +
                "VALUES ($id, $serial, NULL, 'OK_FINAL', 0, $family, $sku, '', " +
                "$woId, $woNumber, $client, $session, NULL, $now, $now, $actor, $now, $now);",
                ("$id", unitId), ("$serial", serial), ("$family", cmd.FamilyCode), ("$sku", cmd.SkuCode),
                ("$woId", cmd.WorkOrderId), ("$woNumber", cmd.WorkOrderNumber),
                ("$client", cmd.WorkOrderClient), ("$session", cmd.SessionId),
                ("$now", cmd.NowUtc), ("$actor", cmd.ActorName));

            Sql.Execute(connection,
                "INSERT INTO unit_ok_scans (id, unit_id, serial, label_id, mode, wo_target_id, wo_target_number, " +
                "client, actor_account_id, actor_name, shift_code, line_code, sku_code, session_id, occurred_at_utc) " +
                "VALUES ($id, $unitId, $serial, NULL, $mode, $woId, $woNumber, $client, $accountId, $actor, " +
                "$shift, $line, $sku, $session, $now);",
                ("$id", okId), ("$unitId", unitId), ("$serial", serial),
                ("$mode", OkScanMode.SerialOnly), ("$woId", cmd.WorkOrderId),
                ("$woNumber", cmd.WorkOrderNumber), ("$client", cmd.WorkOrderClient),
                ("$accountId", cmd.ActorAccountId), ("$actor", cmd.ActorName),
                ("$shift", cmd.ShiftCode), ("$line", cmd.LineCode), ("$sku", cmd.SkuCode),
                ("$session", cmd.SessionId), ("$now", cmd.NowUtc));

            SealKey(connection, cmd.Key, unitId, okId);
            transaction.Commit();
            return new NgWriteOutcome { UnitId = unitId, RecordId = okId };
        }
        catch (SqliteException exception) when (IsKeyCollision(exception))
        {
            SafeRollback(transaction);
            return Replay(connection, cmd);
        }
        catch (SqliteException exception) when (IsSerialCollision(exception))
        {
            SafeRollback(transaction);
            throw FoundationErrors.Conflict("SERIAL_FINISHED",
                $"Serial {cmd.Serial} baru saja tercatat pada transaksi lain.");
        }
        catch
        {
            SafeRollback(transaction);
            throw;
        }
    }

    /// <summary>
    /// OK hasil rework: pasangan label (Uji ulang) + serial baru + WO tujuan pada satu
    /// transaksi. WO tujuan wajib berbeda dari WO asal unit bila asalnya diketahui.
    /// </summary>
    public NgWriteOutcome RecordOkRework(NgWriteCommand cmd)
    {
        using var connection = _db.OpenConnection();
        using var transaction = connection.BeginTransaction();
        try
        {
            InsertKey(connection, cmd);

            if (cmd.LabelId.Length == 0)
            {
                throw FoundationErrors.Conflict("LABEL_REQUIRED",
                    "Mode rework membutuhkan QR label internal dan serial produk.");
            }

            var labelStatus = Sql.ScalarText(connection,
                "SELECT status FROM ng_labels WHERE id = $id;", ("$id", cmd.LabelId));
            if (labelStatus is null)
            {
                throw FoundationErrors.Conflict("LABEL_UNKNOWN",
                    $"Label {cmd.LabelId} belum pernah diterbitkan.");
            }

            if (string.Equals(labelStatus, LabelStatus.Void, StringComparison.Ordinal))
            {
                throw FoundationErrors.Conflict("LABEL_VOID", $"Label {cmd.LabelId} sudah dibatalkan.");
            }

            var unit = FindUnitByLabelCore(connection, cmd.LabelId)
                ?? throw FoundationErrors.Conflict("LABEL_UNBOUND",
                    $"Label {cmd.LabelId} tidak terikat ke unit NG mana pun; gunakan mode serial.");

            if (string.Equals(unit.Status, UnitStatus.OkFinal, StringComparison.Ordinal))
            {
                throw FoundationErrors.Conflict("UNIT_FINISHED",
                    "Unit ini sudah lulus OK akhir; tidak dapat dipindai ulang.");
            }

            if (string.Equals(unit.Status, UnitStatus.QcHold, StringComparison.Ordinal))
            {
                throw FoundationErrors.Conflict("QC_HOLD",
                    "Unit ini dalam Tahan QC; OK akhir menunggu disposisi QC.");
            }

            if (!string.Equals(unit.Status, UnitStatus.Retest, StringComparison.Ordinal))
            {
                throw FoundationErrors.Conflict("OK_NOT_READY",
                    "Unit masih menunggu rework selesai; OK akhir belum dapat dicatat.");
            }

            if (unit.Serial is not null)
            {
                throw FoundationErrors.Conflict("UNIT_FINISHED",
                    $"Unit ini sudah memiliki serial {unit.Serial}.");
            }

            var serial = cmd.Serial ?? string.Empty;
            if (unit.OriginWorkOrderNumber is not null && cmd.WorkOrderNumber is not null &&
                string.Equals(unit.OriginWorkOrderNumber, cmd.WorkOrderNumber, StringComparison.OrdinalIgnoreCase))
            {
                throw FoundationErrors.Conflict("WO_SAME",
                    $"WO tujuan {cmd.WorkOrderNumber} sama dengan WO asal unit; pilih WO tujuan lain.");
            }

            var other = FindUnitBySerialCore(connection, serial);
            if (other is not null && !string.Equals(other.Id, unit.Id, StringComparison.Ordinal))
            {
                throw FoundationErrors.Conflict("SERIAL_BOUND_OTHER",
                    $"Serial {serial} sudah terikat ke unit lain.");
            }

            if (FindOkScanBySerialCore(connection, serial) is not null)
            {
                throw FoundationErrors.Conflict("SERIAL_FINISHED",
                    $"Serial {serial} sudah pernah tercatat OK akhir.");
            }

            var changed = Sql.ExecuteCount(connection,
                "UPDATE units SET serial = $serial, status = 'OK_FINAL', target_wo_id = $woId, " +
                "target_wo_number = $woNumber, target_client = $client, target_session_id = $session, " +
                "ok_at_utc = $now, ok_actor_name = $actor, last_event_at_utc = $now, updated_at_utc = $now " +
                "WHERE id = $id AND status = 'REWORK_RETEST' AND serial IS NULL;",
                ("$serial", serial), ("$woId", cmd.WorkOrderId), ("$woNumber", cmd.WorkOrderNumber),
                ("$client", cmd.WorkOrderClient), ("$session", cmd.SessionId), ("$now", cmd.NowUtc),
                ("$actor", cmd.ActorName), ("$id", unit.Id));
            if (changed != 1)
            {
                throw FoundationErrors.Conflict("STATE_CHANGED",
                    "Status unit berubah saat transaksi berjalan; pindai ulang sekali lagi.");
            }

            Sql.Execute(connection,
                "UPDATE ng_labels SET bound_serial = $serial WHERE id = $labelId AND bound_serial IS NULL;",
                ("$serial", serial), ("$labelId", cmd.LabelId));

            var okId = NewId();
            Sql.Execute(connection,
                "INSERT INTO unit_ok_scans (id, unit_id, serial, label_id, mode, wo_target_id, wo_target_number, " +
                "client, actor_account_id, actor_name, shift_code, line_code, sku_code, session_id, occurred_at_utc) " +
                "VALUES ($id, $unitId, $serial, $labelId, $mode, $woId, $woNumber, $client, $accountId, $actor, " +
                "$shift, $line, $sku, $session, $now);",
                ("$id", okId), ("$unitId", unit.Id), ("$serial", serial), ("$labelId", cmd.LabelId),
                ("$mode", OkScanMode.ReworkPair), ("$woId", cmd.WorkOrderId),
                ("$woNumber", cmd.WorkOrderNumber), ("$client", cmd.WorkOrderClient),
                ("$accountId", cmd.ActorAccountId), ("$actor", cmd.ActorName),
                ("$shift", cmd.ShiftCode), ("$line", cmd.LineCode), ("$sku", cmd.SkuCode),
                ("$session", cmd.SessionId), ("$now", cmd.NowUtc));

            SealKey(connection, cmd.Key, unit.Id, okId);
            transaction.Commit();
            return new NgWriteOutcome { UnitId = unit.Id, RecordId = okId };
        }
        catch (SqliteException exception) when (IsKeyCollision(exception))
        {
            SafeRollback(transaction);
            return Replay(connection, cmd);
        }
        catch (SqliteException exception) when (IsSerialCollision(exception))
        {
            SafeRollback(transaction);
            throw FoundationErrors.Conflict("SERIAL_BOUND_OTHER",
                $"Serial {cmd.Serial} baru saja terikat ke unit lain.");
        }
        catch
        {
            SafeRollback(transaction);
            throw;
        }
    }

    // -----------------------------------------------------------------------
    // Internal
    // -----------------------------------------------------------------------

    private static void InsertNgEvent(
        SqliteConnection connection, string eventId, string unitId, int seq, int attemptNo,
        string disposition, string labelId, NgWriteCommand cmd)
    {
        Sql.Execute(connection,
            "INSERT INTO unit_ng_events (id, unit_id, seq, label_id, stage_id, stage_code, stage_name, " +
            "defect_id, defect_group, defect_name, attempt_no, disposition, wo_origin_id, wo_origin_number, " +
            "actor_account_id, actor_name, shift_code, line_code, sku_code, family_code, session_id, note, " +
            "occurred_at_utc) " +
            "VALUES ($id, $unitId, $seq, $labelId, $stageId, $stageCode, $stageName, " +
            "$defectId, $defectGroup, $defectName, $attempt, $disposition, $woId, $woNumber, " +
            "$accountId, $actor, $shift, $line, $sku, $family, $session, $note, $now);",
            ("$id", eventId), ("$unitId", unitId), ("$seq", seq), ("$labelId", labelId),
            ("$stageId", cmd.StageId), ("$stageCode", cmd.StageCode), ("$stageName", cmd.StageName),
            ("$defectId", cmd.DefectId), ("$defectGroup", cmd.DefectGroup), ("$defectName", cmd.DefectName),
            ("$attempt", attemptNo), ("$disposition", disposition),
            ("$woId", cmd.WorkOrderId), ("$woNumber", cmd.WorkOrderNumber),
            ("$accountId", cmd.ActorAccountId), ("$actor", cmd.ActorName),
            ("$shift", cmd.ShiftCode), ("$line", cmd.LineCode), ("$sku", cmd.SkuCode),
            ("$family", cmd.FamilyCode), ("$session", cmd.SessionId), ("$note", cmd.Note),
            ("$now", cmd.NowUtc));
    }

    /// <summary>Kunci ditulis paling awal supaya duplikat paralel gagal sebelum menyentuh stok.</summary>
    private static void InsertKey(SqliteConnection connection, NgWriteCommand cmd)
    {
        Sql.Execute(connection,
            "INSERT INTO scan_request_keys (key, operation, request_hash, unit_id, result_ref, created_at_utc) " +
            "VALUES ($key, $op, $hash, NULL, NULL, $now);",
            ("$key", cmd.Key), ("$op", cmd.Operation), ("$hash", cmd.Hash), ("$now", cmd.NowUtc));
    }

    private static void SealKey(SqliteConnection connection, string key, string unitId, string resultRef)
    {
        Sql.Execute(connection,
            "UPDATE scan_request_keys SET unit_id = $unitId, result_ref = $ref WHERE key = $key;",
            ("$unitId", unitId), ("$ref", resultRef), ("$key", key));
    }

    private static ScanRequestKeyRecord? FindKeyCore(SqliteConnection connection, string key) =>
        Sql.ReadOne(connection,
            "SELECT key, operation, request_hash, unit_id, result_ref, created_at_utc " +
            "FROM scan_request_keys WHERE key = $key;",
            (reader) => new ScanRequestKeyRecord
            {
                Key = Sql.Text(reader, "key"),
                Operation = Sql.Text(reader, "operation"),
                RequestHash = Sql.Text(reader, "request_hash"),
                UnitId = Sql.NullableText(reader, "unit_id"),
                ResultRef = Sql.NullableText(reader, "result_ref"),
                CreatedAtUtc = Sql.Text(reader, "created_at_utc"),
            },
            ("$key", key));

    /// <summary>Tabrakan kunci = permintaan yang sama sedang/sudah diproses; hasilnya diputar ulang.</summary>
    private static NgWriteOutcome Replay(SqliteConnection connection, NgWriteCommand cmd)
    {
        var row = FindKeyCore(connection, cmd.Key)
            ?? throw FoundationErrors.Conflict("DB_BUSY",
                "Permintaan tidak dapat disimpan karena transaksi lain berjalan; ulangi sekali lagi.");

        if (!string.Equals(row.Operation, cmd.Operation, StringComparison.Ordinal) ||
            !string.Equals(row.RequestHash, cmd.Hash, StringComparison.Ordinal))
        {
            throw FoundationErrors.Conflict("IDEMPOTENCY_CONFLICT",
                "Kunci idempotensi ini sudah dipakai untuk permintaan yang berbeda.");
        }

        return new NgWriteOutcome
        {
            Reused = true,
            UnitId = row.UnitId ?? string.Empty,
            RecordId = row.ResultRef ?? string.Empty,
        };
    }

    private static UnitRecord? ResolveUnitCore(SqliteConnection connection, string reference)
    {
        var text = (reference ?? string.Empty).Trim();
        if (text.Length == 0) return null;
        var labelId = NgLabelRules.NormalizeLabelId(text) ?? text;
        return Sql.ReadOne(connection,
            $"{UnitSelect} WHERE u.id = $ref OR u.label_id = $labelId " +
            "OR (u.serial IS NOT NULL AND u.serial = $ref COLLATE NOCASE) LIMIT 1;",
            MapUnit, ("$ref", text), ("$labelId", labelId));
    }

    private static UnitRecord? FindUnitByLabelCore(SqliteConnection connection, string labelId) =>
        string.IsNullOrWhiteSpace(labelId)
            ? null
            : Sql.ReadOne(connection, $"{UnitSelect} WHERE u.label_id = $labelId;", MapUnit,
                ("$labelId", labelId));

    private static UnitRecord? FindUnitBySerialCore(SqliteConnection connection, string serial) =>
        string.IsNullOrWhiteSpace(serial)
            ? null
            : Sql.ReadOne(connection, $"{UnitSelect} WHERE u.serial = $serial COLLATE NOCASE;", MapUnit,
                ("$serial", serial));

    private static NgEventRecord? LastNgEventCore(SqliteConnection connection, string unitId) =>
        Sql.ReadOne(connection, $"{NgEventSelect} WHERE unit_id = $unitId ORDER BY seq DESC LIMIT 1;",
            MapNgEvent, ("$unitId", unitId));

    private static List<NgEventRecord> NgEventsCore(SqliteConnection connection, string unitId) =>
        Sql.ReadList(connection, $"{NgEventSelect} WHERE unit_id = $unitId ORDER BY seq;",
            MapNgEvent, ("$unitId", unitId));

    private static List<ReworkEventRecord> ReworkEventsCore(SqliteConnection connection, string unitId) =>
        Sql.ReadList(connection, $"{ReworkSelect} WHERE unit_id = $unitId ORDER BY attempt_no;",
            MapRework, ("$unitId", unitId));

    private static OkScanRecord? FindOkScanCore(SqliteConnection connection, string unitId) =>
        Sql.ReadOne(connection, $"{OkSelect} WHERE unit_id = $unitId;", MapOk, ("$unitId", unitId));

    private static OkScanRecord? FindOkScanBySerialCore(SqliteConnection connection, string serial) =>
        string.IsNullOrWhiteSpace(serial)
            ? null
            : Sql.ReadOne(connection, $"{OkSelect} WHERE serial = $serial COLLATE NOCASE;", MapOk,
                ("$serial", serial));

    private static string? LikeTerm(string? search)
    {
        if (string.IsNullOrWhiteSpace(search)) return null;
        var escaped = search.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
        return $"%{escaped}%";
    }

    private static bool IsKeyCollision(SqliteException exception) =>
        exception.SqliteErrorCode == 19 &&
        exception.Message.Contains("scan_request_keys.key", StringComparison.OrdinalIgnoreCase);

    private static bool IsSerialCollision(SqliteException exception) =>
        exception.SqliteErrorCode == 19 &&
        (exception.Message.Contains("units.serial", StringComparison.OrdinalIgnoreCase) ||
         exception.Message.Contains("unit_ok_scans.serial", StringComparison.OrdinalIgnoreCase));

    private static void SafeRollback(SqliteTransaction transaction)
    {
        try
        {
            transaction.Rollback();
        }
        catch (InvalidOperationException)
        {
            // Transaksi sudah diselesaikan (auto-rollback SQLite).
        }
        catch (SqliteException)
        {
            // Koneksi sudah ditutup; rollback tidak dapat dilakukan lagi.
        }
    }

    private static string NewId() => Guid.NewGuid().ToString("N");

    private static UnitRecord MapUnit(SqliteDataReader reader) => new()
    {
        Id = Sql.Text(reader, "id"),
        Serial = Sql.NullableText(reader, "serial"),
        LabelId = Sql.NullableText(reader, "label_id"),
        LabelPayload = Sql.NullableText(reader, "label_payload"),
        Status = Sql.Text(reader, "status"),
        Attempts = Sql.Int(reader, "attempts"),
        FamilyCode = Sql.Text(reader, "family_code"),
        SkuCode = Sql.Text(reader, "sku_code"),
        StageCode = Sql.Text(reader, "stage_code"),
        OriginWorkOrderId = Sql.NullableText(reader, "origin_wo_id"),
        OriginWorkOrderNumber = Sql.NullableText(reader, "origin_wo_number"),
        OriginClient = Sql.NullableText(reader, "origin_client"),
        OriginSessionId = Sql.NullableText(reader, "origin_session_id"),
        TargetWorkOrderId = Sql.NullableText(reader, "target_wo_id"),
        TargetWorkOrderNumber = Sql.NullableText(reader, "target_wo_number"),
        TargetClient = Sql.NullableText(reader, "target_client"),
        TargetSessionId = Sql.NullableText(reader, "target_session_id"),
        FirstNgAtUtc = Sql.NullableText(reader, "first_ng_at_utc") ?? string.Empty,
        LastEventAtUtc = Sql.Text(reader, "last_event_at_utc"),
        OkAtUtc = Sql.NullableText(reader, "ok_at_utc"),
        OkActorName = Sql.NullableText(reader, "ok_actor_name"),
        CreatedAtUtc = Sql.Text(reader, "created_at_utc"),
        UpdatedAtUtc = Sql.Text(reader, "updated_at_utc"),
    };

    private static NgEventRecord MapNgEvent(SqliteDataReader reader) => new()
    {
        Id = Sql.Text(reader, "id"),
        UnitId = Sql.Text(reader, "unit_id"),
        Seq = Sql.Int(reader, "seq"),
        LabelId = Sql.Text(reader, "label_id"),
        StageId = Sql.Text(reader, "stage_id"),
        StageCode = Sql.Text(reader, "stage_code"),
        StageName = Sql.Text(reader, "stage_name"),
        DefectId = Sql.Text(reader, "defect_id"),
        DefectGroup = Sql.Text(reader, "defect_group"),
        DefectName = Sql.Text(reader, "defect_name"),
        AttemptNo = Sql.Int(reader, "attempt_no"),
        Disposition = Sql.Text(reader, "disposition"),
        WorkOrderOriginId = Sql.NullableText(reader, "wo_origin_id"),
        WorkOrderOriginNumber = Sql.NullableText(reader, "wo_origin_number"),
        ActorAccountId = Sql.Text(reader, "actor_account_id"),
        ActorName = Sql.Text(reader, "actor_name"),
        ShiftCode = Sql.Text(reader, "shift_code"),
        LineCode = Sql.Text(reader, "line_code"),
        SkuCode = Sql.Text(reader, "sku_code"),
        FamilyCode = Sql.Text(reader, "family_code"),
        SessionId = Sql.Text(reader, "session_id"),
        Note = Sql.Text(reader, "note"),
        OccurredAtUtc = Sql.Text(reader, "occurred_at_utc"),
    };

    private static ReworkEventRecord MapRework(SqliteDataReader reader) => new()
    {
        Id = Sql.Text(reader, "id"),
        UnitId = Sql.Text(reader, "unit_id"),
        AttemptNo = Sql.Int(reader, "attempt_no"),
        NgEventId = Sql.Text(reader, "ng_event_id"),
        Action = Sql.Text(reader, "action"),
        ActorAccountId = Sql.Text(reader, "actor_account_id"),
        ActorName = Sql.Text(reader, "actor_name"),
        ShiftCode = Sql.Text(reader, "shift_code"),
        LineCode = Sql.Text(reader, "line_code"),
        SessionId = Sql.Text(reader, "session_id"),
        Note = Sql.Text(reader, "note"),
        PerformedAtUtc = Sql.Text(reader, "performed_at_utc"),
    };

    private static OkScanRecord MapOk(SqliteDataReader reader) => new()
    {
        Id = Sql.Text(reader, "id"),
        UnitId = Sql.Text(reader, "unit_id"),
        Serial = Sql.Text(reader, "serial"),
        LabelId = Sql.NullableText(reader, "label_id"),
        Mode = Sql.Text(reader, "mode"),
        WorkOrderTargetId = Sql.NullableText(reader, "wo_target_id"),
        WorkOrderTargetNumber = Sql.NullableText(reader, "wo_target_number"),
        Client = Sql.NullableText(reader, "client"),
        ActorAccountId = Sql.Text(reader, "actor_account_id"),
        ActorName = Sql.Text(reader, "actor_name"),
        ShiftCode = Sql.Text(reader, "shift_code"),
        LineCode = Sql.Text(reader, "line_code"),
        SkuCode = Sql.Text(reader, "sku_code"),
        SessionId = Sql.Text(reader, "session_id"),
        OccurredAtUtc = Sql.Text(reader, "occurred_at_utc"),
    };
}
