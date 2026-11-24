using Microsoft.AspNetCore.Mvc;
using Traceability.Application;
using Traceability.Domain;

namespace Traceability.Api.Controllers;

/// <summary>
/// Master produksi resmi Fase 1 (keluarga, SKU, shift, line, tahap, defect).
/// Pembacaan pilihan aktif boleh untuk semua akun login (pemilih "Mulai produksi");
/// perubahan dan penonaktifan memerlukan hak admin.master.
/// </summary>
[Route("api/v1/master/catalog")]
public sealed class MasterCatalogController : BaseApiController
{
    private readonly IMasterCatalogService _catalog;

    public MasterCatalogController(IConfigService config, IAccountService accounts,
        Infrastructure.Foundation.TraceDbOptions options, IMasterCatalogService catalog)
        : base(config, accounts, options)
    {
        _catalog = catalog;
    }

    public sealed record StatusRequest(string? Status);

    /// <summary>Pilihan aktif untuk pemilih Mulai produksi / layar NG (tanpa nonaktif).</summary>
    [HttpGet("active")]
    public IActionResult Active()
    {
        RequireLoggedIn();
        return Ok(_catalog.GetCatalog(true));
    }

    [HttpGet]
    public IActionResult All()
    {
        RequireCaller(PermissionCatalog.AdminMaster);
        return Ok(_catalog.GetCatalog(false));
    }

    [HttpPost("family")]
    public IActionResult Family([FromBody] MasterItemInput input)
    {
        RequireCaller(PermissionCatalog.AdminMaster);
        _catalog.UpsertFamily(input);
        return Ok(_catalog.GetCatalog(false));
    }

    [HttpPost("sku")]
    public IActionResult Sku([FromBody] MasterItemInput input)
    {
        RequireCaller(PermissionCatalog.AdminMaster);
        _catalog.UpsertSku(input);
        return Ok(_catalog.GetCatalog(false));
    }

    [HttpPost("shift")]
    public IActionResult Shift([FromBody] MasterItemInput input)
    {
        RequireCaller(PermissionCatalog.AdminMaster);
        _catalog.UpsertShift(input);
        return Ok(_catalog.GetCatalog(false));
    }

    [HttpPost("line")]
    public IActionResult Line([FromBody] MasterItemInput input)
    {
        RequireCaller(PermissionCatalog.AdminMaster);
        _catalog.UpsertLine(input);
        return Ok(_catalog.GetCatalog(false));
    }

    [HttpPost("stage")]
    public IActionResult Stage([FromBody] MasterItemInput input)
    {
        RequireCaller(PermissionCatalog.AdminMaster);
        _catalog.UpsertStage(input);
        return Ok(_catalog.GetCatalog(false));
    }

    [HttpPost("defect")]
    public IActionResult Defect([FromBody] MasterItemInput input)
    {
        RequireCaller(PermissionCatalog.AdminMaster);
        _catalog.UpsertDefect(input);
        return Ok(_catalog.GetCatalog(false));
    }

    /// <summary>Nonaktifkan/aktifkan master — hanya memengaruhi pilihan baru.</summary>
    [HttpPut("{entity}/{id}/status")]
    public IActionResult SetStatus(string entity, string id, [FromBody] StatusRequest request)
    {
        RequireCaller(PermissionCatalog.AdminMaster);
        _catalog.SetStatus(entity, id, request.Status ?? EntityStatus.Active);
        return Ok(_catalog.GetCatalog(false));
    }
}
