using System.Security.Cryptography;
using System.Text;
using Traceability.Application;
using Traceability.Domain;

namespace Traceability.Infrastructure.Foundation;

/// <summary>
/// Login email/password dengan hash dari fasilitas autentikasi framework,
/// sesi server-side yang dapat diakhiri, dan provisioning admin awal
/// tanpa password bawaan (Fase 1 langkah 3).
/// </summary>
public sealed class AccountService : IAccountService
{
    private readonly AccountStore _store;
    private readonly IProductionSessionService _production;
    private readonly IPasswordHasher _hasher;
    private readonly TraceDbOptions _options;

    public AccountService(AccountStore store, IProductionSessionService production,
        IPasswordHasher hasher, TraceDbOptions options)
    {
        _store = store;
        _production = production;
        _hasher = hasher;
        _options = options;
    }

    public bool HasAnyAccount() => _store.HasAnyAccount();

    public LoginResultDto Login(string email, string password)
    {
        var normalizedEmail = (email ?? string.Empty).Trim();
        if (normalizedEmail.Length == 0 || string.IsNullOrEmpty(password))
        {
            throw FoundationErrors.BadRequest("LOGIN_INPUT", "Email dan password wajib diisi.");
        }

        var account = _store.FindByEmail(normalizedEmail);
        if (account is null || !_hasher.Verify(account.PasswordHash, password))
        {
            // Pesan generik: tidak membocorkan email mana yang terdaftar.
            throw FoundationErrors.BadRequest("LOGIN_DENIED", "Email atau password salah.");
        }

        if (!string.Equals(account.Status, EntityStatus.Active, StringComparison.Ordinal))
        {
            throw AccountServiceErrors.ForbiddenLoginDisabled();
        }

        var token = NewToken();
        var now = DateTimeOffset.UtcNow;
        var expires = now.AddHours(Math.Max(1, _options.SessionLifetimeHours));
        _store.InsertSession(new AuthSessionRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            AccountId = account.Id,
            TokenHash = HashToken(token),
            CreatedAtUtc = now.ToString("O"),
            ExpiresAtUtc = expires.ToString("O"),
            LastSeenAtUtc = now.ToString("O"),
        });

