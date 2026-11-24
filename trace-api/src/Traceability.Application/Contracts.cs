using System.Text.Json.Nodes;
using Traceability.Domain;

namespace Traceability.Application;

public sealed class ScanInput
{
    public string Mode { get; set; } = "OK";
    public string Sn { get; set; } = string.Empty;
    public string? StationId { get; set; }
    public string? Reason { get; set; }
    public string? Disposition { get; set; }
    public string? Action { get; set; }
    public string? Verdict { get; set; }
}

public sealed class RecordQuery
{
    public string? Search { get; set; }
    public string? Date { get; set; }
    public string Status { get; set; } = "ALL";
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}

public sealed class KpiSummary
{
    public int Ok { get; set; }
    public int Ng { get; set; }
    public int Rework { get; set; }
}

public sealed class ScanUiTexts
{
    public string ScanOkTitle { get; set; } = string.Empty;
    public string ScanNgTitle { get; set; } = string.Empty;
    public string ScanRwTitle { get; set; } = string.Empty;
    public string DbHeaderTitle { get; set; } = string.Empty;
    public string DbSearchTitle { get; set; } = string.Empty;
    public string DbTableTitle { get; set; } = string.Empty;
    public string ReportTitle { get; set; } = string.Empty;
    public string ScanStatusBadge { get; set; } = string.Empty;
}

