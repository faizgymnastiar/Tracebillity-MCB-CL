using Traceability.Domain;

namespace Traceability.Application;

/// <summary>Kelas status untuk memetakan kegagalan fondasi ke kode HTTP di layer Api.</summary>
public enum FoundationFailure
{
    Unauthorized,
    Forbidden,
    BadRequest,
    NotFound,
    Conflict,
}

/// <summary>Validasi/otorisasi fondasi gagal; dilempar service, dipetakan controller ke HTTP.</summary>
public sealed class FoundationException : Exception
{
    public FoundationException(FoundationFailure failure, string code, string message) : base(message)
    {
        Failure = failure;
        Code = code;
    }

    public FoundationFailure Failure { get; }
    public string Code { get; }
}

public static class FoundationErrors
{
    public static FoundationException Unauthorized(string message = "Sesi login tidak ditemukan atau sudah berakhir.")
        => new(FoundationFailure.Unauthorized, "UNAUTHENTICATED", message);

    public static FoundationException Forbidden(string permission)
        => new(FoundationFailure.Forbidden, "FORBIDDEN", $"Akun tidak memiliki izin \"{permission}\".");

    public static FoundationException BadRequest(string code, string message)
        => new(FoundationFailure.BadRequest, code, message);

    public static FoundationException NotFound(string message)
        => new(FoundationFailure.NotFound, "NOT_FOUND", message);

    public static FoundationException Conflict(string code, string message)
        => new(FoundationFailure.Conflict, code, message);
}

// ---------------------------------------------------------------------------
// Autentikasi & akun
// ---------------------------------------------------------------------------