        return new LoginResultDto
        {
            Token = token,
            ExpiresAtUtc = expires.ToString("O"),
            Profile = ToProfile(account, _production.GetCurrent(account.Id)),
        };
    }

    public CallerPrincipal? ResolveSession(string? token, bool includeProduction = true)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;

        var session = _store.FindSessionByTokenHash(HashToken(token.Trim()));
        if (session is null) return null;

        if (session.RevokedAtUtc is not null)
        {
            throw FoundationErrors.Unauthorized("Sesi sudah diakhiri. Silakan login ulang.");
        }

        if (DateTimeOffset.TryParse(session.ExpiresAtUtc, out var expires) && expires <= DateTimeOffset.UtcNow)
        {
            throw FoundationErrors.Unauthorized("Sesi login sudah berakhir. Silakan login ulang.");
        }

        var account = _store.FindById(session.AccountId);
        if (account is null) return null;

        if (!string.Equals(account.Status, EntityStatus.Active, StringComparison.Ordinal))
        {
            throw AccountServiceErrors.ForbiddenLoginDisabled();
        }

        _store.TouchSession(session.Id, DateTimeOffset.UtcNow.ToString("O"));

        return new CallerPrincipal
        {
            AccountId = account.Id,
            Email = account.Email,
            FullName = account.FullName,
            MustChangePassword = account.MustChangePassword,
            Permissions = account.Permissions,
            Production = includeProduction ? _production.GetCurrent(account.Id) : null,
        };
    }

    public void Logout(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return;
        var session = _store.FindSessionByTokenHash(HashToken(token.Trim()));
        if (session is not null)
        {
            _store.RevokeSession(session.Id, DateTimeOffset.UtcNow.ToString("O"));
        }
    }

    public IReadOnlyList<AccountDto> ListAccounts()
    {
        return _store.ListAccounts().Select(ToDto).ToList();
    }

    public AccountDto CreateAccount(CreateAccountInput input)
    {
        var email = (input.Email ?? string.Empty).Trim();
        var fullName = (input.FullName ?? string.Empty).Trim();
        if (email.Length == 0 || !email.Contains('@'))
        {
            throw FoundationErrors.BadRequest("ACCOUNT_EMAIL", "Email akun tidak valid.");
        }

        if (fullName.Length == 0)
        {
            throw FoundationErrors.BadRequest("ACCOUNT_NAME", "Nama lengkap wajib diisi.");
        }

        if (string.IsNullOrEmpty(input.Password) || input.Password.Length < 8)
        {
            throw FoundationErrors.BadRequest("ACCOUNT_PASSWORD", "Password minimal 8 karakter.");
        }

        if (_store.FindByEmail(email) is not null)
        {
            throw FoundationErrors.Conflict("ACCOUNT_EXISTS", $"Email {email} sudah terdaftar.");
        }

        var permissions = PermissionCatalog.NormalizeMany(input.Permissions);
        if (permissions.Count == 0)
        {
            throw FoundationErrors.BadRequest("ACCOUNT_PERMISSIONS", "Pilih minimal satu hak operasi.");
        }

        var nowUtc = DateTimeOffset.UtcNow.ToString("O");
        var account = new AccountRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            Email = email,
            FullName = fullName,
            PasswordHash = _hasher.Hash(input.Password),
            Status = EntityStatus.Active,
            MustChangePassword = false,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
            Permissions = permissions,
        };
        _store.Insert(account, permissions);
        return ToDto(account);
    }

    public AccountDto UpdateAccount(UpdateAccountInput input)
    {
        var account = _store.FindById(input.Id ?? string.Empty)
            ?? throw FoundationErrors.NotFound("Akun tidak ditemukan.");

        if (!string.IsNullOrWhiteSpace(input.FullName)) account.FullName = input.FullName.Trim();
        if (!string.IsNullOrWhiteSpace(input.Status)) account.Status = EntityStatus.Normalize(input.Status);
        if (!string.IsNullOrWhiteSpace(input.NewPassword))
        {
            if (input.NewPassword.Length < 8)
            {
                throw FoundationErrors.BadRequest("ACCOUNT_PASSWORD", "Password minimal 8 karakter.");
            }

            account.PasswordHash = _hasher.Hash(input.NewPassword);
            account.MustChangePassword = true;
            // Reset password oleh admin mengakhiri seluruh sesi aktif akun.
            _store.RevokeAllSessions(account.Id, DateTimeOffset.UtcNow.ToString("O"));
        }

        List<string>? permissions = input.Permissions is null
            ? null
            : PermissionCatalog.NormalizeMany(input.Permissions);
        if (permissions is not null && permissions.Count == 0)
        {
            throw FoundationErrors.BadRequest("ACCOUNT_PERMISSIONS", "Pilih minimal satu hak operasi.");
        }

        account.UpdatedAtUtc = DateTimeOffset.UtcNow.ToString("O");
        _store.Update(account, permissions);
        return ToDto(_store.FindById(account.Id) ?? account);
    }

    public void ChangeOwnPassword(string accountId, string currentPassword, string newPassword)
    {
        var account = _store.FindById(accountId)
            ?? throw FoundationErrors.NotFound("Akun tidak ditemukan.");
        if (!_hasher.Verify(account.PasswordHash, currentPassword ?? string.Empty))
        {
            throw FoundationErrors.BadRequest("PASSWORD_WRONG", "Password saat ini salah.");
        }

        if (string.IsNullOrEmpty(newPassword) || newPassword.Length < 8)
        {
            throw FoundationErrors.BadRequest("PASSWORD_WEAK", "Password baru minimal 8 karakter.");
        }

        account.PasswordHash = _hasher.Hash(newPassword);
        account.MustChangePassword = false;
        account.UpdatedAtUtc = DateTimeOffset.UtcNow.ToString("O");
        _store.Update(account, null);
    }

    public IReadOnlyList<AuthSessionDto> ListSessions(string? accountIdFilter)
    {
        var nowUtc = DateTimeOffset.UtcNow;
        return _store.ListSessions(accountIdFilter).Select(session => new AuthSessionDto
        {
            Id = session.Id,
            AccountId = session.AccountId,
            Email = _store.EmailOfAccount(session.AccountId) ?? string.Empty,
            CreatedAtUtc = session.CreatedAtUtc,
            ExpiresAtUtc = session.ExpiresAtUtc,
            LastSeenAtUtc = session.LastSeenAtUtc,
            Revoked = session.RevokedAtUtc is not null
                       || (DateTimeOffset.TryParse(session.ExpiresAtUtc, out var exp) && exp <= nowUtc),
        }).ToList();
    }

    public void RevokeSession(string sessionId, CallerPrincipal caller)
    {
        var session = _store.ListSessions(null).FirstOrDefault(item => item.Id == sessionId)
            ?? throw FoundationErrors.NotFound("Sesi tidak ditemukan.");
        if (session.AccountId != caller.AccountId && !caller.Has(PermissionCatalog.AdminAccounts))
        {
            throw FoundationErrors.Forbidden(PermissionCatalog.AdminAccounts);
        }

        _store.RevokeSession(sessionId, DateTimeOffset.UtcNow.ToString("O"));
    }

    /// <summary>
    /// Provisioning admin awal tanpa password bawaan: hanya berjalan bila tabel
    /// akun masih kosong. Password bisa disuplai lewat environment; tanpa suplai,
    /// password acak dibuat dan dilaporkan sekali melalui hasil (log start-up).
    /// </summary>
    public string ProvisionBootstrapAdmin(string? email, string? password)
    {
        if (_store.HasAnyAccount()) return "Akun sudah ada; provisioning admin dilewati.";

        var normalizedEmail = (email ?? string.Empty).Trim();
        if (normalizedEmail.Length == 0)
        {
            return "Provisioning admin dilewati: Trace__BootstrapAdmin__Email belum diset. " +
                   "Set email admin awal lalu jalankan ulang API.";
        }

        var generated = string.IsNullOrEmpty(password);
        var effectivePassword = generated ? GeneratePassword() : password!;
        var nowUtc = DateTimeOffset.UtcNow.ToString("O");
        var account = new AccountRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            Email = normalizedEmail,
            FullName = "Administrator",
            PasswordHash = _hasher.Hash(effectivePassword),
            Status = EntityStatus.Active,
            MustChangePassword = generated,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
            Permissions = PermissionCatalog.AdminDefaults.ToList(),
        };
        _store.Insert(account, account.Permissions);

        return generated
            ? $"Admin awal \"{normalizedEmail}\" dibuat dengan password sementara (sekali tampil, wajib diganti): {effectivePassword}"
            : $"Admin awal \"{normalizedEmail}\" dibuat dari konfigurasi environment.";
    }

    private AuthProfileDto ToProfile(AccountRecord account, ProductionContextDto? production)
    {
        return new AuthProfileDto
        {
            AccountId = account.Id,
            Email = account.Email,
            FullName = account.FullName,
            Status = account.Status,
            MustChangePassword = account.MustChangePassword,
            Permissions = account.Permissions,
            Production = production,
        };
    }

    private static AccountDto ToDto(AccountRecord account) => new()
    {
        Id = account.Id,
        Email = account.Email,
        FullName = account.FullName,
        Status = account.Status,
        MustChangePassword = account.MustChangePassword,
        Permissions = account.Permissions,
        CreatedAtUtc = account.CreatedAtUtc,
    };

    private static string NewToken()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    private static string HashToken(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes);
    }

    private static string GeneratePassword()
    {
        const string alphabet = "abcdefghijkmnopqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789@#%$";
        var chars = alphabet.ToCharArray();
        var password = new char[16];
        for (var i = 0; i < password.Length; i += 1)
        {
            password[i] = chars[RandomNumberGenerator.GetInt32(chars.Length)];
        }

        return new string(password);
    }
}

