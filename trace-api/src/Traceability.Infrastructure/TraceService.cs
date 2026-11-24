using System.Text.RegularExpressions;
using Traceability.Application;
using Traceability.Domain;

namespace Traceability.Infrastructure;

public sealed class TraceService : ITraceService
{
    private static readonly TimeSpan PatternTimeout = TimeSpan.FromMilliseconds(250);

    private readonly ITraceStore _store;

    public TraceService(ITraceStore store)
    {
        _store = store;
    }

    public ScanSnapshot GetScanState(OperatorContext context)
    {
        lock (_store.Gate)
        {
            var day = EnsureActiveDay(context);
            return SnapshotBuilder.Build(_store.State, day, context);
        }
    }

    public ScanResult PerformScan(ScanInput input, OperatorContext context)
    {
        lock (_store.Gate)
        {
            var store = _store.State;
            var day = EnsureActiveDay(context);
            var config = store.Config;
            var now = Wib.Now();
            var timestamp = Wib.FullTimestamp(now);
            var codeContext = TraceHelpers.CodeContextFor(config, context);
            var sn = TraceHelpers.NormalizeUpperOrEmpty(input.Sn);

            ScanResult Reply(bool ok, string message, string tone, BridgePayload? autoBridge = null)
            {
                return new ScanResult
                {
                    Ok = ok,
                    Message = message,
                    Tone = tone,
                    AutoBridge = autoBridge,
                    Snapshot = SnapshotBuilder.Build(store, day, context),
                };
            }

            if (sn.Length < 4)
            {
                return Reply(false, "Serial Number tidak valid (minimal 4 karakter).", "error");
            }

            if (!string.IsNullOrWhiteSpace(config.SnPattern))
            {
                try
                {
                    if (!Regex.IsMatch(sn, config.SnPattern, RegexOptions.None, PatternTimeout))
                    {
                        return Reply(false, $"Format SN {sn} tidak sesuai pola ({config.SnPattern}).", "error");
                    }
                }
                catch (ArgumentException)
                {
                    // Pola tidak valid diabaikan supaya operator tetap bisa melanjutkan produksi.
                }
                catch (RegexMatchTimeoutException)
                {
                    // Pola terlalu mahal untuk dievaluasi; produksi tidak boleh terhenti.
                }
            }

            store.LastSyncAt = TraceHelpers.NowIso();

            if (string.Equals(input.Mode, "OK", StringComparison.OrdinalIgnoreCase))
            {
                return HandleOk(store, day, context, config, codeContext, sn, timestamp, now, Reply);
            }

            if (string.Equals(input.Mode, "NG", StringComparison.OrdinalIgnoreCase))
            {
                return HandleNg(store, day, context, config, sn, timestamp, input, Reply);
            }

            return HandleRework(store, day, context, config, codeContext, sn, timestamp, now, input, Reply);
        }
    }

    public ScanSnapshot MarkLabelsPrinted(OperatorContext context)
    {
        lock (_store.Gate)
        {
            var store = _store.State;
            var day = EnsureActiveDay(context);
            var printedAt = Wib.FullTimestamp();
            day.InnerPrintedTime = printedAt;
            day.OuterPrintedTime = printedAt;

            if (day.CompletedInners.Count > 0)
            {
                var latestInner = day.CompletedInners[^1];
                DayLifecycle.Log(store, new BarcodeHistoryEntry
                {
                    Type = "INNER",
                    Id = latestInner.Id,
                    Sku = latestInner.Sku,
                    Destination = latestInner.Destination,
                    Qty = latestInner.Items.Count,
                    Event = "PRINT",
                    Time = printedAt,
                    IsoDate = day.IsoDate,
                });
            }

            if (day.CompletedOuters.Count > 0)
            {
                var latestOuter = day.CompletedOuters[^1];
                DayLifecycle.Log(store, new BarcodeHistoryEntry
                {
                    Type = "OUTER",
                    Id = latestOuter.Id,
                    Sku = latestOuter.Sku,
                    Destination = latestOuter.Destination,
                    Qty = latestOuter.TotalPcs,
                    Event = "PRINT",
                    Time = printedAt,
                    IsoDate = day.IsoDate,
                });
            }

            store.LastSyncAt = TraceHelpers.NowIso();
            return SnapshotBuilder.Build(store, day, context);
        }
    }

