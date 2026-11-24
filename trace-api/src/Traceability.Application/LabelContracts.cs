namespace Traceability.Application;

// ---------------------------------------------------------------------------
// Fase 2 — stok label QR internal NG, batch, job cetak, dan sequence penerbitan.
// Kontrak ini dipakai UI "Generate ID", ekspor CSV NiceLabel, dan Fase 3
// (pengikatan label ke unit) tanpa melihat implementasi SQLite.
// ---------------------------------------------------------------------------

/// <summary>Halaman berisi data + metadata pagination (SSOT §8: pagination wajib).</summary>
public sealed class PagedResult<T>
{
    public List<T> Items { get; set; } = new();
    public int Total { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; }
    public int TotalPages { get; set; } = 1;
}

public sealed class NgLabelDto
{
    public string Id { get; set; } = string.Empty;
    public long Seq { get; set; }
    public string Namespace { get; set; } = string.Empty;
    /// <summary>Payload final yang dicetak; tidak pernah berubah saat cetak ulang.</summary>
    public string Payload { get; set; } = string.Empty;
    public string BatchId { get; set; } = string.Empty;
    /// <summary>AVAILABLE (stok) / BOUND (terikat unit) / VOID.</summary>
    public string Status { get; set; } = string.Empty;
    public string? BoundSerial { get; set; }
    public string? BoundAtUtc { get; set; }
    public int PrintedCount { get; set; }
    public string? LastPrintedAtUtc { get; set; }
    public string CreatedAtUtc { get; set; } = string.Empty;
}

public sealed class LabelBatchDto
{
    public string Id { get; set; } = string.Empty;
    public string Namespace { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public long FirstSeq { get; set; }
    public long LastSeq { get; set; }
    public string Note { get; set; } = string.Empty;
    public string CreatedByAccountId { get; set; } = string.Empty;
    public string CreatedByName { get; set; } = string.Empty;
    public string CreatedAtUtc { get; set; } = string.Empty;
    /// <summary>Ringkasan status label dalam batch ini.</summary>
    public int AvailableCount { get; set; }
    public int BoundCount { get; set; }
    public int PrintJobCount { get; set; }
    public int ConfirmedPrintCount { get; set; }
    public string? LastPrintedAtUtc { get; set; }
}

public sealed class PrintJobDto
{
    public string Id { get; set; } = string.Empty;
    public string BatchId { get; set; } = string.Empty;
    /// <summary>FIRST / REPRINT — cetak ulang memakai ID yang sama pada job baru.</summary>
    public string Kind { get; set; } = string.Empty;
    /// <summary>NICELABEL_CSV (Fase 2) atau DIRECT_PRINTER (Fase 5).</summary>
    public string Channel { get; set; } = string.Empty;
    /// <summary>EXPORTED / CONFIRMED / UNKNOWN / FAILED.</summary>
    public string Status { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public string RequestedByAccountId { get; set; } = string.Empty;
    public string RequestedByName { get; set; } = string.Empty;
    public string RequestedAtUtc { get; set; } = string.Empty;
    public string? ConfirmedAtUtc { get; set; }
    public int? ConfirmedCount { get; set; }
    public string? ExportFilename { get; set; }
    public string Note { get; set; } = string.Empty;
    public List<string> LabelIds { get; set; } = new();
}

public sealed class LabelBatchDetailDto
{
    public LabelBatchDto Batch { get; set; } = new();
    public List<NgLabelDto> Labels { get; set; } = new();
    public List<PrintJobDto> PrintJobs { get; set; } = new();
}

/// <summary>Kondisi sequence penerbitan — dipakai langkah verifikasi dan prosedur restore.</summary>
public sealed class LabelSequenceStateDto
{
    public string Name { get; set; } = string.Empty;
    /// <summary>Digit berikutnya yang akan dialokasikan DB.</summary>
    public long NextValue { get; set; }
    /// <summary>Digit tertinggi yang nyata-nyata tercatat di tabel label.</summary>
    public long MaxLabelSeq { get; set; }
    /// <summary>Digit tertinggi dari catatan penerbitan di luar file DB (ledger).</summary>
    public long LedgerMaxSeq { get; set; }
    /// <summary>Titik aman tertinggi yang pernah dikenal sistem.</summary>
    public long HighWaterSeq { get; set; }
    /// <summary>False saat restore tertinggal; generate ditolak sampai direkonsiliasi.</summary>
    public bool Consistent { get; set; }
    public string LedgerPath { get; set; } = string.Empty;
    public string? LedgerError { get; set; }
}

public sealed class GenerateBatchInput
{
    public int Quantity { get; set; }
    public string Note { get; set; } = string.Empty;
    /// <summary>Wajib; retry dengan key + isi yang sama mengembalikan batch lama.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;
}

public sealed class GenerateBatchResultDto
{
    public LabelBatchDto Batch { get; set; } = new();
    public List<NgLabelDto> Labels { get; set; } = new();
    /// <summary>True bila permintaan ulang mengembalikan batch yang sudah ada.</summary>
    public bool Reused { get; set; }
    public LabelSequenceStateDto Sequence { get; set; } = new();
    public int MaxPerRequest { get; set; }
}

public sealed class CreatePrintJobInput
{
    public string BatchId { get; set; } = string.Empty;
    /// <summary>Subset label; kosong = seluruh label pada batch.</summary>
    public List<string> LabelIds { get; set; } = new();
    /// <summary>NICELABEL_CSV (default) atau DIRECT_PRINTER.</summary>
    public string Channel { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public string? IdempotencyKey { get; set; }
}

public sealed class PrintJobResultDto
{
    public PrintJobDto Job { get; set; } = new();
    public bool Reused { get; set; }
    /// <summary>Berisi hanya ketika job dibuat/diubah pada request ini.</summary>
    public string? ExportCsv { get; set; }
    public string? ExportFilename { get; set; }
}

public sealed class PrintJobLabelResultInput
{
    public string LabelId { get; set; } = string.Empty;
    /// <summary>PRINTED atau MISSING menurut pemeriksaan fisik operator.</summary>
    public string Result { get; set; } = string.Empty;
}

public sealed class ConfirmPrintJobInput
{
    /// <summary>CONFIRMED (hasil fisik sah), UNKNOWN (tidak pasti → minta cetak ulang), FAILED.</summary>
    public string Status { get; set; } = string.Empty;
    public int? PrintedCount { get; set; }
    public string Note { get; set; } = string.Empty;
    public List<PrintJobLabelResultInput> Results { get; set; } = new();
}

public sealed class LabelBatchQuery
{
    public string? Search { get; set; }
    /// <summary>yyyy-MM-dd (WIB) — batas bawah inklusif pada waktu generate.</summary>
    public string? From { get; set; }
    /// <summary>yyyy-MM-dd (WIB) — batas atas eksklusif pada waktu generate.</summary>
    public string? To { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;
}

public sealed class PrintJobQuery
{
    public string? BatchId { get; set; }
    public string? Status { get; set; }
    public string? From { get; set; }
    public string? To { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;
}

public sealed class LabelStockSummaryDto
{
    public bool UsbPrintEnabled { get; set; }
    public int TotalLabels { get; set; }
    public int AvailableStock { get; set; }
    public int BoundStock { get; set; }
    public int TotalBatches { get; set; }
    public int TotalPrintJobs { get; set; }
    public int PendingConfirmationJobs { get; set; }
    public LabelSequenceStateDto Sequence { get; set; } = new();
}

public sealed class LabelCsvExport
{
    public string Filename { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public int RowCount { get; set; }
}

public interface ILabelStockService
{
    LabelStockSummaryDto GetSummary();