public sealed class MasterCatalogService : IMasterCatalogService
{
    private readonly MasterCatalogStore _store;
    private readonly IMasterProjection _projection;
    private readonly ITraceStore _traceStore;

    public MasterCatalogService(MasterCatalogStore store, IMasterProjection projection, ITraceStore traceStore)
    {
        _store = store;
        _projection = projection;
        _traceStore = traceStore;
    }

    public MasterCatalogDto GetCatalog(bool activeOnly)
    {
        return BuildCatalog(null, activeOnly);
    }

    public MasterCatalogDto GetCatalogForFamily(string familyId, bool activeOnly)
    {
        return BuildCatalog(familyId, activeOnly);
    }

    public void UpsertFamily(MasterItemInput input)
    {
        var code = RequireCode(input.Code, "Kode keluarga wajib diisi.");
        var name = RequireName(input.Name);
        _store.Upsert("product_families", input.Id ?? string.Empty, new Dictionary<string, object?>
        {
            ["code"] = code,
            ["name"] = name,
            ["status"] = EntityStatus.Normalize(input.Status),
        });
        SyncProjection();
    }

    public void UpsertSku(MasterItemInput input)
    {
        var code = RequireCode(input.Code, "Kode SKU wajib diisi.");
        var name = RequireName(input.Name);
        var family = _store.FindFamily(input.FamilyId ?? string.Empty)
            ?? throw FoundationErrors.BadRequest("SKU_FAMILY", "SKU wajib terkait keluarga produk yang sah.");
        _store.Upsert("product_skus", input.Id ?? string.Empty, new Dictionary<string, object?>
        {
            ["family_id"] = family.Id,
            ["code"] = code,
            ["name"] = name,
            ["status"] = EntityStatus.Normalize(input.Status),
        });
        SyncProjection();
    }

