using Traceability.Api.Foundation;
using Traceability.Application;
using Traceability.Infrastructure;
using Traceability.Infrastructure.Foundation;

namespace Traceability.Api;

public static class ServiceRegistration
{
    /// <summary>
    /// Engine lama tetap memakai store in-memory; Fase 1 menambahkan fondasi
    /// relasional (SQLite) untuk akun, izin, sesi, master, dan WO. Satu-satunya
    /// titik ganti provider ada di TraceDb.
    /// </summary>
    public static IServiceCollection AddTraceabilityApi(this IServiceCollection services)
    {
        services.AddSingleton<ITraceStore, InMemoryTraceStore>();
        services.AddSingleton<ITraceService, TraceService>();
        services.AddSingleton<ITraceQueryService, TraceQueries>();
        services.AddSingleton<IConfigService, ConfigService>();

        services.AddSingleton(sp => BindFoundationOptions(sp.GetRequiredService<IConfiguration>()));
        services.AddSingleton(sp => new TraceDb(
            sp.GetRequiredService<TraceDbOptions>(),
            sp.GetRequiredService<IHostEnvironment>().ContentRootPath));

        services.AddSingleton<AccountStore>();
        services.AddSingleton<MasterCatalogStore>();
        services.AddSingleton<ProductionStore>();

        // Fase 2: stok label QR internal — ledger penerbitan hidup di luar file DB
        // supaya pemulihan backup tertinggal tetap dapat mengenal batas tertinggi.
        services.AddSingleton(sp => new LabelIssuanceLedger(sp.GetRequiredService<TraceDbOptions>()
            .ResolvedLedgerPath(sp.GetRequiredService<IHostEnvironment>().ContentRootPath)));
        services.AddSingleton<LabelStore>();
        services.AddSingleton<UsbPrintService>();
        services.AddSingleton<ILabelStockService, LabelStockService>();

        // Fase 3: alur karantina NG → rework → OK akhir, tegas lewat transaksi DB.
        services.AddSingleton<NgWorkflowStore>();
        services.AddSingleton<PackingService>();
        services.AddSingleton<INgWorkflowService, NgWorkflowService>();

        services.AddSingleton<IPasswordHasher, IdentityPasswordHasher>();
        services.AddSingleton<IProductionSessionService, ProductionSessionService>();
        services.AddSingleton<IAccountService, AccountService>();
        services.AddSingleton<IMasterProjection, MasterProjection>();
        services.AddSingleton<IMasterCatalogService, MasterCatalogService>();

        services.AddSingleton<FoundationExceptionFilter>();
        return services;
    }

    /// <summary>Seluruh opsi fondasi dapat diset lewat appsettings atau environment (Trace__...).</summary>
    private static TraceDbOptions BindFoundationOptions(IConfiguration configuration)
    {
        return new TraceDbOptions
        {
            Provider = configuration["Trace:Db:Provider"] ?? "sqlite",
            Path = configuration["Trace:Db:Path"] ?? "data/traceability.db",
            AuthEnforced = !string.Equals(configuration["Trace:Auth:Enforced"], "false", StringComparison.OrdinalIgnoreCase),
            BootstrapAdminEmail = Nullify(configuration["Trace:BootstrapAdmin:Email"]),
            BootstrapAdminPassword = Nullify(configuration["Trace:BootstrapAdmin:Password"]),
            SessionLifetimeHours = int.TryParse(configuration["Trace:Session:LifetimeHours"], out var hours) && hours > 0
                ? hours
                : 12,
            LabelMaxPerRequest = int.TryParse(configuration["Trace:Labels:MaxPerRequest"], out var maxLabels) && maxLabels > 0
                ? maxLabels
                : 500,
            LabelLedgerPath = Nullify(configuration["Trace:Labels:LedgerPath"]),
            NgMaxAttempts = int.TryParse(configuration["Trace:Ng:MaxAttempts"], out var maxAttempts) && maxAttempts > 0
                ? maxAttempts
                : 3,
        };
    }

    private static string? Nullify(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
