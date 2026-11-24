using Microsoft.AspNetCore.Mvc;
using Traceability.Application;
using Traceability.Domain;

namespace Traceability.Api.Controllers;

/// <summary>
/// Daftar master warisan (layar Database lama). Fase 1 memindahkan skus/lines/shifts
/// ke Master Produksi berbasis DB — perubahan lewat endpoint ini ditolak saat
/// enforcement aktif; kategori sisanya tetap dilindungi hak admin.master.
/// </summary>
[Route("api/v1/master/lists")]
public sealed class MasterDataController : BaseApiController
{
    private static readonly string[] DbManagedKeys = ["skus", "lines", "shifts"];

    public MasterDataController(IConfigService config, IAccountService accounts,
        Infrastructure.Foundation.TraceDbOptions options) : base(config, accounts, options)
    {
    }

    [HttpGet]
    public ActionResult<MasterListsResponse> Get()
    {
        if (Enforced) RequireLoggedIn();
        return Ok(Config.GetMasterLists());
    }

    [HttpPost]
    public async Task<IActionResult> Add()
    {
        var guard = GuardWrite();
        if (guard is not null) return guard;

        var body = await ReadJsonObjectAsync();
        var key = StringValue(body, "key") ?? string.Empty;
        var value = (StringValue(body, "value") ?? string.Empty).Trim();
        if (!IsKnownKey(key)) return BadRequest(new { error = "Kategori master tidak dikenal." });
        if (value.Length == 0) return BadRequest(new { error = "Nilai master data tidak boleh kosong." });

        var dbGuard = GuardDbManaged(key);
        if (dbGuard is not null) return dbGuard;

        return Ok(new { lists = Config.AddMasterValue(key, value).Lists });
    }

    [HttpDelete]
    public async Task<IActionResult> Remove()
    {
        var guard = GuardWrite();
        if (guard is not null) return guard;

        var body = await ReadJsonObjectAsync();
        var key = StringValue(body, "key") ?? string.Empty;
        if (!IsKnownKey(key)) return BadRequest(new { error = "Kategori master tidak dikenal." });

        var dbGuard = GuardDbManaged(key);
        if (dbGuard is not null) return dbGuard;

        var value = StringValue(body, "value");
        var index = string.IsNullOrEmpty(value) ? IntValue(body, "index") : null;
        return Ok(new { lists = Config.RemoveMasterValue(key, index, value).Lists });
    }

    private IActionResult? GuardWrite()
    {
        if (Enforced)
        {
            RequireCaller(PermissionCatalog.AdminMaster);
            return null;
        }

        return IsAdminSession
            ? null
            : StatusCode(StatusCodes.Status403Forbidden, new { error = "Akses Super Admin diperlukan." });
    }

    private IActionResult? GuardDbManaged(string key)
    {
        if (!Enforced || !DbManagedKeys.Contains(key, StringComparer.OrdinalIgnoreCase)) return null;
        return BadRequest(new
        {
            error = $"Master \"{key}\" dikelola lewat Master Produksi (basis data) — gunakan halaman Master Produksi.",
            code = "MASTER_DB_MANAGED",
        });
    }

    private bool IsKnownKey(string key)
    {
        return Config.GetMasterLists().Keys.Contains(key, StringComparer.Ordinal);
    }
}