    public void UpsertShift(MasterItemInput input)
    {
        _store.Upsert("shifts", input.Id ?? string.Empty, new Dictionary<string, object?>
        {
            ["code"] = RequireCode(input.Code, "Kode shift wajib diisi."),
            ["name"] = RequireName(input.Name),
            ["status"] = EntityStatus.Normalize(input.Status),
        });
        SyncProjection();
    }

    public void UpsertLine(MasterItemInput input)
    {
        _store.Upsert("lines", input.Id ?? string.Empty, new Dictionary<string, object?>
        {
            ["code"] = RequireCode(input.Code, "Kode line wajib diisi."),
            ["name"] = RequireName(input.Name),
            ["status"] = EntityStatus.Normalize(input.Status),
        });
        SyncProjection();
    }

    public void UpsertStage(MasterItemInput input)
    {
        var code = RequireCode(input.Code, "Kode tahap wajib diisi (mis. ST-10).").ToUpperInvariant();
        var name = RequireName(input.Name);
        var family = _store.FindFamily(input.FamilyId ?? string.Empty)
            ?? throw FoundationErrors.BadRequest("STAGE_FAMILY", "Tahap wajib terkait keluarga produk yang sah.");
        var seq = input.Seq is > 0 ? input.Seq.Value : NextSeq(code);
        _store.Upsert("stages", input.Id ?? string.Empty, new Dictionary<string, object?>
        {
            ["family_id"] = family.Id,
            ["code"] = code,
            ["name"] = name,
            ["seq"] = seq,
            ["status"] = EntityStatus.Normalize(input.Status),
        });
        SyncProjection();
    }

    public void UpsertDefect(MasterItemInput input)
    {
        var name = RequireName(input.Name);
        var stage = _store.FindStage(input.StageId ?? string.Empty)
            ?? throw FoundationErrors.BadRequest("DEFECT_STAGE", "Defect wajib terkait tahap yang sah.");
        var groupCode = (input.GroupCode ?? string.Empty).Trim().ToUpperInvariant();
        if (groupCode.Length > 0 && !groupCode.StartsWith("DF-", StringComparison.Ordinal))
        {
            throw FoundationErrors.BadRequest("DEFECT_GROUP", "Kode grup defect harus berformat DF-xx.");
        }

        // Kode grup mengikuti posnya bila tidak diisi (DF-01 untuk ST-01, dst).
        if (groupCode.Length == 0) groupCode = "DF-" + stage.Code.Split('-')[^1];

        _store.Upsert("defects", input.Id ?? string.Empty, new Dictionary<string, object?>
        {
            ["stage_id"] = stage.Id,
            ["group_code"] = groupCode,
            ["name"] = name,
            ["status"] = EntityStatus.Normalize(input.Status),
        });
        SyncProjection();
    }