    public ScanSnapshot ClearActiveBatch(OperatorContext context)
    {
        lock (_store.Gate)
        {
            var store = _store.State;
            var day = EnsureActiveDay(context);
            day.CurrentBatch.Clear();
            store.LastSyncAt = TraceHelpers.NowIso();
            return SnapshotBuilder.Build(store, day, context);
        }
    }

    public ScanSnapshot RemoveBatchItem(string sn, OperatorContext context)
    {
        lock (_store.Gate)
        {
            var store = _store.State;
            var day = EnsureActiveDay(context);
            var key = TraceHelpers.NormalizeUpperOrEmpty(sn);
            var index = day.CurrentBatch.FindIndex(item => string.Equals(item.Sn, key, StringComparison.Ordinal));
            if (index >= 0) day.CurrentBatch.RemoveAt(index);
            store.LastSyncAt = TraceHelpers.NowIso();
            return SnapshotBuilder.Build(store, day, context);
        }
    }

    public BridgePayload BuildLatestBridge(OperatorContext context)
    {
        lock (_store.Gate)
        {
            var store = _store.State;
            var day = EnsureActiveDay(context);
            var config = store.Config;
            var codeContext = TraceHelpers.CodeContextFor(config, context);

            if (day.CompletedInners.Count > 0)
            {
                var latestInner = day.CompletedInners[^1];
                return BarcodeRules.BuildBridgePayload(
                    config.ExternalPrinterMode,
                    new BarcodeTarget
                    {
                        BoxId = latestInner.Id,
                        Sku = latestInner.Sku,
                        WorkOrder = latestInner.WorkOrder,
                        MaterialLot = latestInner.MaterialLot,
                        QtyText = $"{latestInner.Items.Count} PCS",
                        Destination = latestInner.Destination,
                        PrintTime = string.IsNullOrEmpty(day.InnerPrintedTime) ? latestInner.Time : day.InnerPrintedTime!,
                        Line = context.Line,
                        Operator = context.Operator,
                    },
                    config);
            }

            if (day.CompletedOuters.Count > 0)
            {
                var latestOuter = day.CompletedOuters[^1];
                return BarcodeRules.BuildBridgePayload(
                    config.ExternalPrinterMode,
                    new BarcodeTarget
                    {
                        BoxId = latestOuter.Id,
                        Sku = latestOuter.Sku,
                        WorkOrder = latestOuter.WorkOrder,
                        MaterialLot = latestOuter.MaterialLot,
                        QtyText = $"{latestOuter.TotalPcs} PCS",
                        Destination = latestOuter.Destination,
                        PrintTime = string.IsNullOrEmpty(day.OuterPrintedTime) ? latestOuter.Time : day.OuterPrintedTime!,
                        Line = context.Line,
                        Operator = context.Operator,
                    },
                    config);
            }

            return BarcodeRules.BuildBridgePayload(
                config.ExternalPrinterMode,
                new BarcodeTarget
                {
                    BoxId = CodeFormatter.FormatCode(config.InnerCodeFormat, day.InnerSeq, codeContext),
                    Sku = context.Sku,
                    WorkOrder = context.WorkOrder,
                    MaterialLot = context.MaterialLot,
                    QtyText = $"{day.CurrentBatch.Count} PCS",
                    Destination = context.Destination,
                    PrintTime = Wib.FullTimestamp(),
                    Line = context.Line,
                    Operator = context.Operator,
                },
                config);
        }
    }

