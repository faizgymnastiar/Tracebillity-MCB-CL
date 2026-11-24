namespace Traceability.Domain;

public sealed class NgStation
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public List<string> Defects { get; set; } = new();
}

public sealed class ExportTemplate
{
    public string Name { get; set; } = string.Empty;
    public List<string> Columns { get; set; } = new();
    public string Orientation { get; set; } = string.Empty;
}

public sealed class ColorScheme
{
    public string Ok { get; set; } = "#059669";
    public string Ng { get; set; } = "#E11D48";
    public string Rework { get; set; } = "#FF7F00";
    public string Navy { get; set; } = "#161B41";
}

public sealed class AppConfig
{
    public string AdminPin { get; set; } = string.Empty;
    public string BrowserTitle { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;
    public string HeroTitle { get; set; } = string.Empty;
    public string BtnLoginText { get; set; } = string.Empty;
    public string BtnDbText { get; set; } = string.Empty;
    public string LoginTitle { get; set; } = string.Empty;
    public string LoginSubmitText { get; set; } = string.Empty;
    public string FooterTopText { get; set; } = string.Empty;
    public string FooterBottomText { get; set; } = string.Empty;
    public string LogoDataUrl { get; set; } = string.Empty;
    public string AccentColor { get; set; } = "#161b41";
    public string UiScanOkTitle { get; set; } = string.Empty;
    public string UiScanStatusBadge { get; set; } = string.Empty;
    public string UiScanNgTitle { get; set; } = string.Empty;
    public string UiScanRwTitle { get; set; } = string.Empty;
    public string UiDbHeaderTitle { get; set; } = string.Empty;
    public string UiDbSearchTitle { get; set; } = string.Empty;
    public string UiDbTableTitle { get; set; } = string.Empty;
    public string UiReportTitle { get; set; } = string.Empty;
    public string InnerCodeFormat { get; set; } = "INN-{YYYY}{MM}{DD}-{SEQ}";
    public string OuterCodeFormat { get; set; } = "OUT-{YYYY}{MM}{DD}-{SEQ}";
    public string ReworkCodeFormat { get; set; } = "RWK-{YYYY}{MM}-{SEQ}";
    public int SeqPadding { get; set; } = 3;
    public string SnPattern { get; set; } = string.Empty;
    public string Barcode2DType { get; set; } = "QR_MATRIX";
    public int Barcode2DSize { get; set; } = 112;
    public int LabelWidthMm { get; set; } = 100;
    public int LabelHeightMm { get; set; } = 60;
    public string BarcodeDelimiter { get; set; } = "|";
    public string BarcodePayloadFields { get; set; } = "{BOX_ID}|{SKU}|{WO}|{LOT}|{QTY}|{DEST}|{PRINT_TIME}";
    public string ExternalPrinterMode { get; set; } = "BARTENDER_CSV";
    public string WebhookUrl { get; set; } = string.Empty;
    public bool AutoBridgePrint { get; set; }
    public int InnerCap { get; set; } = 12;
    public int OuterCap { get; set; } = 10;
    public int MaxReworkCycles { get; set; } = 2;
    public List<string> Skus { get; set; } = new();
    public List<string> Materials { get; set; } = new();
    public List<string> Operators { get; set; } = new();
    public List<string> Lines { get; set; } = new();
    public List<string> Shifts { get; set; } = new();
    public List<string> Destinations { get; set; } = new();
    public List<string> Reworks { get; set; } = new();
    public string FaviconEmoji { get; set; } = string.Empty;
    public ColorScheme Colors { get; set; } = new();
    public string LabelInnerTitle { get; set; } = "INNER BOX LABEL";
    public string LabelOuterTitle { get; set; } = "OUTER BOX LABEL";
    public bool DailyReset { get; set; } = true;
    public List<NgStation> NgStations { get; set; } = new();
    public Dictionary<string, string> ColumnLabels { get; set; } = new();
    public List<ExportTemplate> ExportTemplates { get; set; } = new();
    public List<string> SelectedColumns { get; set; } = new();
}

public sealed class OperatorContext
{
    public string Operator { get; set; } = string.Empty;
    public string WorkOrder { get; set; } = string.Empty;
    public string MaterialLot { get; set; } = string.Empty;
    public string Line { get; set; } = string.Empty;
    public string Shift { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public string Destination { get; set; } = string.Empty;

    // Tambahan Fase 1 — identitas sesi produksi/WO aktif (kontrak fondasi).
    public string WorkOrderClient { get; set; } = string.Empty;
    public string ShippingReference { get; set; } = string.Empty;
    public int WorkOrderTarget { get; set; }
    public string ProductionSessionId { get; set; } = string.Empty;
    public int ContextVersion { get; set; }

    public OperatorContext Clone()
    {
        return new OperatorContext
        {
            Operator = Operator,
            WorkOrder = WorkOrder,
            MaterialLot = MaterialLot,
            Line = Line,
            Shift = Shift,
            Sku = Sku,
            Destination = Destination,
            WorkOrderClient = WorkOrderClient,
            ShippingReference = ShippingReference,
            WorkOrderTarget = WorkOrderTarget,
            ProductionSessionId = ProductionSessionId,
            ContextVersion = ContextVersion,
        };
    }
}

public sealed class BatchItem
{
    public string Sn { get; set; } = string.Empty;
    public string DayName { get; set; } = string.Empty;
    public string IsoDate { get; set; } = string.Empty;
    public string Time { get; set; } = string.Empty;
    public string Operator { get; set; } = string.Empty;
    public string WorkOrder { get; set; } = string.Empty;
    public string MaterialLot { get; set; } = string.Empty;
    public string Line { get; set; } = string.Empty;
    public string Shift { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public bool Reworked { get; set; }
    public int ReworkCycle { get; set; }
    public string ReworkTicket { get; set; } = "-";
}

public sealed class InnerBox
{
    public string Id { get; set; } = string.Empty;
    public string DayName { get; set; } = string.Empty;
    public string IsoDate { get; set; } = string.Empty;
    public string Time { get; set; } = string.Empty;
    public string Operator { get; set; } = string.Empty;
    public string WorkOrder { get; set; } = string.Empty;
    public string MaterialLot { get; set; } = string.Empty;
    public string Line { get; set; } = string.Empty;
    public string Shift { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public string Destination { get; set; } = string.Empty;
    public bool Partial { get; set; }
    public List<BatchItem> Items { get; set; } = new();
}

public sealed class OuterBox
{
    public string Id { get; set; } = string.Empty;
    public string DayName { get; set; } = string.Empty;
    public string IsoDate { get; set; } = string.Empty;
    public string Time { get; set; } = string.Empty;
    public string Operator { get; set; } = string.Empty;
    public string WorkOrder { get; set; } = string.Empty;
    public string MaterialLot { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public string Destination { get; set; } = string.Empty;
    public bool Partial { get; set; }
    public List<string> Inners { get; set; } = new();
    public int TotalPcs { get; set; }
}

public sealed class NgItem
{
    public string Sn { get; set; } = string.Empty;
    public string StationId { get; set; } = string.Empty;
    public string StationLabel { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string Disposition { get; set; } = string.Empty;
    public int ReworkCycle { get; set; }
    public int? ReworkFails { get; set; }
    public string DayName { get; set; } = string.Empty;
    public string IsoDate { get; set; } = string.Empty;
    public string Time { get; set; } = string.Empty;
    public string Operator { get; set; } = string.Empty;
    public string WorkOrder { get; set; } = string.Empty;
    public string MaterialLot { get; set; } = string.Empty;
    public string Line { get; set; } = string.Empty;
    public string Shift { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public string Destination { get; set; } = string.Empty;
}

public sealed class ReworkItem
{
    public string TicketId { get; set; } = string.Empty;
    public string Sn { get; set; } = string.Empty;
    public int Cycle { get; set; }
    public string StationLabel { get; set; } = string.Empty;
    public string PrevDefect { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string DayName { get; set; } = string.Empty;
    public string IsoDate { get; set; } = string.Empty;
    public string Time { get; set; } = string.Empty;
    public string Operator { get; set; } = string.Empty;
    public string WorkOrder { get; set; } = string.Empty;
    public string MaterialLot { get; set; } = string.Empty;
    public string Line { get; set; } = string.Empty;
    public string Shift { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public string Destination { get; set; } = string.Empty;
}

public sealed class BarcodeHistoryEntry
{
    public string Type { get; set; } = "INNER";
    public string Id { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public string Destination { get; set; } = string.Empty;
    public int Qty { get; set; }
    public string Event { get; set; } = "FULL";
    public string Time { get; set; } = string.Empty;
    public string IsoDate { get; set; } = string.Empty;
}

public sealed class DayState
{
    public string IsoDate { get; set; } = string.Empty;
    public string DayName { get; set; } = string.Empty;
    public int InnerSeq { get; set; } = 1;
    public int OuterSeq { get; set; } = 1;
    public int ReworkSeq { get; set; } = 1;
    public string? InnerPrintedTime { get; set; }
    public string? OuterPrintedTime { get; set; }
    public List<BatchItem> CurrentBatch { get; set; } = new();
    public List<InnerBox> CompletedInners { get; set; } = new();
    public List<OuterBox> CompletedOuters { get; set; } = new();
    public List<NgItem> NgItems { get; set; } = new();
    public List<ReworkItem> ReworkItems { get; set; } = new();
}

public sealed class TraceStoreState
{
    public AppConfig Config { get; set; } = new();
    public Dictionary<string, DayState> Days { get; set; } = new();
    public string ActiveDate { get; set; } = string.Empty;
    public OperatorContext DefaultContext { get; set; } = new();
    public List<BarcodeHistoryEntry> History { get; set; } = new();
    public Dictionary<string, int> ReworkCounts { get; set; } = new();
    public string LastSyncAt { get; set; } = string.Empty;
}

public sealed class BridgePayload
{
    public string Mode { get; set; } = "BARTENDER_CSV";
    public string Filename { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string Payload2d { get; set; } = string.Empty;
    public string BoxId { get; set; } = string.Empty;
}

public sealed class BarcodeTarget
{
    public string BoxId { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public string WorkOrder { get; set; } = string.Empty;
    public string MaterialLot { get; set; } = string.Empty;
    public string QtyText { get; set; } = string.Empty;
    public string Destination { get; set; } = string.Empty;
    public string PrintTime { get; set; } = string.Empty;
    public string Line { get; set; } = string.Empty;
    public string Operator { get; set; } = string.Empty;
}