    public void SetStatus(string entity, string id, string status)
    {
        var normalized = EntityStatus.Normalize(status);
        switch (entity.ToLowerInvariant())
        {
            case "family":
                RequireExists(_store.FindFamily(id), "Keluarga produk tidak ditemukan.");
                _store.SetStatus("product_families", id, normalized);
                break;
            case "sku":
                RequireExists(_store.FindSku(id), "SKU tidak ditemukan.");
                _store.SetStatus("product_skus", id, normalized);
                break;
            case "shift":
                RequireExists(_store.FindShift(id), "Shift tidak ditemukan.");
                _store.SetStatus("shifts", id, normalized);
                break;
            case "line":
                RequireExists(_store.FindLine(id), "Line tidak ditemukan.");
                _store.SetStatus("lines", id, normalized);
                break;
            case "stage":
                RequireExists(_store.FindStage(id), "Tahap tidak ditemukan.");
                _store.SetStatus("stages", id, normalized);
                break;
            case "defect":
                RequireExists(_store.FindDefect(id), "Defect tidak ditemukan.");
                _store.SetStatus("defects", id, normalized);
                break;
            default:
                throw FoundationErrors.BadRequest("MASTER_ENTITY", $"Entitas master \"{entity}\" tidak dikenal.");
        }

        SyncProjection();
    }

    private MasterCatalogDto BuildCatalog(string? familyId, bool activeOnly)
    {
        var catalog = new MasterCatalogDto
        {
            Families = _store.Families(!activeOnly),
            Skus = _store.Skus(!activeOnly),
            Shifts = _store.Shifts(!activeOnly),
            Lines = _store.Lines(!activeOnly),
            Stages = _store.Stages(familyId, !activeOnly),
        };

        if (activeOnly)
        {
            var familyIds = catalog.Families.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
            catalog.Skus = catalog.Skus.Where(item => familyIds.Contains(item.FamilyId)).ToList();
        }

        return catalog;
    }

    private static int NextSeq(string stageCode)
    {
        var tail = stageCode.Split('-')[^1];
        return int.TryParse(tail, out var number) ? number : 99;
    }

    private static string RequireCode(string? value, string message)
    {
        var code = (value ?? string.Empty).Trim();
        if (code.Length == 0) throw FoundationErrors.BadRequest("MASTER_CODE", message);
        return code;
    }

    private static string RequireName(string? value)
    {
        var name = (value ?? string.Empty).Trim();
        if (name.Length == 0) throw FoundationErrors.BadRequest("MASTER_NAME", "Nama master wajib diisi.");
        return name;
    }

    private static void RequireExists(object? found, string message)
    {
        if (found is null) throw FoundationErrors.NotFound(message);
    }

    /// <summary>Perubahan master langsung tercermin ke pilihan engine scan (snapshot dropdown).</summary>
    private void SyncProjection()
    {
        lock (_traceStore.Gate)
        {
            _projection.Apply(_traceStore.State.Config);
        }
    }
}

/// <summary>
/// Sesi produksi & WO (Fase 1 langkah 5). Memvalidasi master aktif, relasi
/// keluarga SKU, target pcs positif, dan menjaga context_version untuk menolak
/// transaksi dengan konteks lama.
/// </summary>
public sealed class ProductionSessionService : IProductionSessionService
{
    private readonly ProductionStore _store;
    private readonly MasterCatalogStore _catalog;

    public ProductionSessionService(ProductionStore store, MasterCatalogStore catalog)
    {
        _store = store;
        _catalog = catalog;
    }

    public ProductionContextDto? GetCurrent(string accountId)
    {
        var session = _store.FindActiveForAccount(accountId);
        return session is null ? null : ToContext(session);
    }