    LabelSequenceStateDto GetSequenceState();

    /// <summary>
    /// Alokasi atomik: batch + label tersimpan dalam satu transaksi, ID dari
    /// sequence DB, dan idempoten terhadap kunci idempotensi.
    /// </summary>
    GenerateBatchResultDto Generate(CallerPrincipal caller, GenerateBatchInput input);

    PagedResult<LabelBatchDto> ListBatches(LabelBatchQuery query);

    LabelBatchDetailDto GetBatch(string batchId, int labelLimit);

    PagedResult<NgLabelDto> ListLabels(string? batchId, string? status, int page, int pageSize);

    /// <summary>
    /// Bukti cetak balik: petakan payload hasil scan ("NG:000123") atau ID ("NG-000123")
    /// ke record label. Payload dengan namespace lain ditolak sebagai bukan label internal.
    /// </summary>
    NgLabelDto ResolveScan(string payloadOrId);

    /// <summary>Buat job cetak (FIRST/REPRINT) untuk seluruh batch atau subset label.</summary>
    PrintJobResultDto CreatePrintJob(CallerPrincipal caller, CreatePrintJobInput input);

    PagedResult<PrintJobDto> ListPrintJobs(PrintJobQuery query);

    PrintJobDto GetPrintJob(string jobId);

    /// <summary>Operator mencatat hasil cetak fisik; ekspor CSV saja tidak menandai tercetak.</summary>
    PrintJobDto ConfirmPrintJob(CallerPrincipal caller, string jobId, ConfirmPrintJobInput input);

    /// <summary>Berkas CSV untuk satu job (ID + payload) — isi QR tidak berubah.</summary>
    LabelCsvExport BuildJobExport(string jobId);

    /// <summary>
    /// Naikkan batas sequence dari catatan penerbitan (prosedur restore).
    /// observedMaxSeq dipakai bila operator melaporkan ID tertinggi yang masih beredar
    /// sementara catatan penerbitan ikut hilang saat pemulihan.
    /// </summary>
    LabelSequenceStateDto ReconcileSequence(CallerPrincipal caller, long? observedMaxSeq, string note);
}
