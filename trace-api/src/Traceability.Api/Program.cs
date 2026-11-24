using System.Text.Json;
using Microsoft.OpenApi.Models;
using Traceability.Api;
using Traceability.Api.Foundation;
using Traceability.Application;
using Traceability.Infrastructure;
using Traceability.Infrastructure.Foundation;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddControllers(options => options.Filters.Add<FoundationExceptionFilter>())
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.DictionaryKeyPolicy = null;
    });

builder.Services.AddTraceabilityApi();

var swaggerEnabled = builder.Configuration.GetValue("Swagger:Enabled", builder.Environment.IsDevelopment());
var internalKey = builder.Configuration["Trace:InternalApiKey"];
if (swaggerEnabled)
{
    builder.Services.AddSwaggerGen(options =>
    {
        options.SwaggerDoc("v1", new OpenApiInfo
        {
            Title = "LELCO Traceability API",
            Version = "v1",
            Description = "API backend untuk produksi, NG dan rework, stok label, packing, dan riwayat unit."
        });
        options.IncludeXmlComments(Path.Combine(AppContext.BaseDirectory, "Traceability.Api.xml"));
        options.AddSecurityDefinition("Session", new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.ApiKey, In = ParameterLocation.Header, Name = "X-Trace-Session",
            Description = "Masukkan nilai token dari POST /api/v1/auth/login."
        });
        if (!string.IsNullOrWhiteSpace(internalKey))
        {
            options.AddSecurityDefinition("InternalKey", new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.ApiKey, In = ParameterLocation.Header, Name = "X-Trace-Internal-Key",
                Description = "Kunci internal dari konfigurasi server."
            });
        }
        options.OperationFilter<TraceOpenApiOperationFilter>(!string.IsNullOrWhiteSpace(internalKey));
    });
}

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
if (allowedOrigins.Length > 0)
{
    builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
        policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod()));
}

var app = builder.Build();

// --- Fondasi Fase 1: migrasi DB, provisioning admin awal, proyeksi master ke engine ---
var foundationLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Foundation");
var traceDb = app.Services.GetRequiredService<TraceDb>();
var appliedMigrations = traceDb.Initialize();
if (appliedMigrations.Count > 0)
{
    foreach (var version in appliedMigrations)
    {
        foundationLogger.LogInformation("Migrasi fondasi diterapkan: {Version} (db: {Path})", version, traceDb.DatabasePath);
    }
}
else
{
    foundationLogger.LogInformation("Migrasi fondasi mutakhir (db: {Path})", traceDb.DatabasePath);
}

var accountService = app.Services.GetRequiredService<IAccountService>();
if (!accountService.HasAnyAccount())
{
    var dbOptions = app.Services.GetRequiredService<TraceDbOptions>();
    var provisioningReport = accountService.ProvisionBootstrapAdmin(dbOptions.BootstrapAdminEmail, dbOptions.BootstrapAdminPassword);
    foundationLogger.LogInformation("Provisioning admin awal:{NewLine}{Report}", Environment.NewLine, provisioningReport);
}

var traceStore = app.Services.GetRequiredService<ITraceStore>();
var masterProjection = app.Services.GetRequiredService<IMasterProjection>();
lock (traceStore.Gate)
{
    traceStore.State.Config.SnPattern = builder.Configuration["Trace:Ng:SerialPattern"] ?? string.Empty;
    masterProjection.Apply(traceStore.State.Config);
}

// --- Fondasi Fase 2: sequence penerbitan label harus di atas batas tertinggi yang pernah keluar ---
var labelService = app.Services.GetRequiredService<ILabelStockService>();
var labelSequence = labelService.GetSequenceState();
foundationLogger.LogInformation(
    "Sequence label {Name}: next_value {Next}, tertinggi diketahui {HighWater}, ledger {Ledger}",
    labelSequence.Name, labelSequence.NextValue, labelSequence.HighWaterSeq, labelSequence.LedgerPath);
if (!labelSequence.Consistent)
{
    foundationLogger.LogWarning(
        "Sequence label tertinggal dari batas tertinggi yang pernah diterbitkan ({Next} vs {HighWater}). " +
        "Generate ditolak sampai POST /api/v1/labels/sequence/reconcile dijalankan.",
        labelSequence.NextValue, labelSequence.HighWaterSeq);
}

if (!string.IsNullOrWhiteSpace(internalKey))
{
    app.Use(async (context, next) =>
    {
        if (!context.Request.Path.StartsWithSegments("/api") || context.Request.Path == "/api/v1/health")
        {
            await next();
            return;
        }

        var provided = context.Request.Headers["X-Trace-Internal-Key"].ToString();
        if (!string.Equals(provided, internalKey, StringComparison.Ordinal))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Internal API key tidak valid." });
            return;
        }

        await next();
    });
}

if (allowedOrigins.Length > 0) app.UseCors();

if (swaggerEnabled)
{
    app.UseSwagger();
    app.UseSwaggerUI(options => options.SwaggerEndpoint("./v1/swagger.json", "LELCO Traceability API v1"));
}

app.MapGet("/", () => Results.Ok(new
{
    name = "LELCO Traceability API",
    version = "1.0.0",
    dataSource = "sqlite-foundation + in-memory-engine",
    db = traceDb.DatabasePath,
    authEnforced = traceDb.Options.AuthEnforced,
}));

app.MapControllers();
app.Run();

public partial class Program
{
}