/// <summary>Identitas pelaku hasil resolusi sesi — dipakai semua operasi backend.</summary>
public sealed class CallerPrincipal
{
    public string AccountId { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public bool MustChangePassword { get; set; }
    public List<string> Permissions { get; set; } = new();
    public ProductionContextDto? Production { get; set; }

    public bool Has(string permission) => Permissions.Contains(permission, StringComparer.Ordinal);

    public void Require(params string[] anyOf)
    {
        if (anyOf.Length == 0 || anyOf.Any(Has)) return;
        throw FoundationErrors.Forbidden(anyOf[0]);
    }
}

public sealed class AuthProfileDto
{
    public string AccountId { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Status { get; set; } = EntityStatus.Active;
    public bool MustChangePassword { get; set; }
    public List<string> Permissions { get; set; } = new();
    public ProductionContextDto? Production { get; set; }
}

public sealed class LoginResultDto
{
    /// <summary>Token opaque; hanya dikembalikan saat login. Server menyimpan hash-nya.</summary>
    public string Token { get; set; } = string.Empty;
    public string ExpiresAtUtc { get; set; } = string.Empty;
    public AuthProfileDto Profile { get; set; } = new();
}

public sealed class AccountDto
{
    public string Id { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Status { get; set; } = EntityStatus.Active;
    public bool MustChangePassword { get; set; }
    public List<string> Permissions { get; set; } = new();
    public string CreatedAtUtc { get; set; } = string.Empty;
}

public sealed class CreateAccountInput
{
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public List<string> Permissions { get; set; } = new();
}

public sealed class UpdateAccountInput
{
    public string Id { get; set; } = string.Empty;
    public string? FullName { get; set; }
    public string? Status { get; set; }
    public List<string>? Permissions { get; set; }
    /// <summary>Reset password oleh admin; memicu wajib ganti password saat login berikutnya.</summary>
    public string? NewPassword { get; set; }
}

public sealed class AuthSessionDto
{
    public string Id { get; set; } = string.Empty;
    public string AccountId { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string CreatedAtUtc { get; set; } = string.Empty;
    public string ExpiresAtUtc { get; set; } = string.Empty;
    public string LastSeenAtUtc { get; set; } = string.Empty;
    public bool Revoked { get; set; }
}

public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string hash, string password);
}

public interface IAccountService
{
    LoginResultDto Login(string email, string password);
    /// <summary>Null bila token kosong/tidak dikenal. Melempar Unauthorized bila kedaluwarsa/revoke/akun nonaktif.</summary>
    CallerPrincipal? ResolveSession(string? token, bool includeProduction = true);
    void Logout(string? token);
    IReadOnlyList<AccountDto> ListAccounts();
    AccountDto CreateAccount(CreateAccountInput input);
    AccountDto UpdateAccount(UpdateAccountInput input);
    void ChangeOwnPassword(string accountId, string currentPassword, string newPassword);
    IReadOnlyList<AuthSessionDto> ListSessions(string? accountIdFilter);
    void RevokeSession(string sessionId, CallerPrincipal caller);
    /// <summary>Dipanggil saat start: provisioning admin awal tanpa password bawaan.</summary>
    string ProvisionBootstrapAdmin(string? email, string? password);
    bool HasAnyAccount();
}

// ---------------------------------------------------------------------------
// Master produksi
// ---------------------------------------------------------------------------

public sealed class MasterCatalogDto
{
    public List<FamilyRecord> Families { get; set; } = new();
    public List<SkuRecord> Skus { get; set; } = new();
    public List<ShiftRecord> Shifts { get; set; } = new();
    public List<LineRecord> Lines { get; set; } = new();
    public List<StageRecord> Stages { get; set; } = new();
}

/// <summary>Input generik CRUD master; field tidak relevan diabaikan per entitas.</summary>
public sealed class MasterItemInput
{
    public string? Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? FamilyId { get; set; }
    public string? StageId { get; set; }
    public string? GroupCode { get; set; }
    public int? Seq { get; set; }
    public string? Status { get; set; }
}

public interface IMasterCatalogService
{
    MasterCatalogDto GetCatalog(bool activeOnly);
    MasterCatalogDto GetCatalogForFamily(string familyId, bool activeOnly);
    void UpsertFamily(MasterItemInput input);
    void UpsertSku(MasterItemInput input);
    void UpsertShift(MasterItemInput input);
    void UpsertLine(MasterItemInput input);
    void UpsertStage(MasterItemInput input);
    void UpsertDefect(MasterItemInput input);
    /// <summary>Nonaktifkan/aktifkan kembali; hanya memengaruhi pilihan baru.</summary>
    void SetStatus(string entity, string id, string status);
}

/// <summary>Menyelaraskan master DB aktif ke AppConfig engine lama (snapshot dropdown scan).</summary>
public interface IMasterProjection
{
    void Apply(AppConfig config);
}

// ---------------------------------------------------------------------------
// Sesi produksi & WO
// ---------------------------------------------------------------------------

public sealed class WorkOrderDto
{
    public string Id { get; set; } = string.Empty;
    public string Number { get; set; } = string.Empty;
    public string Client { get; set; } = string.Empty;
    public string ShippingReference { get; set; } = string.Empty;
    public int TargetQty { get; set; }
}

/// <summary>
/// Kontrak konteks aktif yang diserahkan ke fase lain: identitas pelaku, WO aktif,
/// snapshot nama/kode master, dan versi konteks untuk menolak transaksi basi.
/// </summary>
public sealed class ProductionContextDto
{
    public string ProductionSessionId { get; set; } = string.Empty;
    public string OperatorName { get; set; } = string.Empty;
    public string ShiftCode { get; set; } = string.Empty;
    public string ShiftName { get; set; } = string.Empty;
    public string LineCode { get; set; } = string.Empty;
    public string LineName { get; set; } = string.Empty;
    public string SkuCode { get; set; } = string.Empty;
    public string SkuName { get; set; } = string.Empty;
    public string FamilyCode { get; set; } = string.Empty;
    public string? ActiveWorkOrderId { get; set; }
    public string? ActiveWorkOrderNumber { get; set; }
    public string? WorkOrderClient { get; set; }
    public string? ShippingReference { get; set; }
    public int? TargetQty { get; set; }
    public int ContextVersion { get; set; }
    public string StartedAtUtc { get; set; } = string.Empty;
    public List<WorkOrderDto> WorkOrders { get; set; } = new();
}

public sealed class WorkOrderInput
{
    public string Number { get; set; } = string.Empty;
    public string Client { get; set; } = string.Empty;
    public string ShippingReference { get; set; } = string.Empty;
    public int TargetQty { get; set; }
}

public sealed class StartProductionInput
{
    public string ShiftId { get; set; } = string.Empty;
    public string LineId { get; set; } = string.Empty;
    public string SkuId { get; set; } = string.Empty;
    public List<WorkOrderInput> WorkOrders { get; set; } = new();
    public string? ActiveWorkOrderNumber { get; set; }
}

public interface IProductionSessionService
{
    ProductionContextDto? GetCurrent(string accountId);
    ProductionContextDto Start(string accountId, string operatorName, StartProductionInput input);
    ProductionContextDto SetActiveWorkOrder(string accountId, string workOrderNumber);
    ProductionContextDto AddWorkOrder(string accountId, WorkOrderInput input);
    void End(string accountId);
}