    public ProductionContextDto Start(string accountId, string operatorName, StartProductionInput input)
    {
        var shift = _catalog.FindShift(input.ShiftId ?? string.Empty);
        if (shift is null || !string.Equals(shift.Status, EntityStatus.Active, StringComparison.Ordinal))
        {
            throw FoundationErrors.BadRequest("SHIFT_INVALID", "Shift tidak ditemukan atau sudah nonaktif.");
        }

        var line = _catalog.FindLine(input.LineId ?? string.Empty);
        if (line is null || !string.Equals(line.Status, EntityStatus.Active, StringComparison.Ordinal))
        {
            throw FoundationErrors.BadRequest("LINE_INVALID", "Line tidak ditemukan atau sudah nonaktif.");
        }

        var sku = _catalog.FindSku(input.SkuId ?? string.Empty)
            ?? throw FoundationErrors.BadRequest("SKU_UNKNOWN", $"SKU \"{input.SkuId}\" tidak ditemukan.");
        if (!string.Equals(sku.Status, EntityStatus.Active, StringComparison.Ordinal))
        {
            throw FoundationErrors.BadRequest("SKU_INACTIVE", $"SKU \"{sku.Code}\" sudah nonaktif.");
        }

        var family = _catalog.FindFamily(sku.FamilyId)
            ?? throw FoundationErrors.BadRequest("SKU_FAMILY", $"SKU \"{sku.Code}\" tidak memiliki keluarga sah.");

        if (input.WorkOrders.Count == 0)
        {
            throw FoundationErrors.BadRequest("WO_REQUIRED", "Isi minimal satu WO untuk mulai produksi.");
        }

        var nowUtc = DateTimeOffset.UtcNow.ToString("O");
        var session = new ProductionSessionRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            AccountId = accountId,
            OperatorName = operatorName,
            ShiftId = shift.Id,
            LineId = line.Id,
            SkuId = sku.Id,
            ShiftCode = shift.Code,
            ShiftName = shift.Name,
            LineCode = line.Code,
            LineName = line.Name,
            SkuCode = sku.Code,
            SkuName = sku.Name,
            FamilyCode = family.Code,
            StartedAtUtc = nowUtc,
        };

        var workOrderNumbers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var draft in input.WorkOrders)
        {
            session.WorkOrders.Add(NewWorkOrder(session.Id, draft, workOrderNumbers));
        }

        var activeNumber = (input.ActiveWorkOrderNumber ?? string.Empty).Trim();
        session.ActiveWorkOrderId = string.IsNullOrEmpty(activeNumber)
            ? session.WorkOrders[0].Id
            : MatchWorkOrder(session, activeNumber).Id;

        // Satu operator hanya satu sesi produksi aktif; sesi lama ditutup otomatis.
        _store.InsertSession(session);
        return ToContext(session);
    }

    public ProductionContextDto SetActiveWorkOrder(string accountId, string workOrderNumber)
    {
        var session = RequireActiveSession(accountId);
        var workOrder = MatchWorkOrder(session, workOrderNumber);

        // WO valid terhadap sesi & SKU-nya (SKU melekat pada sesi, jadi dijamin konsisten).
        session.ActiveWorkOrderId = workOrder.Id;
        session.ContextVersion += 1;
        _store.SetActiveWorkOrder(session.Id, workOrder.Id, session.ContextVersion);
        return ToContext(session);
    }

    public ProductionContextDto AddWorkOrder(string accountId, WorkOrderInput input)
    {
        var session = RequireActiveSession(accountId);
        var workOrder = NewWorkOrder(session.Id, input,
            new HashSet<string>(session.WorkOrders.Select(item => item.Number), StringComparer.OrdinalIgnoreCase));
        _store.InsertWorkOrder(session.Id, workOrder);
        session.WorkOrders.Add(workOrder);
        return ToContext(session);
    }

    public void End(string accountId)
    {
        var session = RequireActiveSession(accountId);
        _store.EndSession(session.Id, DateTimeOffset.UtcNow.ToString("O"), session.ContextVersion + 1);
    }

    private ProductionSessionRecord RequireActiveSession(string accountId)
    {
        return _store.FindActiveForAccount(accountId)
            ?? throw FoundationErrors.Conflict("NO_PRODUCTION_SESSION",
                "Belum ada sesi produksi aktif. Gunakan Mulai produksi di beranda.");
    }

    private WorkOrderRecord NewWorkOrder(string sessionId, WorkOrderInput input, HashSet<string> taken)
    {
        var number = (input.Number ?? string.Empty).Trim().ToUpperInvariant();
        if (number.Length == 0)
        {
            throw FoundationErrors.BadRequest("WO_NUMBER", "Nomor WO wajib diisi.");
        }

        if (taken.Contains(number))
        {
            throw FoundationErrors.Conflict("WO_DUPLICATE",
                $"Nomor WO \"{number}\" sudah dipakai dalam sesi ini. " +
                "Scope identitas nomor WO lintas proses menunggu konfirmasi pemilik proses.");
        }

        var client = (input.Client ?? string.Empty).Trim();
        if (client.Length == 0)
        {
            throw FoundationErrors.BadRequest("WO_CLIENT", "Klien WO wajib diisi.");
        }

        if (input.TargetQty <= 0)
        {
            throw FoundationErrors.BadRequest("WO_TARGET", "Target WO harus bilangan pcs positif.");
        }

        taken.Add(number);
        return new WorkOrderRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            SessionId = sessionId,
            Number = number,
            Client = client,
            ShippingReference = OrDash(input.ShippingReference),
            TargetQty = input.TargetQty,
            CreatedAtUtc = DateTimeOffset.UtcNow.ToString("O"),
        };
    }

    private static WorkOrderRecord MatchWorkOrder(ProductionSessionRecord session, string numberOrId)
    {
        var needle = (numberOrId ?? string.Empty).Trim();
        var match = session.WorkOrders.FirstOrDefault(item =>
            string.Equals(item.Id, needle, StringComparison.Ordinal)
            || string.Equals(item.Number, needle, StringComparison.OrdinalIgnoreCase));
        return match ?? throw FoundationErrors.BadRequest("WO_ACTIVE",
            $"WO \"{numberOrId}\" tidak dapat dipakai pada sesi dan SKU terpilih.");
    }

    private ProductionContextDto ToContext(ProductionSessionRecord session)
    {
        var active = session.WorkOrders.FirstOrDefault(item => item.Id == session.ActiveWorkOrderId);
        return new ProductionContextDto
        {
            ProductionSessionId = session.Id,
            OperatorName = session.OperatorName,
            ShiftCode = session.ShiftCode,
            ShiftName = session.ShiftName,
            LineCode = session.LineCode,
            LineName = session.LineName,
            SkuCode = session.SkuCode,
            SkuName = session.SkuName,
            FamilyCode = session.FamilyCode,
            ActiveWorkOrderId = active?.Id,
            ActiveWorkOrderNumber = active?.Number,
            WorkOrderClient = active?.Client,
            ShippingReference = active?.ShippingReference,
            TargetQty = active?.TargetQty,
            ContextVersion = session.ContextVersion,
            StartedAtUtc = session.StartedAtUtc,
            WorkOrders = session.WorkOrders.Select(item => new WorkOrderDto
            {
                Id = item.Id,
                Number = item.Number,
                Client = item.Client,
                ShippingReference = item.ShippingReference,
                TargetQty = item.TargetQty,
            }).ToList(),
        };
    }

    private static string OrDash(string? value)
    {
        var trimmed = (value ?? string.Empty).Trim();
        return trimmed.Length == 0 ? "-" : trimmed;
    }
}

