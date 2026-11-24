using Microsoft.AspNetCore.Mvc;
using Traceability.Application;
using Traceability.Domain;

namespace Traceability.Api.Controllers;

[Route("api/v1/config")]
public sealed class SettingsController : BaseApiController
{
    private readonly IMasterProjection _projection;
    private readonly ITraceStore _store;

    public SettingsController(IConfigService config, IAccountService accounts,
        Infrastructure.Foundation.TraceDbOptions options, IMasterProjection projection, ITraceStore store)
        : base(config, accounts, options)
    {
        _projection = projection;
        _store = store;
    }

    [HttpGet]
    [HttpGet("public")]
    public ActionResult<PublicConfig> Get()
    {
        return Ok(Config.GetPublicConfig());
    }

    [HttpPut]
    public async Task<IActionResult> Update()
    {
        var guard = GuardAdmin();
        if (guard is not null) return guard;

        var patch = await TryReadJsonObjectAsync();
        if (patch is null) return BadRequest(new { error = "Payload konfigurasi tidak valid." });

        if (Enforced)
        {
            var managed = new[] { "skus", "lines", "shifts" }
                .Where(key => patch[key] is not null)
                .ToList();
            if (managed.Count > 0)
            {
                return BadRequest(new
                {
                    error = "Master " + string.Join(", ", managed) + " dikelola lewat Master Produksi (basis data).",
                    code = "MASTER_DB_MANAGED",
                });
            }
        }

        return Ok(ReapplyMasters(Config.UpdateConfig(patch)));
    }

    [HttpPost("reset")]
    public IActionResult Reset()
    {
        var guard = GuardAdmin();
        if (guard is not null) return guard;

        return Ok(ReapplyMasters(Config.ResetConfig()));
    }

    /// <summary>
    /// Reset/patch konfigurasi dapat menimpa daftar master hasil proyeksi basis data;
    /// saat enforcement aktif proyeksi dipasang kembali agar layar produksi tetap memakai SSOT.
    /// </summary>
    private PublicConfig ReapplyMasters(PublicConfig result)
    {
        if (Enforced)
        {
            lock (_store.Gate)
            {
                _projection.Apply(_store.State.Config);
            }
        }

        return result;
    }

    private IActionResult? GuardAdmin()
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
}
