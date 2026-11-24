using Microsoft.AspNetCore.Mvc;
using QRCoder;
using Traceability.Application;
using Traceability.Domain;
using Traceability.Infrastructure.Foundation;

namespace Traceability.Api.Controllers;

/// <summary>
/// Karantina NG → rework → OK akhir (Fase 3). Pencatatan NG/rework memerlukan hak
/// ng.record, scan OK akhir memerlukan scan.unit; pembacaan riwayat menerima salah
/// satu hak tersebut. Tiga operasi tulis mewajibkan sesi produksi aktif dan menolak
/// konteks usang (CONTEXT_STALE) sebelum menyentuh transaksi.
/// </summary>
[Route("api/v1/ng")]
public sealed class NgController : BaseApiController
{
    private readonly INgWorkflowService _service;

    public NgController(IConfigService config, IAccountService accounts,
        TraceDbOptions options, INgWorkflowService service)
        : base(config, accounts, options)
    {
        _service = service;
    }

    // -----------------------------------------------------------------------
    // Baca
    // -----------------------------------------------------------------------

    [HttpGet("summary")]
    public IActionResult Summary()
    {
        var caller = RequireCaller(PermissionCatalog.NgRecord, PermissionCatalog.ScanUnit,
            PermissionCatalog.HistoryRead);
        return Ok(_service.GetSummary(caller));
    }

    public sealed record ScanPreviewInput(string? Value);

    [HttpPost("scan-preview")]
    public IActionResult ScanPreview(ScanPreviewInput input)
    {
        RequireCaller(PermissionCatalog.ScanUnit, PermissionCatalog.ScanInner, PermissionCatalog.ScanOuter,
            PermissionCatalog.NgRecord, PermissionCatalog.PrintManage, PermissionCatalog.HistoryRead);
        var value = input.Value?.Trim();
        if (string.IsNullOrEmpty(value) || value.Length > 500)
            return BadRequest(new { error = "Kode pratinjau harus berisi 1–500 karakter." });

        using var data = QRCodeGenerator.GenerateQrCode(value, QRCodeGenerator.ECCLevel.M);
        using var qr = new PngByteQRCode(data);
        return File(qr.GetGraphic(6), "image/png");
    }

    /// <summary>Pos QC + defect aktif untuk keluarga SKU sesi pelaku (dropdown server-side).</summary>
    [HttpGet("stations")]
    public IActionResult Stations()
    {
        var caller = RequireCaller(PermissionCatalog.NgRecord, PermissionCatalog.ScanUnit,
            PermissionCatalog.HistoryRead);
        return Ok(_service.ListStations(caller));
    }

    [HttpGet("units")]
    public IActionResult Units(
        [FromQuery] string? search,
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25)
    {
        RequireCaller(PermissionCatalog.ScanUnit, PermissionCatalog.NgRecord, PermissionCatalog.HistoryRead);
        return Ok(_service.ListUnits(new NgUnitQuery
        {
            Search = search,
            Status = status,
            Page = page,
            PageSize = pageSize,
        }));
    }

    /// <summary>Riwayat lengkap unit: kejadian NG berurutan, rework per percobaan, hasil OK.</summary>
    [HttpGet("units/{unitRef}")]
    public IActionResult Unit(string unitRef)
    {
        RequireCaller(PermissionCatalog.ScanUnit, PermissionCatalog.NgRecord, PermissionCatalog.HistoryRead);
        return Ok(_service.GetUnit(unitRef.Trim()));
    }

    // -----------------------------------------------------------------------
    // Tindakan
    // -----------------------------------------------------------------------

    /// <summary>Catat NG pertama (ikat label) atau NG ulang setelah rework selesai.</summary>
    [HttpPost("records/ng")]
    public IActionResult RecordNg([FromBody] RecordNgInput input)
    {
        var caller = RequireCaller(PermissionCatalog.NgRecord);
        AssertContextVersionCurrent(caller);
        return Ok(_service.RecordNg(caller, input));
    }

    /// <summary>Catat rework selesai: tambah satu percobaan, unit kembali ke Uji ulang.</summary>
    [HttpPost("records/rework")]
    public IActionResult RecordRework([FromBody] RecordReworkInput input)
    {
        var caller = RequireCaller(PermissionCatalog.NgRecord);
        AssertContextVersionCurrent(caller);
        return Ok(_service.RecordRework(caller, input));
    }

    /// <summary>OK akhir mode serial saja atau pasangan label + serial hasil rework.</summary>
    [HttpPost("records/ok")]
    public IActionResult RecordOk([FromBody] OkScanInput input)
    {
        var caller = RequireCaller(PermissionCatalog.ScanUnit);
        AssertContextVersionCurrent(caller);
        return Ok(_service.RecordOk(caller, input));
    }
}
