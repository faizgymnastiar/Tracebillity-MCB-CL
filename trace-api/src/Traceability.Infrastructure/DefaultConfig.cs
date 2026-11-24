using Traceability.Domain;

namespace Traceability.Infrastructure;

public static class DefaultConfig
{
    public static readonly string[] OfficialSkus =
    {
        "MCB CL P1 - 2 A",
        "MCB CL P1 - 4 A",
        "MCB CL P1 - 6 A",
        "MCB CL P1 - 10 A",
        "MCB CL P1 - 16 A",
        "MCB CL P1 - 20 A",
        "MCB CL P1 - 25 A",
        "MCB CL P1 - 35 A",
        "MCB CL P1 - 50 A",
        "MCB CL P3 - 10 A",
        "MCB CL P3 - 16 A",
        "MCB CL P3 - 20 A",
        "MCB CL P3 - 25 A",
        "MCB CL P3 - 35 A",
    };

    public static readonly string[] DefaultSelectedColumns =
    {
        "no", "isoDate", "waktu", "sn", "status", "station", "sku", "wo", "lot",
        "innerBox", "outerBox", "destination", "operator", "keterangan",
    };

    public static AppConfig Create()
    {
        return new AppConfig
        {
            AdminPin = "admin123",
            BrowserTitle = "PT. LELCO Trindo Nusantara — Traceability System",
            CompanyName = "PT. LELCO TRINDO NUSANTARA",
            HeroTitle = "WELCOME - TO - TRACEABILITY SYSTEM",
            BtnLoginText = "LOGIN & SCAN",
            BtnDbText = "DATABASE TRACE",
            LoginTitle = "END-OF-LINE TRACEABILITY GATE",
            LoginSubmitText = "MASUK & SCAN",
            FooterTopText = "PT. LELCO TRINDO NUSANTARA © 2026 — END-OF-LINE QUALITY & TRACEABILITY SYSTEM",
            LogoDataUrl = string.Empty,
            AccentColor = "#161b41",
            UiScanOkTitle = "Page scan produk ok",
            UiScanStatusBadge = "SCAN READY",
            UiScanNgTitle = "Page Scan Produk NG (Karantina Reject)",
            UiScanRwTitle = "Page Scan After Rework",
            UiDbHeaderTitle = "Database Traceability & Daily Output",
            UiDbSearchTitle = "Pencarian Traceability & Audit Kemasan Box",
            UiDbTableTitle = "Log Rincian Traceability Serial Number & Kemasan",
            UiReportTitle = "LAPORAN TRACEABILITY PRODUKSI",
            InnerCodeFormat = "INN-{YYYY}{MM}{DD}-{SEQ}",
            OuterCodeFormat = "OUT-{YYYY}{MM}{DD}-{SEQ}",
            ReworkCodeFormat = "RWK-{YYYY}{MM}-{SEQ}",
            SeqPadding = 3,
            SnPattern = string.Empty,
            Barcode2DType = "QR_MATRIX",
            Barcode2DSize = 112,
            LabelWidthMm = 100,
            LabelHeightMm = 60,
            BarcodeDelimiter = "|",
            BarcodePayloadFields = BarcodeRules.DefaultPayloadFields,
            ExternalPrinterMode = "BARTENDER_CSV",
            WebhookUrl = "http://localhost:9100/api/print-label",
            AutoBridgePrint = false,
            InnerCap = 12,
            OuterCap = 10,
            MaxReworkCycles = 2,
            Skus = new List<string>(OfficialSkus),
            Materials = new List<string>
            {
                "LOT-BM-2611A (Bimetal Strip)",
                "LOT-COIL-2611B (Magnetic Trip Coil)",
                "LOT-CASE-2611C (Housing Flame Retardant)",
            },
            Operators = new List<string> { "OP-2026-REGA", "OP-2026-TAOFIQ", "OP-2026-OWEN", "OP-2026-IHSAN" },
            Lines = new List<string> { "LINE 01 - MCB CL", "LINE 02 - MCB CL" },
            Shifts = new List<string> { "Shift 1", "Shift 2", "Shift 3" },
            Destinations = new List<string>
            {
                "CUST-01 - PLN Jakarta",
                "CUST-02 - PLN Bandung",
                "CUST-03 - PLN Tangerang",
                "CUST-04 - PLN Surabaya",
            },
            Reworks = new List<string>
            {
                "RW-01: Kalibrasi Ulang & Lulus Uji Akhir Hi-Pot",
                "RW-02: Penyetelan Ulang Kalibrasi Trip MCB",
                "RW-03: Penggantian Mekanisme Toggle & Uji Ulang",
                "RW-04: Penggantian Baut Terminal & Torsi Ulang",
                "RW-05: Laser / Tampo Marking Ulang & Inspeksi Visual OK",
            },
            FaviconEmoji = string.Empty,
            Colors = new ColorScheme(),
            LabelInnerTitle = "INNER BOX LABEL",
            LabelOuterTitle = "OUTER BOX LABEL",
            DailyReset = true,
            NgStations = BuildStations(),
            ColumnLabels = new Dictionary<string, string>(),
            ExportTemplates = new List<ExportTemplate>(),
            SelectedColumns = new List<string>(DefaultSelectedColumns),
        };
    }

    private static List<NgStation> BuildStations()
    {
        return new List<NgStation>
        {
            Station("ST-01", "ST-01: Rivet", "DF-01: Rivet Crack", "DF-01: Missing Rivet", "DF-01: Other"),
            Station("ST-02", "ST-02: Thermal Adjuster", "DF-02: Early Trip", "DF-02: Late Trip", "DF-02: Handle Loss", "DF-02: Other"),
            Station("ST-03", "ST-03: Magnetic Test 4 IN", "DF-03: Tidak Trip Diatas 02 Second", "DF-03: Other"),
            Station("ST-04", "ST-04: Magnetic Test 6 IN", "DF-04: Tidak Trip Dibawah 02 Second", "DF-04: Other"),
            Station("ST-05", "ST-05: High Voltage", "DF-05: Arus Bocor Saat di Suplay 2 KV", "DF-05: Other"),
            Station("ST-06", "ST-06: Pad Printing", "DF-06: Hasil Printing Tipis", "DF-06: Printing Miring", "DF-06: Other"),
            Station("ST-07", "ST-07: Accessories", "DF-07: -", "DF-07: Other"),
            Station("ST-08", "ST-08: Laser QR Code", "DF-08: Marking Laser Tipis", "DF-08: Hasil Laser Miring", "DF-08: Other"),
            Station("ST-09", "ST-09: Visual & Packing", "DF-09: Visual Tidak Sesuai", "DF-09: Other"),
        };
    }

    private static NgStation Station(string id, string label, params string[] defects)
    {
        return new NgStation { Id = id, Label = label, Defects = new List<string>(defects) };
    }
}
