using Microsoft.AspNetCore.Mvc;
using Traceability.Application;
using Traceability.Domain;

namespace Traceability.Api.Controllers;

[Route("api/v1/auth")]
public sealed class AuthController : BaseApiController
{
    public AuthController(IConfigService config, IAccountService accounts,
        Infrastructure.Foundation.TraceDbOptions options) : base(config, accounts, options)
    {
    }

    public sealed record LoginRequest(string? Email, string? Password);

    public sealed record ChangePasswordRequest(string? CurrentPassword, string? NewPassword);

    /// <summary>Login email/password; hash diverifikasi dengan fasilitas framework, sesi server-side dibuat.</summary>
    [HttpPost("login")]
    public IActionResult Login([FromBody] LoginRequest request)
    {
        var result = Accounts.Login(request.Email ?? string.Empty, request.Password ?? string.Empty);
        return Ok(result);
    }

    /// <summary>Akhiri sesi login saat ini (token direvoke di backend).</summary>
    [HttpPost("logout")]
    public IActionResult Logout()
    {
        Accounts.Logout(ReadHeader("X-Trace-Session"));
        return Ok(new { ok = true });
    }

    /// <summary>Identitas pelaku + izin + konteks produksi aktif dari sesi.</summary>
    [HttpGet("me")]
    public IActionResult Me()
    {
        var caller = RequireLoggedIn();
        return Ok(new AuthProfileDto
        {
            AccountId = caller.AccountId,
            Email = caller.Email,
            FullName = caller.FullName,
            Status = EntityStatus.Active,
            MustChangePassword = caller.MustChangePassword,
            Permissions = caller.Permissions,
            Production = caller.Production,
        });
    }

    [HttpPut("change-password")]
    public IActionResult ChangePassword([FromBody] ChangePasswordRequest request)
    {
        var caller = RequireLoggedIn();
        Accounts.ChangeOwnPassword(caller.AccountId, request.CurrentPassword ?? string.Empty,
            request.NewPassword ?? string.Empty);
        return Ok(new { ok = true });
    }

    /// <summary>Daftar sesi login; admin melihat semua, pengguna biasa hanya miliknya.</summary>
    [HttpGet("sessions")]
    public IActionResult Sessions()
    {
        var caller = RequireLoggedIn();
        var filter = caller.Has(PermissionCatalog.AdminAccounts) ? null : caller.AccountId;
        return Ok(Accounts.ListSessions(filter));
    }

    [HttpPost("sessions/{id}/revoke")]
    public IActionResult RevokeSession(string id)
    {
        var caller = RequireLoggedIn();
        Accounts.RevokeSession(id, caller);
        return Ok(new { ok = true });
    }

    /// <summary>
    /// PIN studio konfigurasi (warisan). Saat enforcement aktif, tetap butuh
    /// sesi login dengan hak master agar tidak menjadi jalur pintas tanpa akun.
    /// </summary>
    [HttpPost("verify-pin")]
    public async Task<IActionResult> VerifyPin()
    {
        if (Enforced) return NotFound();

        var body = await TryReadJsonObjectAsync();
        if (body is null) return BadRequest(new { error = "Payload PIN tidak valid." });

        var pin = StringValue(body, "pin") ?? string.Empty;
        return Ok(new PinResult { Valid = Config.VerifyPin(pin) });
    }
}
