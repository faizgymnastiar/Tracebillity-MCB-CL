using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Traceability.Api.Foundation;

public sealed class TraceOpenApiOperationFilter(bool internalKeyRequired) : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var path = context.ApiDescription.RelativePath;
        var method = context.ApiDescription.HttpMethod;
        if (path is null || !path.StartsWith("api/v1/", StringComparison.Ordinal) || path == "api/v1/health")
            return;

        var security = new OpenApiSecurityRequirement();
        if (internalKeyRequired) AddSecurity(security, "InternalKey");
        if (path is not ("api/v1/auth/login" or "api/v1/auth/verify-pin" or "api/v1/config/public")
            && !(path == "api/v1/config" && method == "GET"))
        {
            AddSecurity(security, "Session");
        }
        if (security.Count > 0) operation.Security.Add(security);

        if (path.StartsWith("api/v1/ng/records/", StringComparison.Ordinal)
            || path is "api/v1/packing/{kind}/{actionName}" or "api/v1/traceability/scans")
        {
            operation.Parameters.Add(new OpenApiParameter
            {
                Name = "X-Trace-Context-Version",
                In = ParameterLocation.Header,
                Description = "Wajib untuk transaksi produksi saat autentikasi diberlakukan. Gunakan contextVersion dari sesi produksi aktif.",
                Schema = new OpenApiSchema { Type = "integer", Format = "int32" }
            });
        }
    }

    private static void AddSecurity(OpenApiSecurityRequirement requirement, string id)
    {
        requirement.Add(new OpenApiSecurityScheme
        {
            Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = id }
        }, []);
    }
}
