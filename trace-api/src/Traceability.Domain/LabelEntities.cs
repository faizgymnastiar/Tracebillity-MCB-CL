namespace Traceability.Domain;

/// <summary>Status satu label internal (Fase 2 → dipakai Fase 3 saat pengikatan unit).</summary>
public static class LabelStatus
{
    /// <summary>Ada di stok, belum terikat unit mana pun.</summary>
    public const string Available = "AVAILABLE";

    /// <summary>Sudah terikat satu unit seumur hidup; tidak dapat dipakai untuk unit lain.</summary>
    public const string Bound = "BOUND";

    /// <summary>Ditarik dari peredaran (koreksi admin).</summary>
    public const string Void = "VOID";

    public static readonly string[] All = { Available, Bound, Void };

    public static bool IsKnown(string? value) => value is not null && All.Contains(value, StringComparer.Ordinal);
}

/// <summary>Jenis percobaan cetak; cetak ulang selalu membuat job baru pada ID yang sama.</summary>
public static class PrintJobKind
{
    public const string First = "FIRST";
    public const string Reprint = "REPRINT";
}

/// <summary>Saluran cetak. Ekspor CSV NiceLabel dipakai sampai cetak langsung lolos uji fisik (SSOT §4).</summary>
public static class PrintChannel
{
    public const string NicelabelCsv = "NICELABEL_CSV";
    public const string DirectPrinter = "DIRECT_PRINTER";

    public static readonly string[] All = { NicelabelCsv, DirectPrinter };

    public static string Normalize(string? value)
    {
        if (string.Equals(value, DirectPrinter, StringComparison.OrdinalIgnoreCase)) return DirectPrinter;
        return NicelabelCsv;
    }
}

/// <summary>
/// Status job. EXPORTED berarti berkas tercetak telah dibuat tetapi hasil fisik
/// BELUM dikonfirmasi operator — label belum dianggap tercetak (Fase 2 langkah 5).
/// UNKNOWN dipakai saat hasil tidak pasti sehingga operator diminta memilih cetak ulang.
/// </summary>
public static class PrintJobStatus
{
    public const string Exported = "EXPORTED";
    public const string Confirmed = "CONFIRMED";
    public const string Unknown = "UNKNOWN";
    public const string Failed = "FAILED";

    public static readonly string[] All = { Exported, Confirmed, Unknown, Failed };

    public static bool IsKnown(string? value) => value is not null && All.Contains(value, StringComparer.Ordinal);
}

/// <summary>Hasil pemeriksaan fisik satu label di dalam job.</summary>
public static class PrintLabelResult
{
    public const string Pending = "PENDING";
    public const string Printed = "PRINTED";
    public const string Missing = "MISSING";

    public static string Normalize(string? value)
    {
        if (string.Equals(value, Missing, StringComparison.OrdinalIgnoreCase)) return Missing;
        if (string.Equals(value, Printed, StringComparison.OrdinalIgnoreCase)) return Printed;
        return Pending;
    }
}

/// <summary>Batch label stok — tanpa kaitan WO (SSOT §4), lintas WO untuk Fase 3.</summary>
public sealed class LabelBatchRecord
{
    public string Id { get; set; } = string.Empty;
    public string Namespace { get; set; } = NgLabelRules.Namespace;
    public int Quantity { get; set; }
    public long FirstSeq { get; set; }
    public long LastSeq { get; set; }
    public string Note { get; set; } = string.Empty;
    public string CreatedByAccountId { get; set; } = string.Empty;
    public string CreatedByName { get; set; } = string.Empty;
    public string CreatedAtUtc { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public string RequestHash { get; set; } = string.Empty;
    public int AvailableCount { get; set; }
    public int BoundCount { get; set; }
    public int PrintJobCount { get; set; }
    public int ConfirmedPrintCount { get; set; }
    public string? LastPrintedAtUtc { get; set; }
}

/// <summary>Label internal: ID global, payload unik, dan relasi ke batch.</summary>
public sealed class NgLabelRecord
{
    public string Id { get; set; } = string.Empty;
    public long Seq { get; set; }
    public string Namespace { get; set; } = NgLabelRules.Namespace;
    public string Payload { get; set; } = string.Empty;
    public string BatchId { get; set; } = string.Empty;
    public string Status { get; set; } = LabelStatus.Available;
    public string? BoundSerial { get; set; }
    public string? BoundAtUtc { get; set; }
    public int PrintedCount { get; set; }
    public string? LastPrintedAtUtc { get; set; }
    public string CreatedAtUtc { get; set; } = string.Empty;
}

/// <summary>Job cetak: operator, jenis cetak, waktu, status, dan subset label.</summary>
public sealed class PrintJobRecord
{
    public string Id { get; set; } = string.Empty;
    public string BatchId { get; set; } = string.Empty;
    public string Kind { get; set; } = PrintJobKind.First;
    public string Channel { get; set; } = PrintChannel.NicelabelCsv;
    public string Status { get; set; } = PrintJobStatus.Exported;
    public int Quantity { get; set; }
    public string RequestedByAccountId { get; set; } = string.Empty;
    public string RequestedByName { get; set; } = string.Empty;
    public string RequestedAtUtc { get; set; } = string.Empty;
    public string? ConfirmedAtUtc { get; set; }
    public int? ConfirmedCount { get; set; }
    public string? ExportFilename { get; set; }
    public string Note { get; set; } = string.Empty;
    public string? IdempotencyKey { get; set; }
    public List<string> LabelIds { get; set; } = new();
}

/// <summary>Baris catatan penerbitan di luar file DB (ledger pemulihan sequence).</summary>
public sealed class LabelIssuanceEntry
{
    public string AtUtc { get; set; } = string.Empty;
    public string BatchId { get; set; } = string.Empty;
    public long FirstSeq { get; set; }
    public long LastSeq { get; set; }
    public string Actor { get; set; } = string.Empty;
}
