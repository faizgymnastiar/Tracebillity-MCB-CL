using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;
using Traceability.Application;
using Traceability.Domain;
using Traceability.Infrastructure.Foundation;

namespace Traceability.Api.Controllers;

/// <summary>
/// Basis controller traceability. Fase 1 memindahkan otoritasi ke backend:
/// browser/Next.js mengirim token sesi lewat header X-Trace-Session; pelaku,
/// izin, dan konteks produksi/WO aktif diselesaikan dari sesi server, bukan
/// dari header konteks yang bisa dimanipulasi. Header X-Trace-* lama hanya
/// dipakai ketika enforcement dimatikan (mode demo lokal).
/// </summary>
[ApiController]
public abstract class BaseApiController : ControllerBase
{
    protected BaseApiController(IConfigService config, IAccountService accounts, TraceDbOptions options)
    {
        Config = config;
        Accounts = accounts;
        Options = options;
    }

    protected IConfigService Config { get; }
    protected IAccountService Accounts { get; }
    protected TraceDbOptions Options { get; }

    protected bool Enforced => Options.AuthEnforced;

    /// <summary>Header X-Trace-Admin: granted — hanya berlaku ketika enforcement mati.</summary>
    protected bool IsAdminSession =>
        string.Equals(ReadHeader("X-Trace-Admin"), "granted", StringComparison.Ordinal);

    /// <summary>
    /// Identitas pelaku dari sesi login. Melempar 401/403 saat enforcement aktif
    /// dan token tidak ada/kedaluwarsa/nonaktif. Mode demo mengembalikan principal
    /// tamu tanpa izin tersimpan.
    /// </summary>
    protected CallerPrincipal RequireCaller(params string[] anyOfPermissions)
    {
        if (!Enforced)
        {
            var guest = new CallerPrincipal
            {
                AccountId = string.Empty,
                Email = "demo",
                FullName = ReadHeader("X-Trace-Operator") ?? "Demo Operator",
                Production = null,
            };
            guest.Permissions.AddRange(PermissionCatalog.All);
            guest.Require(anyOfPermissions);
            return guest;
        }

        var caller = Accounts.ResolveSession(ReadHeader("X-Trace-Session"))
            ?? throw FoundationErrors.Unauthorized();
        if (anyOfPermissions.Length > 0 && caller.MustChangePassword)
        {
            throw new FoundationException(FoundationFailure.Forbidden, "PASSWORD_CHANGE_REQUIRED",
                "Ganti kata sandi sebelum menggunakan fitur ini.");
        }
        caller.Require(anyOfPermissions);
        return caller;
    }

    /// <summary>Principal tanpa syarat izin — cukup sesi login yang sah.</summary>
    protected CallerPrincipal RequireLoggedIn() => RequireCaller();

    /// <summary>Konteks header lama (fallback default) — hanya untuk mode demo.</summary>
    protected OperatorContext ResolveContext()
    {
        var fallback = Config.GetDefaultContext();
        return new OperatorContext
        {
            Operator = ReadHeader("X-Trace-Operator") ?? fallback.Operator,
            WorkOrder = ReadHeader("X-Trace-Work-Order") ?? fallback.WorkOrder,
            MaterialLot = ReadHeader("X-Trace-Material-Lot") ?? fallback.MaterialLot,
            Line = ReadHeader("X-Trace-Line") ?? fallback.Line,
            Shift = ReadHeader("X-Trace-Shift") ?? fallback.Shift,
            Sku = ReadHeader("X-Trace-Sku") ?? fallback.Sku,
            Destination = ReadHeader("X-Trace-Destination") ?? fallback.Destination,
        };
    }

    /// <summary>
    /// Konteks untuk transaksi engine: saat enforcement aktif, selalu berasal
    /// dari sesi produksi (pelaku + shift/line/SKU/WO snapshot); tanpa sesi
    /// produksi operasi transaksi ditolak dengan 409.
    /// </summary>
    protected OperatorContext ResolveTransactionContext(CallerPrincipal caller)
    {
        if (!Enforced) return ResolveContext();
        if (caller.Production is null)
        {
            throw FoundationErrors.Conflict("NO_PRODUCTION_SESSION",
                "Belum ada sesi produksi aktif. Mulai produksi di beranda sebelum menyimpan transaksi.");
        }

        AssertContextVersionCurrent(caller);
        return ContextFromProduction(caller.Production, Config.GetDefaultContext().MaterialLot);
    }