/// <summary>
/// Menerjemahkan master DB aktif menjadi daftar yang dipakai engine scan lama
/// (AppConfig) agar snapshot & validasi konsisten dengan sumber resmi.
/// </summary>
public sealed class MasterProjection : IMasterProjection
{
    private readonly MasterCatalogStore _store;

    public MasterProjection(MasterCatalogStore store) => _store = store;

    public void Apply(AppConfig config)
    {
        var skus = _store.Skus(false).Select(item => item.Name).ToList();
        if (skus.Count > 0) Replace(config.Skus, skus);

        var lines = _store.Lines(false).Select(item => item.Name).ToList();
        if (lines.Count > 0) Replace(config.Lines, lines);

        var shifts = _store.Shifts(false).Select(item => item.Name).ToList();
        if (shifts.Count > 0) Replace(config.Shifts, shifts);

        // Grup defect ditampilkan bersama kodenya (SSOT §2) namun tiap opsi tetap baris terpisah.
        var stations = _store.Stages(null, false)
            .OrderBy(item => item.Seq)
            .Select(stage => new NgStation
            {
                Id = stage.Code,
                Label = $"{stage.Code}: {stage.Name}",
                Defects = stage.Defects
                    .Select(defect => $"{defect.GroupCode}: {defect.Name}")
                    .ToList(),
            })
            .ToList();
        if (stations.Count > 0)
        {
            config.NgStations.Clear();
            config.NgStations.AddRange(stations);
        }
    }

    private static void Replace(List<string> target, List<string> values)
    {
        target.Clear();
        target.AddRange(values);
    }
}

internal static class AccountServiceErrors
{
    public static FoundationException ForbiddenLoginDisabled() =>
        new(FoundationFailure.Forbidden, "ACCOUNT_DISABLED", "Akun tidak aktif. Hubungi administrator.");
}
