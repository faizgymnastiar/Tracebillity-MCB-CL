using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Traceability.Application;
using Traceability.Domain;

namespace Traceability.Api.Foundation;

/// <summary>
/// Memetakan kegagalan fondasi (FoundationException) ke kode HTTP seragam:
/// 401 sesi, 403 izin, 400 validasi, 404 tidak ditemukan, 409 konflik konteks.
/// </summary>
public sealed class FoundationExceptionFilter : IExceptionFilter
{
    private static readonly Dictionary<FoundationFailure, int> StatusMap = new()
    {
        [FoundationFailure.Unauthorized] = StatusCodes.Status401Unauthorized,
        [FoundationFailure.Forbidden] = StatusCodes.Status403Forbidden,
        [FoundationFailure.BadRequest] = StatusCodes.Status400BadRequest,
        [FoundationFailure.NotFound] = StatusCodes.Status404NotFound,
        [FoundationFailure.Conflict] = StatusCodes.Status409Conflict,
    };

    public void OnException(ExceptionContext context)
    {
        if (context.Exception is FoundationException failure)
        {
            context.Result = new ObjectResult(new
            {
                error = failure.Message,
                code = failure.Code,
            })
            {
                StatusCode = StatusMap.TryGetValue(failure.Failure, out var status) ? status : 500,
            };
            context.ExceptionHandled = true;
            return;
        }

        if (context.Exception is Microsoft.Data.Sqlite.SqliteException sqlite)
        {
            context.Result = new ObjectResult(new
            {
                error = "Database fondasi menolak operasi: " + sqlite.Message,
                code = "DB_ERROR",
            })
            {
                StatusCode = StatusCodes.Status500InternalServerError,
            };
            context.ExceptionHandled = true;
        }
    }
}

/// <summary>Pemetaan mode layar scan ke hak operasi terpisah (SSOT §3).</summary>
public static class ScanPermissions
{
    public static string[] ForMode(string mode) => mode.ToUpperInvariant() switch
    {
        "OK" => [PermissionCatalog.ScanUnit],
        "NG" => [PermissionCatalog.NgRecord],
        "REWORK" => [PermissionCatalog.NgRecord],
        _ => [PermissionCatalog.ScanUnit],
    };

    /// <summary>Hak yang boleh membuka layar scan (salah satu cukup).</summary>
    public static string[] AnyScanGate =>
    [
        PermissionCatalog.ScanUnit,
        PermissionCatalog.ScanInner,
        PermissionCatalog.ScanOuter,
        PermissionCatalog.NgRecord,
        PermissionCatalog.HistoryRead,
    ];
}
