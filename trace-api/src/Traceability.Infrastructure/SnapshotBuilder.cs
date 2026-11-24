using Traceability.Application;
using Traceability.Domain;

namespace Traceability.Infrastructure;

public static class SnapshotBuilder
{
    private sealed class LabelSource
    {
        public string Code { get; init; } = string.Empty;
        public string Sku { get; init; } = string.Empty;
        public string WorkOrder { get; init; } = string.Empty;
        public string MaterialLot { get; init; } = string.Empty;
        public string Destination { get; init; } = string.Empty;
        public string QtyText { get; init; } = string.Empty;
        public string PrintedAt { get; init; } = string.Empty;
    }

    public static ScanSnapshot Build(TraceStoreState store, DayState day, OperatorContext context)
    {
        var config = store.Config;
        var liveTime = Wib.FullTimestamp();
        var codeContext = TraceHelpers.CodeContextFor(config, context);

        var targetInner = day.CompletedInners.Count > 0 ? day.CompletedInners[^1] : null;
        var innerId = targetInner?.Id ?? CodeFormatter.FormatCode(config.InnerCodeFormat, day.InnerSeq, codeContext);
        var innerSku = targetInner?.Sku ?? (string.IsNullOrEmpty(context.Sku) ? "—" : context.Sku);
        var innerWo = targetInner?.WorkOrder ?? (string.IsNullOrEmpty(context.WorkOrder) ? "—" : context.WorkOrder);
        var innerLot = targetInner?.MaterialLot ?? context.MaterialLot;
        var innerQty = targetInner is not null
            ? $"{targetInner.Items.Count} Pcs (FULL)"
            : $"{day.CurrentBatch.Count} / {config.InnerCap} Pcs";
        var innerTime = !string.IsNullOrEmpty(day.InnerPrintedTime)
            ? day.InnerPrintedTime!
            : targetInner is not null && day.CurrentBatch.Count == 0
                ? targetInner.Time
                : liveTime;
        var innerDest = targetInner?.Destination ?? (string.IsNullOrEmpty(context.Destination) ? "—" : context.Destination);

        var targetOuter = day.CompletedOuters.Count > 0 ? day.CompletedOuters[^1] : null;
        var outerId = targetOuter?.Id ?? CodeFormatter.FormatCode(config.OuterCodeFormat, day.OuterSeq, codeContext);
        var outerSku = targetOuter?.Sku ?? (string.IsNullOrEmpty(context.Sku) ? "—" : context.Sku);
        var outerWo = targetOuter?.WorkOrder ?? (string.IsNullOrEmpty(context.WorkOrder) ? "—" : context.WorkOrder);
        var outerLot = targetOuter?.MaterialLot ?? context.MaterialLot;
        var pendingInners = TraceHelpers.UnassignedInners(day).Count;
        var outerQty = targetOuter is not null
            ? $"{targetOuter.TotalPcs} Pcs ({targetOuter.Inners.Count} Inner • {(targetOuter.Partial ? "PARTIAL" : "FULL")})"
            : $"{pendingInners} / {config.OuterCap} Inner ({config.OuterCap * config.InnerCap} Pcs)";
        var outerTime = !string.IsNullOrEmpty(day.OuterPrintedTime) ? day.OuterPrintedTime! : liveTime;
        var outerDest = targetOuter?.Destination ?? (string.IsNullOrEmpty(context.Destination) ? "—" : context.Destination);

        var completedUnits = day.CompletedInners.Sum(box => box.Items.Count);
        var history = store.History
            .Where(entry => entry.IsoDate == day.IsoDate)
            .TakeLast(24)
            .Reverse()
            .ToList();

        return new ScanSnapshot
        {
            ActiveDate = day.IsoDate,
            ActiveDayName = day.DayName,
            Context = context,
            ActiveInnerId = CodeFormatter.FormatCode(config.InnerCodeFormat, day.InnerSeq, codeContext),
            ActiveOuterId = CodeFormatter.FormatCode(config.OuterCodeFormat, day.OuterSeq, codeContext),
            NextReworkTicket = CodeFormatter.FormatCode(config.ReworkCodeFormat, day.ReworkSeq, codeContext),
            InnerCap = config.InnerCap,
            OuterCap = config.OuterCap,
            ActiveInnerQty = day.CurrentBatch.Count,
            UnassignedInnerQty = pendingInners,
            MaxReworkCycles = config.MaxReworkCycles,
            CurrentBatch = day.CurrentBatch,
            CompletedInners = day.CompletedInners,
            CompletedOuters = day.CompletedOuters,
            NgToday = day.NgItems,
            ReworkToday = day.ReworkItems,
            History = history,
            Kpis = new KpiSummary
            {
                Ok = completedUnits + day.CurrentBatch.Count,
                Ng = day.NgItems.Count,
                Rework = day.ReworkItems.Count,
            },
            Labels = new ScanLabels
            {
                Inner = BuildLabel(
                    store,
                    new LabelSource
                    {
                        Code = innerId,
                        Sku = innerSku,
                        WorkOrder = innerWo,
                        MaterialLot = innerLot,
                        Destination = innerDest,
                        QtyText = innerQty,
                        PrintedAt = innerTime,
                    },
                    context.Line,
                    context.Operator),
                Outer = BuildLabel(
                    store,
                    new LabelSource
                    {
                        Code = outerId,
                        Sku = outerSku,
                        WorkOrder = outerWo,
                        MaterialLot = outerLot,
                        Destination = outerDest,
                        QtyText = outerQty,
                        PrintedAt = outerTime,
                    },
                    context.Line,
                    context.Operator),
            },
            NgStations = config.NgStations,
            Reworks = config.Reworks,
            Ui = new ScanUiTexts
            {
                ScanOkTitle = config.UiScanOkTitle,
                ScanNgTitle = config.UiScanNgTitle,
                ScanRwTitle = config.UiScanRwTitle,
                DbHeaderTitle = config.UiDbHeaderTitle,
                DbSearchTitle = config.UiDbSearchTitle,
                DbTableTitle = config.UiDbTableTitle,
                ReportTitle = config.UiReportTitle,
                ScanStatusBadge = config.UiScanStatusBadge,
            },
            Brand = new BrandInfo
            {
                CompanyName = config.CompanyName,
                Subtitle = config.Subtitle,
                LogoDataUrl = config.LogoDataUrl,
                LabelInnerTitle = config.LabelInnerTitle,
                LabelOuterTitle = config.LabelOuterTitle,
                AccentColor = config.AccentColor,
            },
            Barcode = new BarcodeInfo
            {
                Type = config.Barcode2DType,
                Size = config.Barcode2DSize,
                LabelWidthMm = config.LabelWidthMm,
                LabelHeightMm = config.LabelHeightMm,
            },
            BridgeMode = config.ExternalPrinterMode,
            LastSyncAt = store.LastSyncAt,
        };
    }

    private static LabelData BuildLabel(
        TraceStoreState store,
        LabelSource source,
        string line,
        string operatorName)
    {
        return new LabelData
        {
            Code = source.Code,
            Sku = source.Sku,
            WorkOrder = source.WorkOrder,
            MaterialLot = CodeFormatter.NormalizeMaterialLotCode(source.MaterialLot),
            Destination = source.Destination,
            QtyText = source.QtyText,
            PrintedAt = source.PrintedAt,
            Payload = BarcodeRules.Build2DPayload(
                new BarcodeTarget
                {
                    BoxId = source.Code,
                    Sku = source.Sku,
                    WorkOrder = source.WorkOrder,
                    MaterialLot = source.MaterialLot,
                    QtyText = source.QtyText,
                    Destination = source.Destination,
                    PrintTime = source.PrintedAt,
                    Line = line,
                    Operator = operatorName,
                },
                store.Config),
        };
    }
}
