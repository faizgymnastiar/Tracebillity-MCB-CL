using Microsoft.AspNetCore.Mvc;
using Traceability.Application;
using Traceability.Domain;

namespace Traceability.Api.Controllers;

/// <summary>
/// Sesi produksi Fase 1: mulai produksi (shift/line/SKU + WO), ganti WO aktif,
/// tambah WO, dan akhiri sesi. Semua operasi terikat akun login — bukan header.
/// </summary>
[Route("api/v1/production")]
public sealed class ProductionController : BaseApiController
{
    /// <summary>Hak yang memperbolehkan menjalankan operasi lantai produksi.</summary>
    private static readonly string[] FloorPermissions =
    [
        PermissionCatalog.ScanUnit,
        PermissionCatalog.ScanInner,
        PermissionCatalog.ScanOuter,
        PermissionCatalog.NgRecord,
    ];

    private readonly IProductionSessionService _production;

    public ProductionController(IConfigService config, IAccountService accounts,
        Infrastructure.Foundation.TraceDbOptions options, IProductionSessionService production)
        : base(config, accounts, options)
    {
        _production = production;
    }

    public sealed record ActiveWorkOrderRequest(string? Number);

    /// <summary>Konteks produksi aktif milik akun login (atau penanda belum mulai).</summary>
    [HttpGet("current")]
    public IActionResult Current()
    {
        var caller = RequireLoggedIn();
        return Ok(new { active = caller.Production is not null, production = caller.Production });
    }

    /// <summary>Mulai sesi produksi dengan snapshot master + daftar WO.</summary>
    [HttpPost("start")]
    public IActionResult Start([FromBody] StartProductionInput input)
    {
        var caller = RequireCaller(FloorPermissions);
        return Ok(_production.Start(caller.AccountId, caller.FullName, input));
    }

    /// <summary>Ganti WO aktif; versi konteks naik sehingga layar berkonteks lama ditolak.</summary>
    [HttpPut("active-work-order")]
    public IActionResult SetActiveWorkOrder([FromBody] ActiveWorkOrderRequest request)
    {
        var caller = RequireCaller(FloorPermissions);
        return Ok(_production.SetActiveWorkOrder(caller.AccountId, request.Number ?? string.Empty));
    }

    /// <summary>Tambah WO baru ke sesi produksi yang sedang berjalan.</summary>
    [HttpPost("work-orders")]
    public IActionResult AddWorkOrder([FromBody] WorkOrderInput input)
    {
        var caller = RequireCaller(FloorPermissions);
        return Ok(_production.AddWorkOrder(caller.AccountId, input));
    }

    /// <summary>Akhiri sesi produksi aktif.</summary>
    [HttpDelete("end")]
    public IActionResult End()
    {
        var caller = RequireCaller(FloorPermissions);
        _production.End(caller.AccountId);
        return Ok(new { ok = true });
    }
}