    private DayState EnsureActiveDay(OperatorContext context)
    {
        return DayLifecycle.EnsureActive(_store.State, context);
    }

    private sealed class FinalizeResult
    {
        public string InnerId { get; init; } = string.Empty;
        public string? OuterId { get; init; }
        public BridgePayload? AutoBridge { get; init; }
    }

    private FinalizeResult FinalizeInner(
        TraceStoreState store,
        DayState day,
        OperatorContext context,
        DateTimeOffset now)
    {
        var config = store.Config;
        var codeContext = TraceHelpers.CodeContextFor(config, context);
        var timestamp = Wib.FullTimestamp(now);
        var innerId = CodeFormatter.FormatCode(config.InnerCodeFormat, day.InnerSeq, codeContext, now);
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
            Items = day.CurrentBatch.ToList(),
        };
        day.CompletedInners.Add(inner);
        day.InnerPrintedTime = timestamp;
        day.CurrentBatch.Clear();
        DayLifecycle.Log(store, new BarcodeHistoryEntry
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

        string? outerId = null;
        var pending = TraceHelpers.UnassignedInners(day);
        if (pending.Count >= config.OuterCap)
        {
            outerId = CodeFormatter.FormatCode(config.OuterCodeFormat, day.OuterSeq, codeContext, now);
            day.OuterSeq += 1;
            var grouped = pending.Take(config.OuterCap).ToList();
            var totalPcs = grouped.Sum(box => box.Items.Count);
            day.CompletedOuters.Add(new OuterBox
            {
                Id = outerId,
                DayName = day.DayName,
                IsoDate = day.IsoDate,
                Time = timestamp,
                Operator = context.Operator,
                WorkOrder = context.WorkOrder,
                MaterialLot = CodeFormatter.NormalizeMaterialLotCode(context.MaterialLot),
                Sku = context.Sku,
                Destination = context.Destination,
                Inners = grouped.Select(box => box.Id).ToList(),
                TotalPcs = totalPcs,
            });
            day.OuterPrintedTime = timestamp;
            DayLifecycle.Log(store, new BarcodeHistoryEntry
            {
                Type = "OUTER",
                Id = outerId,
                Sku = context.Sku,
                Destination = context.Destination,
                Qty = totalPcs,
                Event = "FULL",
                Time = timestamp,
                IsoDate = day.IsoDate,
            });
        }

        BridgePayload? autoBridge = null;
        if (config.AutoBridgePrint)
        {
            var targetOuter = outerId is not null ? day.CompletedOuters[^1] : null;
            var boxId = targetOuter?.Id ?? inner.Id;
            var sku = targetOuter?.Sku ?? inner.Sku;
            var workOrder = targetOuter?.WorkOrder ?? inner.WorkOrder;
            var materialLot = targetOuter?.MaterialLot ?? inner.MaterialLot;
            var destination = targetOuter?.Destination ?? inner.Destination;
            var qty = targetOuter?.TotalPcs ?? inner.Items.Count;
            autoBridge = BarcodeRules.BuildBridgePayload(
                config.ExternalPrinterMode,
                new BarcodeTarget
                {
                    BoxId = boxId,
                    Sku = sku,
                    WorkOrder = workOrder,
                    MaterialLot = materialLot,
                    QtyText = $"{qty} PCS",
                    Destination = destination,
                    PrintTime = timestamp,
                    Line = context.Line,
                    Operator = context.Operator,
                },
                config);
        }

        return new FinalizeResult { InnerId = innerId, OuterId = outerId, AutoBridge = autoBridge };
    }