    /// <summary>
    /// Menolak transaksi dari layar yang masih membawa konteks lama sampai
    /// konteks dimuat ulang (Fase 1 langkah 5).
    /// </summary>
    protected void AssertContextVersionCurrent(CallerPrincipal caller)
    {
        if (!Enforced || caller.Production is null) return;
        var provided = IntValue(ReadHeader("X-Trace-Context-Version"));
        if (provided != caller.Production.ContextVersion)
        {
            throw FoundationErrors.Conflict("CONTEXT_STALE",
                "Versi konteks produksi tidak sesuai dengan sesi aktif. Muat ulang konteks sebelum melanjutkan.");
        }
    }

    /// <summary>Snapshot scan boleh dibuka tanpa sesi produksi; konteks ditampilkan berasal dari sesi aktif.</summary>
    protected OperatorContext ResolveViewStateContext(CallerPrincipal caller)
    {
        if (Enforced && caller.Production is not null)
        {
            return ContextFromProduction(caller.Production, Config.GetDefaultContext().MaterialLot);
        }

        return ResolveContext();
    }

    protected static OperatorContext ContextFromProduction(ProductionContextDto production, string materialLot)
    {
        return new OperatorContext
        {
            Operator = production.OperatorName,
            WorkOrder = production.ActiveWorkOrderNumber ?? "-",
            MaterialLot = string.IsNullOrWhiteSpace(materialLot) ? "-" : materialLot,
            Line = production.LineName,
            Shift = production.ShiftName,
            Sku = production.SkuName,
            Destination = string.IsNullOrWhiteSpace(production.WorkOrderClient) ? "-" : production.WorkOrderClient,
            WorkOrderClient = production.WorkOrderClient ?? "-",
            ShippingReference = production.ShippingReference ?? "-",
            WorkOrderTarget = production.TargetQty ?? 0,
            ProductionSessionId = production.ProductionSessionId,
            ContextVersion = production.ContextVersion,
        };
    }

    /// <summary>Null bila sesi operator tidak dikirim, untuk endpoint yang mewajibkan login.</summary>
    protected OperatorContext? TryResolveOperator()
    {
        return ReadHeader("X-Trace-Operator") is null ? null : ResolveContext();
    }

    protected string? ReadHeader(string name)
    {
        var value = Request.Headers[name].ToString().Trim();
        return value.Length == 0 ? null : value;
    }

    /// <summary>Body JSON toleran: body kosong atau tidak valid dikembalikan sebagai objek kosong.</summary>
    protected async Task<JsonObject> ReadJsonObjectAsync()
    {
        return await TryReadJsonObjectAsync() ?? new JsonObject();
    }

    /// <summary>Null bila body kosong atau bukan JSON object.</summary>
    protected async Task<JsonObject?> TryReadJsonObjectAsync()
    {
        using var reader = new StreamReader(Request.Body);
        var raw = await reader.ReadToEndAsync();
        if (string.IsNullOrWhiteSpace(raw)) return null;

        try
        {
            return JsonNode.Parse(raw) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    protected static string? StringValue(JsonObject body, string name)
    {
        if (!body.TryGetPropertyValue(name, out var node) || node is not JsonValue value) return null;
        return value.TryGetValue<string>(out var text) ? text : value.ToString();
    }

    protected static int? IntValue(JsonObject body, string name)
    {
        if (!body.TryGetPropertyValue(name, out var node) || node is not JsonValue value) return null;
        if (value.TryGetValue<int>(out var number)) return number;
        return value.TryGetValue<string>(out var text) && int.TryParse(text, out var parsed) ? parsed : null;
    }

    protected static int? IntValue(string? raw)
    {
        return int.TryParse(raw, out var parsed) ? parsed : null;
    }
}
