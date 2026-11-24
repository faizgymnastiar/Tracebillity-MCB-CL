using System.Text;
using Microsoft.AspNetCore.Mvc;
using Traceability.Api.Foundation;
using Traceability.Application;
using Traceability.Domain;

namespace Traceability.Api.Controllers;

[Route("api/v1/traceability")]
public sealed class TraceabilityController : BaseApiController
{
    private readonly ITraceService _service;
    private readonly ITraceQueryService _queries;

    public TraceabilityController(ITraceService service, ITraceQueryService queries,
        IConfigService config, IAccountService accounts,
        Infrastructure.Foundation.TraceDbOptions options) : base(config, accounts, options)
    {
        _service = service;
        _queries = queries;
    }

    [HttpGet("scan-state")]
    public ActionResult<ScanSnapshot> GetScanState()
    {
        var caller = RequireCaller(ScanPermissions.AnyScanGate);
        return Ok(Annotate(_service.GetScanState(ResolveViewStateContext(caller)), caller));
    }

    [HttpPost("scan-state")]
    public async Task<IActionResult> UpdateScanState()
    {
        var body = await ReadJsonObjectAsync();
        var action = StringValue(body, "action");

        if (action == "print")
        {
            var printer = RequireCaller(PermissionCatalog.PrintManage);
            return Ok(Annotate(_service.MarkLabelsPrinted(ResolveViewStateContext(printer)), printer));
        }

        var caller = RequireCaller(ScanPermissions.AnyScanGate);
        var context = ResolveTransactionContext(caller);

        if (action == "clear") return Ok(Annotate(_service.ClearActiveBatch(context), caller));

        var sn = StringValue(body, "sn");
        if (action == "removeItem" && !string.IsNullOrEmpty(sn))
        {
            return Ok(Annotate(_service.RemoveBatchItem(sn, context), caller));
        }

        return BadRequest(new { error = "Aksi scan-state tidak dikenal." });
    }

    [HttpDelete("scan-state")]
    public ActionResult<ScanSnapshot> ClearScanState()
    {
        var caller = RequireCaller(ScanPermissions.AnyScanGate);
        return Ok(Annotate(_service.ClearActiveBatch(ResolveTransactionContext(caller)), caller));
    }

    [HttpPost("scans")]
    public async Task<IActionResult> Scan()
    {
        var body = await ReadJsonObjectAsync();
        var mode = StringValue(body, "mode") ?? string.Empty;
        if (mode != "OK" && mode != "NG" && mode != "REWORK")
        {
            return BadRequest(new { error = "Mode scan tidak dikenal." });
        }

        // Hak operasi terpisah: mode OK butuh scan.unit; NG/rework butuh ng.record.
        var caller = RequireCaller(ScanPermissions.ForMode(mode));
        var context = ResolveTransactionContext(caller);

        var input = new ScanInput
        {
            Mode = mode,
            Sn = StringValue(body, "sn") ?? string.Empty,
            StationId = StringValue(body, "stationId"),
            Reason = StringValue(body, "reason"),
            Disposition = StringValue(body, "disposition"),
            Action = StringValue(body, "action"),
            Verdict = StringValue(body, "verdict") == "FAIL" ? "FAIL" : "PASS",
        };

        var result = _service.PerformScan(input, context);
        if (result.Snapshot is not null) Annotate(result.Snapshot, caller);
        return Ok(result);
    }

    [HttpGet("records")]
    public ActionResult<TraceRecordListResponse> Records(
        [FromQuery] string? search,
        [FromQuery] string? date,
        [FromQuery] string? status,
        [FromQuery] int? page,
        [FromQuery] int? pageSize)
    {
        var caller = RequireCaller(PermissionCatalog.HistoryRead);
        var query = new RecordQuery
        {
            Search = search,
            Date = date,
            Status = ParseStatus(status),
            Page = page ?? 1,
            PageSize = pageSize ?? 50,
        };

        return Ok(_queries.ListRecords(query, ResolveViewStateContext(caller)));
    }

    [HttpGet("records/{id}")]
    public IActionResult Record(string id)
    {
        var caller = RequireCaller(PermissionCatalog.HistoryRead);
        var detail = _queries.GetTraceDetail(id, ResolveViewStateContext(caller));
        if (detail is null)
        {
            return NotFound(new { error = $"Data traceability \"{id}\" tidak ditemukan." });
        }

        return Ok(detail);
    }

    [HttpGet("daily-summary")]
    public ActionResult<DailySummaryResponse> DailySummary()
    {
        var caller = RequireCaller(PermissionCatalog.HistoryRead);
        return Ok(_queries.GetDailySummary(ResolveViewStateContext(caller)));
    }

    [HttpGet("home-stats")]
    public ActionResult<HomeStats> HomeStats()
    {
        var caller = RequireCaller(PermissionCatalog.HistoryRead);
        return Ok(_queries.GetHomeStats(ResolveViewStateContext(caller)));
    }

    [HttpGet("bridge")]
    public IActionResult Bridge()
    {
        var caller = RequireCaller(PermissionCatalog.PrintManage);
        return Ok(_service.BuildLatestBridge(ResolveViewStateContext(caller)));
    }

    [HttpGet("export")]
    public IActionResult Export(
        [FromQuery] string? search,
        [FromQuery] string? date,
        [FromQuery] string? status,
        [FromQuery] string? columns)
    {
        var caller = RequireCaller(PermissionCatalog.HistoryRead);
        var requested = (columns ?? string.Empty)
            .Split(',')
            .Select(item => item.Trim())
            .Where(item => item.Length > 0)
            .ToList();

        var query = new RecordQuery
        {
            Search = search,
            Date = date,
            Status = ParseStatus(status),
        };

        var csv = _queries.BuildExport(query, requested, ResolveViewStateContext(caller));
        Response.Headers.ContentDisposition = $"attachment; filename=\"{csv.Filename}\"";
        Response.Headers.CacheControl = "no-store";
        return File(Encoding.UTF8.GetBytes(csv.Content), "text/csv; charset=utf-8");
    }

    /// <summary>Tempel kontrak fondasi pada snapshot agar UI menampilkan konteks &amp; hak yang benar.</summary>
    private ScanSnapshot Annotate(ScanSnapshot snapshot, CallerPrincipal caller)
    {
        snapshot.Production = caller.Production;
        snapshot.AuthEnforced = Enforced;
        snapshot.Permissions = caller.Permissions;
        return snapshot;
    }

    private static string ParseStatus(string? value)
    {
        var normalized = (value ?? "ALL").ToUpperInvariant();
        return normalized is "OK" or "NG" or "REWORK" ? normalized : "ALL";
    }
}