    private ScanResult HandleOk(
        TraceStoreState store,
        DayState day,
        OperatorContext context,
        AppConfig config,
        CodeContext codeContext,
        string sn,
        string timestamp,
        DateTimeOffset now,
        Func<bool, string, string, BridgePayload?, ScanResult> reply)
    {
        var duplicate = day.CurrentBatch.Any(item => string.Equals(item.Sn, sn, StringComparison.Ordinal))
            || day.CompletedInners.Any(box => box.Items.Any(item => string.Equals(item.Sn, sn, StringComparison.Ordinal)));
        if (duplicate)
        {
            return reply(false, $"DOUBLE SCAN DITOLAK: Serial Number {sn} sudah tercatat.", "error", null);
        }

        var quarantined = day.NgItems.FirstOrDefault(item => string.Equals(item.Sn, sn, StringComparison.Ordinal));
        if (quarantined is not null)
        {
            return reply(
                false,
                $"DITOLAK: {sn} masih di Karantina NG ({quarantined.StationLabel}). " +
                "Wajib lulus di Page Scan After Rework terlebih dahulu.",
                "error",
                null);
        }

        var reworkRecord = day.ReworkItems.FirstOrDefault(item => string.Equals(item.Sn, sn, StringComparison.Ordinal));
        day.CurrentBatch.Add(new BatchItem
        {
            Sn = sn,
            DayName = day.DayName,
            IsoDate = day.IsoDate,
            Time = timestamp,
            Operator = context.Operator,
            WorkOrder = context.WorkOrder,
            MaterialLot = CodeFormatter.NormalizeMaterialLotCode(context.MaterialLot),
            Line = context.Line,
            Shift = context.Shift,
            Sku = context.Sku,
            Reworked = reworkRecord is not null,
            ReworkCycle = store.ReworkCounts.TryGetValue(sn, out var cycles) ? cycles : 0,
            ReworkTicket = reworkRecord?.TicketId ?? "-",
        });

        if (day.CurrentBatch.Count >= config.InnerCap)
        {
            var finalized = FinalizeInner(store, day, context, now);
            var message = finalized.OuterId is not null
                ? $"INNER BOX {finalized.InnerId} & OUTER BOX {finalized.OuterId} FULL! Barcode 2D siap dicetak."
                : $"INNER BOX {finalized.InnerId} FULL! Barcode Kotak 2D Inner otomatis diperbarui.";
            return reply(true, message, "ok", finalized.AutoBridge);
        }

        return reply(
            true,
            $"SCAN OK [{context.Sku}]: {sn} masuk Inner Box ({day.CurrentBatch.Count}/{config.InnerCap}).",
            "ok",
            null);
    }

    private static ScanResult HandleNg(
        TraceStoreState store,
        DayState day,
        OperatorContext context,
        AppConfig config,
        string sn,
        string timestamp,
        ScanInput input,
        Func<bool, string, string, BridgePayload?, ScanResult> reply)
    {
        var station = config.NgStations.FirstOrDefault(item => string.Equals(item.Id, input.StationId, StringComparison.Ordinal))
            ?? (config.NgStations.Count > 0 ? config.NgStations[0] : new NgStation { Id = "ST-01", Label = "ST-01: Rivet" });
        var reason = !string.IsNullOrWhiteSpace(input.Reason)
            ? input.Reason!.Trim()
            : station.Defects.Count > 0 ? station.Defects[0] : "Other";
        var cycles = store.ReworkCounts.TryGetValue(sn, out var existing) ? existing : 0;
        var disposition = cycles >= config.MaxReworkCycles
            ? "PERMANENT SCRAP"
            : !string.IsNullOrWhiteSpace(input.Disposition) ? input.Disposition! : "REWORK REQUIRED";

        if (day.NgItems.Any(item => string.Equals(item.Sn, sn, StringComparison.Ordinal)))
        {
            return reply(false, $"DOUBLE SCAN NG: Produk {sn} sudah ada di Karantina NG.", "error", null);
        }

        var sealedBox = day.CompletedInners.FirstOrDefault(box => box.Items.Any(item => string.Equals(item.Sn, sn, StringComparison.Ordinal)));
        if (sealedBox is not null)
        {
            return reply(
                false,
                $"DITOLAK: {sn} sudah disegel di {sealedBox.Id}. Buka/bongkar box tersebut dulu sebelum menandai NG.",
                "error",
                null);
        }

        var batchIndex = day.CurrentBatch.FindIndex(item => string.Equals(item.Sn, sn, StringComparison.Ordinal));
        if (batchIndex >= 0) day.CurrentBatch.RemoveAt(batchIndex);

        day.NgItems.Add(new NgItem
        {
            Sn = sn,
            StationId = station.Id,
            StationLabel = station.Label,
            Reason = reason,
            Disposition = disposition,
            ReworkCycle = cycles,
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
        });

        return reply(
            true,
            $"NG QUARANTINE [{station.Label}]: {sn} — {disposition} ({reason}).",
            "error",
            null);
    }

