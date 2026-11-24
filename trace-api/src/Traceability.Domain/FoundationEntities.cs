namespace Traceability.Domain;

/// <summary>Status umum master/akun. Penonaktifan hanya memengaruhi pilihan baru.</summary>
public static class EntityStatus
{
    public const string Active = "ACTIVE";
    public const string Inactive = "INACTIVE";

    public static string Normalize(string? value)
    {
        return string.Equals(value, Inactive, StringComparison.OrdinalIgnoreCase) ? Inactive : Active;
    }
}

/// <summary>
/// Hak operasi terpisah (SSOT §3). Satu akun dapat memegang beberapa hak sekaligus
/// sehingga satu operator bisa menjalankan keempat pos scan.
/// </summary>
public static class PermissionCatalog
{
    public const string ScanUnit = "scan.unit";
    public const string ScanInner = "scan.inner";
    public const string ScanOuter = "scan.outer";
    public const string NgRecord = "ng.record";
    public const string PrintManage = "print.manage";
    public const string HistoryRead = "history.read";
    public const string AdminMaster = "admin.master";
    public const string AdminAccounts = "admin.accounts";

    public static readonly string[] All =
    {
        ScanUnit, ScanInner, ScanOuter, NgRecord, PrintManage, HistoryRead, AdminMaster, AdminAccounts,
    };

    public static readonly Dictionary<string, string> Labels = new(StringComparer.Ordinal)
    {
        [ScanUnit] = "Scan Unit (OK akhir)",
        [ScanInner] = "Scan Inner Box",
        [ScanOuter] = "Scan Outer Box",
        [NgRecord] = "Pencatatan NG & Rework",
        [PrintManage] = "Generate & Cetak Label",
        [HistoryRead] = "Baca Riwayat & Laporan",
        [AdminMaster] = "Kelola Master Produksi",
        [AdminAccounts] = "Kelola Akun & Izin",
    };

    /// <summary>Hak produksi inti: scan unit, inner, outer, dan NG/rework.</summary>
    public static readonly string[] CoreScan = { ScanUnit, ScanInner, ScanOuter, NgRecord };

    /// <summary>Hak bawaan untuk akun admin yang diprovisioning.</summary>
    public static readonly string[] AdminDefaults =
    {
        ScanUnit, ScanInner, ScanOuter, NgRecord, PrintManage, HistoryRead, AdminMaster, AdminAccounts,
    };

    public static bool IsKnown(string? permission)
    {
        return permission is not null && All.Contains(permission, StringComparer.Ordinal);
    }

    public static List<string> NormalizeMany(IEnumerable<string>? values)
    {
        return (values ?? Enumerable.Empty<string>())
            .Select(item => item.Trim())
            .Where(IsKnown)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToList();
    }
}

public sealed class AccountRecord
{
    public string Id { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string Status { get; set; } = EntityStatus.Active;
    public bool MustChangePassword { get; set; }
    public string CreatedAtUtc { get; set; } = string.Empty;
    public string UpdatedAtUtc { get; set; } = string.Empty;
    public List<string> Permissions { get; set; } = new();
}

/// <summary>Sesi login server-side: token disimpan sebagai hash, dapat diakhiri/di-revoke.</summary>
public sealed class AuthSessionRecord
{
    public string Id { get; set; } = string.Empty;
    public string AccountId { get; set; } = string.Empty;
    public string TokenHash { get; set; } = string.Empty;
    public string CreatedAtUtc { get; set; } = string.Empty;
    public string ExpiresAtUtc { get; set; } = string.Empty;
    public string LastSeenAtUtc { get; set; } = string.Empty;
    public string? RevokedAtUtc { get; set; }
}

public sealed class FamilyRecord
{
    public string Id { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = EntityStatus.Active;
}

public sealed class SkuRecord
{
    public string Id { get; set; } = string.Empty;
    public string FamilyId { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = EntityStatus.Active;
}

public sealed class ShiftRecord
{
    public string Id { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = EntityStatus.Active;
}

public sealed class LineRecord
{
    public string Id { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = EntityStatus.Active;
}

public sealed class StageRecord
{
    public string Id { get; set; } = string.Empty;
    public string FamilyId { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Seq { get; set; }
    public string Status { get; set; } = EntityStatus.Active;
    public List<DefectRecord> Defects { get; set; } = new();
}

/// <summary>
/// Satu baris = satu pilihan defect dengan ID tersendiri, walau kode grup (DF-xx) berulang
/// dalam satu pos (SSOT §2).
/// </summary>
public sealed class DefectRecord
{
    public string Id { get; set; } = string.Empty;
    public string StageId { get; set; } = string.Empty;
    public string GroupCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = EntityStatus.Active;
}

public sealed class WorkOrderRecord
{
    public string Id { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string Number { get; set; } = string.Empty;
    public string Client { get; set; } = string.Empty;
    public string ShippingReference { get; set; } = string.Empty;
    public int TargetQty { get; set; }
    public string CreatedAtUtc { get; set; } = string.Empty;
}

/// <summary>
/// Sesi produksi harian operator. Nama/kode shift, line, SKU, dan keluarga disnapshot
/// saat sesi dibuat agar perubahan master tidak mengubah konteks transaksi historis.
/// </summary>
public sealed class ProductionSessionRecord
{
    public string Id { get; set; } = string.Empty;
    public string AccountId { get; set; } = string.Empty;
    public string OperatorName { get; set; } = string.Empty;
    public string ShiftId { get; set; } = string.Empty;
    public string LineId { get; set; } = string.Empty;
    public string SkuId { get; set; } = string.Empty;
    public string ShiftCode { get; set; } = string.Empty;
    public string ShiftName { get; set; } = string.Empty;
    public string LineCode { get; set; } = string.Empty;
    public string LineName { get; set; } = string.Empty;
    public string SkuCode { get; set; } = string.Empty;
    public string SkuName { get; set; } = string.Empty;
    public string FamilyCode { get; set; } = string.Empty;
    public string? ActiveWorkOrderId { get; set; }
    public int ContextVersion { get; set; } = 1;
    public string StartedAtUtc { get; set; } = string.Empty;
    public string? EndedAtUtc { get; set; }
    public List<WorkOrderRecord> WorkOrders { get; set; } = new();
}
