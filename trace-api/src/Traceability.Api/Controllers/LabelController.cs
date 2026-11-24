using System.Text;
using Microsoft.AspNetCore.Mvc;
using QRCoder;
using Traceability.Application;
using Traceability.Domain;
using Traceability.Infrastructure.Foundation;

namespace Traceability.Api.Controllers;

/// <summary>
/// Stok label QR internal NG (Fase 2). Generate batch dan tindakan cetak memerlukan
/// hak print.manage; pembacaan daftar/riwayat menerima print.manage atau history.read.
/// Semua penolakan terjadi di backend, bukan di layar.
/// </summary>
[Route("api/v1/labels")]
public sealed class LabelController : BaseApiController
{
    private readonly ILabelStockService _labels;
    private readonly IConfiguration _configuration;

    public LabelController(IConfigService config, IAccountService accounts,
        TraceDbOptions options, ILabelStockService labels, IConfiguration configuration)
        : base(config, accounts, options)
    {
        _labels = labels;
        _configuration = configuration;
    }

    public sealed record ReconcileRequest(long? ObservedMaxSeq, string? Note);

    // -----------------------------------------------------------------------
    // Baca
    // -----------------------------------------------------------------------

    [HttpGet]
    public IActionResult Summary()
    {
        RequireCaller(PermissionCatalog.PrintManage, PermissionCatalog.HistoryRead);
        var summary = _labels.GetSummary();
        summary.UsbPrintEnabled = _configuration.GetValue<bool>("Trace:UsbPrint:Enabled");
        return Ok(summary);
    }

    [HttpGet("sequence")]
    public IActionResult Sequence()
    {
        RequireCaller(PermissionCatalog.PrintManage, PermissionCatalog.HistoryRead);
        return Ok(_labels.GetSequenceState());
    }

    [HttpGet("batches")]
    public IActionResult Batches(
        [FromQuery] string? search,
        [FromQuery] string? from,
        [FromQuery] string? to,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25)
    {
        RequireCaller(PermissionCatalog.PrintManage, PermissionCatalog.HistoryRead);
        return Ok(_labels.ListBatches(new LabelBatchQuery
        {
            Search = search,
            From = from,
            To = to,
            Page = page,
            PageSize = pageSize,
        }));
    }

    [HttpGet("batches/{batchId}")]
    public IActionResult Batch(string batchId, [FromQuery] int labelLimit = 0)
    {
        RequireCaller(PermissionCatalog.PrintManage, PermissionCatalog.HistoryRead);
        // labelLimit <= 0 berarti seluruh isi batch (permintaan kecil, satu batch <= 500 label).
        return Ok(_labels.GetBatch(batchId.Trim(), labelLimit < 0 ? 0 : Math.Min(labelLimit, 2000)));
    }

    [HttpGet("labels")]
    public IActionResult Labels(
        [FromQuery] string? batchId,
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        RequireCaller(PermissionCatalog.PrintManage, PermissionCatalog.HistoryRead);
        return Ok(_labels.ListLabels(batchId, status, page, pageSize));
    }

    /// <summary>Verifikasi hasil scan: payload/ID internal → record label (bukti cetak balik).</summary>
    [HttpGet("qr.png")]
    public IActionResult PreviewQr(string value)
    {
        RequireCaller(PermissionCatalog.PrintManage, PermissionCatalog.HistoryRead);
        var label = _labels.ResolveScan(value);
        using var data = QRCodeGenerator.GenerateQrCode(label.Payload, QRCodeGenerator.ECCLevel.M);
        using var qr = new PngByteQRCode(data);
        return File(qr.GetGraphic(8), "image/png");
    }

    [HttpGet("resolve")]
    public IActionResult Resolve([FromQuery] string value)
    {
        RequireCaller(PermissionCatalog.PrintManage, PermissionCatalog.HistoryRead);
        return Ok(_labels.ResolveScan(value));
    }

    [HttpGet("print-jobs")]
    public IActionResult PrintJobs(
        [FromQuery] string? batchId,
        [FromQuery] string? status,
        [FromQuery] string? from,
        [FromQuery] string? to,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25)
    {
        RequireCaller(PermissionCatalog.PrintManage, PermissionCatalog.HistoryRead);
        return Ok(_labels.ListPrintJobs(new PrintJobQuery
        {
            BatchId = batchId,
            Status = status,
            From = from,
            To = to,
            Page = page,
            PageSize = pageSize,
        }));
    }

    [HttpGet("print-jobs/{jobId}")]
    public IActionResult PrintJob(string jobId)
    {
        RequireCaller(PermissionCatalog.PrintManage, PermissionCatalog.HistoryRead);
        return Ok(_labels.GetPrintJob(jobId.Trim()));
    }

    // -----------------------------------------------------------------------
    // Tindakan (hak generate/cetak)
    // -----------------------------------------------------------------------

    [HttpPost("batches")]
    public IActionResult Generate([FromBody] GenerateBatchInput input)
    {
        var caller = RequireCaller(PermissionCatalog.PrintManage);
        return Ok(_labels.Generate(caller, input));
    }

    [HttpPost("print-jobs")]
    public IActionResult CreatePrintJob([FromBody] CreatePrintJobInput input)
    {
        var caller = RequireCaller(PermissionCatalog.PrintManage);
        if (PrintChannel.Normalize(input.Channel) == PrintChannel.DirectPrinter &&
            !_configuration.GetValue<bool>("Trace:UsbPrint:Enabled"))
            throw FoundationErrors.Conflict("USB_DISABLED", "Cetak USB belum diaktifkan. Gunakan NiceLabel.");
        return Ok(_labels.CreatePrintJob(caller, input));
    }

    /// <summary>Ekspor CSV satu job (ID + payload) untuk template NiceLabel.</summary>
    [HttpGet("print-jobs/{jobId}/export.csv")]
    public IActionResult ExportJob(string jobId)
    {
        RequireCaller(PermissionCatalog.PrintManage);
        var csv = _labels.BuildJobExport(jobId.Trim());
        Response.Headers.ContentDisposition = $"attachment; filename=\"{csv.Filename}\"";
        Response.Headers.CacheControl = "no-store";
        return File(Encoding.UTF8.GetBytes(csv.Content), "text/csv; charset=utf-8");
    }

    /// <summary>Operator mencatat hasil cetak fisik; ekspor CSV saja tidak menandai tercetak.</summary>
    [HttpPost("print-jobs/{jobId}/confirm")]
    public IActionResult Confirm(string jobId, [FromBody] ConfirmPrintJobInput input)
    {
        var caller = RequireCaller(PermissionCatalog.PrintManage);
        return Ok(_labels.ConfirmPrintJob(caller, jobId.Trim(), input));
    }

    /// <summary>Prosedur restore: naikkan lantai sequence dari catatan penerbitan/laporan ID.</summary>
    [HttpPost("sequence/reconcile")]
    public IActionResult Reconcile([FromBody] ReconcileRequest request)
    {
        var caller = RequireCaller(PermissionCatalog.PrintManage);
        return Ok(_labels.ReconcileSequence(caller, request.ObservedMaxSeq, request.Note ?? string.Empty));
    }
}