    private ScanResult HandleRework(
        TraceStoreState store,
        DayState day,
        OperatorContext context,
        AppConfig config,
        CodeContext codeContext,
        string sn,
        string timestamp,
        DateTimeOffset now,
        ScanInput input,
        Func<bool, string, string, BridgePayload?, ScanResult> reply)
    {
        var ngIndex = day.NgItems.FindIndex(item => string.Equals(item.Sn, sn, StringComparison.Ordinal));
        if (ngIndex < 0)
        {
            return reply(false, $"DITOLAK: Serial Number {sn} tidak ditemukan di Karantina NG.", "error", null);
        }

        var ng = day.NgItems[ngIndex];
        if (ng.Disposition.Contains("PERMANENT SCRAP", StringComparison.Ordinal))
        {
            return reply(false, $"DITOLAK: Produk {sn} berstatus PERMANENT SCRAP.", "error", null);
        }

        var currentCycles = store.ReworkCounts.TryGetValue(sn, out var stored) ? stored : 0;
        var nextCycle = currentCycles + 1;
        if (nextCycle > config.MaxReworkCycles)
        {
            return reply(
                false,
                $"BATAS REWORK TERLAMPAUI ({config.MaxReworkCycles}x): Produk {sn} wajib di-SCRAP.",
                "error",
                null);
        }

        if (string.Equals(input.Verdict, "FAIL", StringComparison.OrdinalIgnoreCase))
        {
            store.ReworkCounts[sn] = nextCycle;
            ng.ReworkCycle = nextCycle;
            ng.ReworkFails = (ng.ReworkFails ?? 0) + 1;
            if (nextCycle >= config.MaxReworkCycles)
            {
                ng.Disposition = $"PERMANENT SCRAP (REWORK GAGAL {ng.ReworkFails}X)";
            }

            var message = ng.Disposition.Contains("SCRAP", StringComparison.Ordinal)
                ? $"REWORK GAGAL: {sn} mencapai batas {config.MaxReworkCycles}x → PERMANENT SCRAP."
                : $"REWORK GAGAL: {sn} kembali ke Karantina NG (siklus #{nextCycle} dari {config.MaxReworkCycles}).";
            return reply(false, message, "error", null);
        }

        var ticketId = CodeFormatter.FormatCode(config.ReworkCodeFormat, day.ReworkSeq, codeContext, now);
        day.ReworkSeq += 1;
        var prevDefect = ng.Reason;
        var prevStation = ng.StationLabel;
        day.NgItems.RemoveAt(ngIndex);
        store.ReworkCounts[sn] = nextCycle;

        day.ReworkItems.Add(new ReworkItem
        {
            TicketId = ticketId,
            Sn = sn,
            Cycle = nextCycle,
            StationLabel = prevStation,
            PrevDefect = prevDefect,
            Action = !string.IsNullOrWhiteSpace(input.Action)
                ? input.Action!
                : config.Reworks.Count > 0 ? config.Reworks[0] : string.Empty,
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
        });

        return reply(
            true,
            $"AFTER REWORK OK [{ticketId}]: {sn} lulus perbaikan siklus #{nextCycle}. " +
            "Siap di-scan di Page scan produk ok.",
            "rework",
            null);
    }
}