public sealed class BrandInfo
{
    public string CompanyName { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;
    public string LogoDataUrl { get; set; } = string.Empty;
    public string LabelInnerTitle { get; set; } = string.Empty;
    public string LabelOuterTitle { get; set; } = string.Empty;
    public string AccentColor { get; set; } = string.Empty;
}

public sealed class BarcodeInfo
{
    public string Type { get; set; } = "QR_MATRIX";
    public int Size { get; set; }
    public int LabelWidthMm { get; set; }
    public int LabelHeightMm { get; set; }
}

public sealed class LabelData
{
    public string Code { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public string WorkOrder { get; set; } = string.Empty;
    public string MaterialLot { get; set; } = string.Empty;
    public string Destination { get; set; } = string.Empty;
    public string QtyText { get; set; } = string.Empty;
    public string PrintedAt { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
}

public sealed class ScanSnapshot
{
    public string ActiveDate { get; set; } = string.Empty;
    public string ActiveDayName { get; set; } = string.Empty;
    public OperatorContext Context { get; set; } = new();
    public string ActiveInnerId { get; set; } = string.Empty;
    public string ActiveOuterId { get; set; } = string.Empty;
    public string NextReworkTicket { get; set; } = string.Empty;
    public int InnerCap { get; set; }
    public int OuterCap { get; set; }
    public int ActiveInnerQty { get; set; }
    public int UnassignedInnerQty { get; set; }
    public int MaxReworkCycles { get; set; }
    public List<BatchItem> CurrentBatch { get; set; } = new();
    public List<InnerBox> CompletedInners { get; set; } = new();
    public List<OuterBox> CompletedOuters { get; set; } = new();
    public List<NgItem> NgToday { get; set; } = new();
    public List<ReworkItem> ReworkToday { get; set; } = new();
    public List<BarcodeHistoryEntry> History { get; set; } = new();
    public KpiSummary Kpis { get; set; } = new();
    public ScanLabels Labels { get; set; } = new();
    public List<NgStation> NgStations { get; set; } = new();
    public List<string> Reworks { get; set; } = new();
    public ScanUiTexts Ui { get; set; } = new();
    public BrandInfo Brand { get; set; } = new();
    public BarcodeInfo Barcode { get; set; } = new();
    public string BridgeMode { get; set; } = string.Empty;
    public string LastSyncAt { get; set; } = string.Empty;

    // Fase 1 — kontrak fondasi: konteks sesi produksi aktif & hak pelaku.
    public ProductionContextDto? Production { get; set; }
    public bool AuthEnforced { get; set; }
    public List<string> Permissions { get; set; } = new();
}

public sealed class ScanLabels
{
    public LabelData Inner { get; set; } = new();
    public LabelData Outer { get; set; } = new();
}

public sealed class ScanResult
{
    public bool Ok { get; set; }
    public string Message { get; set; } = string.Empty;
    public string Tone { get; set; } = "ok";
    public ScanSnapshot? Snapshot { get; set; }
    public BridgePayload? AutoBridge { get; set; }
}

public sealed class TraceabilityRecord
{
    public string Id { get; set; } = string.Empty;
    public string Category { get; set; } = "OK";
    public string DayName { get; set; } = string.Empty;
    public string IsoDate { get; set; } = string.Empty;
    public string Waktu { get; set; } = string.Empty;
    public string Sn { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Station { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public string Wo { get; set; } = string.Empty;
    public string Lot { get; set; } = string.Empty;
    public string InnerBox { get; set; } = string.Empty;
    public string OuterBox { get; set; } = string.Empty;
    public string ReworkTicket { get; set; } = string.Empty;
    public string Line { get; set; } = string.Empty;
    public string Shift { get; set; } = string.Empty;
    public string Destination { get; set; } = string.Empty;
    public string Operator { get; set; } = string.Empty;
    public string Keterangan { get; set; } = string.Empty;
}

public sealed class RecentBox
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = "INNER";
    public string Sku { get; set; } = string.Empty;
    public int Qty { get; set; }
}

public sealed class TraceFocus
{
    public string Kind { get; set; } = "unit";
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}

public sealed class RecordCounts
{
    public int Ok { get; set; }
    public int Rework { get; set; }
    public int Ng { get; set; }
}

public sealed class TraceRecordListResponse
{
    public List<TraceabilityRecord> Items { get; set; } = new();
    public int Total { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages { get; set; }
    public string LastSyncAt { get; set; } = string.Empty;
    public RecordCounts Counts { get; set; } = new();
    public List<RecentBox> RecentBoxes { get; set; } = new();
    public TraceFocus? Focus { get; set; }
}

public sealed class TimelineStep
{
    public string Label { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public string? Time { get; set; }
    public string Tone { get; set; } = "neutral";
}

public sealed class RelatedUnit
{
    public string Sn { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Href { get; set; } = string.Empty;
}

public sealed class RelatedBox
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = "INNER";
    public string Label { get; set; } = string.Empty;
    public string Href { get; set; } = string.Empty;
}

public sealed class TraceDetailResponse
{
    public string Query { get; set; } = string.Empty;
    public string Kind { get; set; } = "unit";
    public TraceabilityRecord? Unit { get; set; }
    public InnerBox? InnerBox { get; set; }
    public OuterBox? OuterBox { get; set; }
    public List<TimelineStep> Timeline { get; set; } = new();
    public List<RelatedUnit> RelatedUnits { get; set; } = new();
    public List<RelatedBox> RelatedBoxes { get; set; } = new();
}

public sealed class DailySummaryRow
{
    public string IsoDate { get; set; } = string.Empty;
    public string DayName { get; set; } = string.Empty;
    public int Total { get; set; }
    public int DirectOk { get; set; }
    public int AfterRework { get; set; }
    public int NgCount { get; set; }
    public int InnerBoxes { get; set; }
    public int OuterBoxes { get; set; }
}

public sealed class DailySummaryResponse
{
    public List<DailySummaryRow> Items { get; set; } = new();
    public int TotalDays { get; set; }
    public string LastSyncAt { get; set; } = string.Empty;
}

public sealed class HomeStats
{
    public string ActiveDate { get; set; } = string.Empty;
    public int TotalOutput { get; set; }
    public int DirectOk { get; set; }
    public int NgCount { get; set; }
    public int InnerBoxes { get; set; }
    public int OuterBoxes { get; set; }
}

public sealed class HealthStatus
{
    public string Status { get; set; } = "ok";
    public string DataSource { get; set; } = "trace-api-memory-store";
    public string? LatestSyncIso { get; set; }
    public int? RowsProcessed { get; set; }
}

public sealed class MasterListsResponse
{
    public List<string> Keys { get; set; } = new();
    public Dictionary<string, string> Labels { get; set; } = new();
    public Dictionary<string, List<string>> Lists { get; set; } = new();
}

public sealed class PublicConfig
{
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
    public string AccentColor { get; set; } = string.Empty;
    public string UiScanOkTitle { get; set; } = string.Empty;
    public string UiScanStatusBadge { get; set; } = string.Empty;
    public string UiScanNgTitle { get; set; } = string.Empty;
    public string UiScanRwTitle { get; set; } = string.Empty;
    public string UiDbHeaderTitle { get; set; } = string.Empty;
    public string UiDbSearchTitle { get; set; } = string.Empty;
    public string UiDbTableTitle { get; set; } = string.Empty;
    public string UiReportTitle { get; set; } = string.Empty;
    public string InnerCodeFormat { get; set; } = string.Empty;
    public string OuterCodeFormat { get; set; } = string.Empty;
    public string ReworkCodeFormat { get; set; } = string.Empty;
    public int SeqPadding { get; set; }
    public string SnPattern { get; set; } = string.Empty;
    public string Barcode2DType { get; set; } = string.Empty;
    public int Barcode2DSize { get; set; }
    public int LabelWidthMm { get; set; }
    public int LabelHeightMm { get; set; }
    public string BarcodeDelimiter { get; set; } = string.Empty;
    public string BarcodePayloadFields { get; set; } = string.Empty;
    public string ExternalPrinterMode { get; set; } = string.Empty;
    public string WebhookUrl { get; set; } = string.Empty;
    public bool AutoBridgePrint { get; set; }
    public int InnerCap { get; set; }
    public int OuterCap { get; set; }
    public int MaxReworkCycles { get; set; }
    public List<string> Skus { get; set; } = new();
    public List<string> Materials { get; set; } = new();
    public List<string> Operators { get; set; } = new();
    public List<string> Lines { get; set; } = new();
    public List<string> Shifts { get; set; } = new();
    public List<string> Destinations { get; set; } = new();
    public List<string> Reworks { get; set; } = new();
    public string FaviconEmoji { get; set; } = string.Empty;
    public ColorScheme Colors { get; set; } = new();
    public string LabelInnerTitle { get; set; } = string.Empty;
    public string LabelOuterTitle { get; set; } = string.Empty;
    public bool DailyReset { get; set; }
    public List<NgStation> NgStations { get; set; } = new();
    public Dictionary<string, string> ColumnLabels { get; set; } = new();
    public List<ExportTemplate> ExportTemplates { get; set; } = new();
    public List<string> SelectedColumns { get; set; } = new();
}

public sealed class MasterValueRequest
{
    public string? Key { get; set; }
    public string? Value { get; set; }
    public int? Index { get; set; }
}

public sealed class PinRequest
{
    public string? Pin { get; set; }
}

public sealed class PinResult
{
    public bool Valid { get; set; }
}

public sealed class CsvExport
{
    public string Filename { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
}

public interface ITraceService
{
    ScanSnapshot GetScanState(OperatorContext context);
    ScanResult PerformScan(ScanInput input, OperatorContext context);
    ScanSnapshot MarkLabelsPrinted(OperatorContext context);
    ScanSnapshot ClearActiveBatch(OperatorContext context);
    ScanSnapshot RemoveBatchItem(string sn, OperatorContext context);
    BridgePayload BuildLatestBridge(OperatorContext context);
}

public interface ITraceQueryService
{
    TraceRecordListResponse ListRecords(RecordQuery query, OperatorContext context);
    TraceDetailResponse? GetTraceDetail(string identifier, OperatorContext context);
    DailySummaryResponse GetDailySummary(OperatorContext context);
    HomeStats GetHomeStats(OperatorContext context);
    HealthStatus GetHealth();
    CsvExport BuildExport(RecordQuery query, IReadOnlyCollection<string> columns, OperatorContext context);
}

public interface IConfigService
{
    AppConfig GetConfig();
    PublicConfig GetPublicConfig();
    PublicConfig UpdateConfig(JsonObject patch);
    PublicConfig ResetConfig();
    MasterListsResponse GetMasterLists();
    MasterListsResponse AddMasterValue(string key, string value);
    MasterListsResponse RemoveMasterValue(string key, int? index, string? value);
    bool VerifyPin(string? pin);
    OperatorContext GetDefaultContext();
    OperatorContext SetDefaultContext(OperatorContext context);
}

public interface ITraceStore
{
    TraceStoreState State { get; }
    object Gate { get; }
}
