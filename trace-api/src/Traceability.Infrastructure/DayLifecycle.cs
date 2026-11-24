using Traceability.Domain;

namespace Traceability.Infrastructure;

/// <summary>
/// Siklus hari produksi: memastikan hari aktif tersedia dan menyegel sisa batch
/// hari sebelumnya ketika tanggal WIB berganti.
/// </summary>
internal static class DayLifecycle
{
    public static DayState EnsureActive(TraceStoreState store, OperatorContext context)
    {
        var today = Wib.IsoDate();
        if (!store.Days.TryGetValue(today, out var day))
        {
            day = SeedData.CreateDayState(today);
            store.Days[today] = day;
        }

        if (!string.Equals(store.ActiveDate, today, StringComparison.Ordinal))
        {
            if (store.Days.TryGetValue(store.ActiveDate, out var previous) && store.Config.DailyReset)
            {
                Seal(store, previous, context);
            }

            store.ActiveDate = today;
            store.LastSyncAt = TraceHelpers.NowIso();
        }

        return day;
    }

    public static void Seal(TraceStoreState store, DayState day, OperatorContext context)
    {
        var config = store.Config;
        var timestamp = Wib.MakeWibTimestamp(day.IsoDate, "23:59:59");
        var codeContext = TraceHelpers.CodeContextFor(config, context);

        if (day.CurrentBatch.Count > 0)
        {
            var innerId = CodeFormatter.FormatCode(config.InnerCodeFormat, day.InnerSeq, codeContext);
            day.InnerSeq += 1;
            var inner = new InnerBox
            {
                Id = innerId,
                DayName = day.DayName,
                IsoDate = day.IsoDate,
                Time = timestamp,
                Operator = context.Operator,
                WorkOrder = context.WorkOrder,
                MaterialLot = CodeFormatter.NormalizeMaterialLotCode(context.MaterialLot),
                Line = context.Line,
                Shift = context.Shift,
                Sku = context.Sku,
                Destination = context.Destination,
                Partial = true,
                Items = day.CurrentBatch.ToList(),
            };
            day.CompletedInners.Add(inner);
            day.CurrentBatch.Clear();
            Log(store, new BarcodeHistoryEntry
            {
                Type = "INNER",
                Id = inner.Id,
                Sku = inner.Sku,
                Destination = inner.Destination,
                Qty = inner.Items.Count,
                Event = "FULL",
                Time = timestamp,
                IsoDate = day.IsoDate,
            });
        }

        var rest = TraceHelpers.UnassignedInners(day);
        if (rest.Count == 0) return;

        var outerId = CodeFormatter.FormatCode(config.OuterCodeFormat, day.OuterSeq, codeContext);
        day.OuterSeq += 1;
        var totalPcs = rest.Sum(box => box.Items.Count);
        var last = rest[^1];
        day.CompletedOuters.Add(new OuterBox
        {
            Id = outerId,
            DayName = day.DayName,
            IsoDate = day.IsoDate,
            Time = timestamp,
            Operator = context.Operator,
            WorkOrder = context.WorkOrder,
            MaterialLot = CodeFormatter.NormalizeMaterialLotCode(context.MaterialLot),
            Sku = last.Sku,
            Destination = last.Destination,
            Partial = true,
            Inners = rest.Select(box => box.Id).ToList(),
            TotalPcs = totalPcs,
        });
        Log(store, new BarcodeHistoryEntry
        {
            Type = "OUTER",
            Id = outerId,
            Sku = last.Sku,
            Destination = last.Destination,
            Qty = totalPcs,
            Event = "FULL",
            Time = timestamp,
            IsoDate = day.IsoDate,
        });
    }

    public static void Log(TraceStoreState store, BarcodeHistoryEntry entry)
    {
        store.History.Add(entry);
        if (store.History.Count > 500) store.History.RemoveAt(0);
    }
}
