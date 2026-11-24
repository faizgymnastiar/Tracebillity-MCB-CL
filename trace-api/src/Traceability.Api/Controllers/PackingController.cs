using Microsoft.AspNetCore.Mvc;
using Traceability.Application;
using Traceability.Domain;
using Traceability.Infrastructure.Foundation;

namespace Traceability.Api.Controllers;

[Route("api/v1/packing")]
public sealed class PackingController(IConfigService config, IAccountService accounts,
    TraceDbOptions options, PackingService packing) : BaseApiController(config, accounts, options)
{
    public sealed record BoxInput(string? Qr, string? Member);

    [HttpPost("{kind}/{actionName}")]
    public IActionResult UpdateBox(string kind, string actionName, BoxInput input)
    {
        if (kind is not ("inner" or "outer") || actionName is not ("open" or "add" or "close"))
            return NotFound();
        var caller = RequireCaller(kind == "inner" ? PermissionCatalog.ScanInner : PermissionCatalog.ScanOuter);
        AssertContextVersionCurrent(caller);
        return Ok(packing.Mutate(caller, NeedProduction(caller), kind.ToUpperInvariant(), actionName,
            input.Qr?.Trim() ?? "", input.Member?.Trim()));
    }

    [HttpGet("history")]
    public IActionResult History(string? search, string? status, string? from, string? to, int page = 1)
    {
        RequireCaller(PermissionCatalog.HistoryRead);
        return Ok(packing.Search(search, status, from, to, page));
    }

    [HttpGet("production-report")]
    public IActionResult ProductionReport(string? from, string? to, string? sku, string? wo)
    {
        RequireCaller(PermissionCatalog.HistoryRead);
        return Ok(packing.ProductionReport(from, to, sku, wo));
    }

    [HttpGet("history/unit")]
    public IActionResult Unit(string reference)
    {
        RequireCaller(PermissionCatalog.HistoryRead);
        return Ok(packing.Detail(reference.Trim()));
    }

    [HttpGet("work-order")]
    public IActionResult WorkOrder()
    {
        var caller = RequireCaller(PermissionCatalog.ScanInner, PermissionCatalog.ScanOuter, PermissionCatalog.HistoryRead);
        return Ok(packing.WorkOrder(NeedProduction(caller)));
    }

    private static ProductionContextDto NeedProduction(CallerPrincipal caller) =>
        caller.Production ?? throw FoundationErrors.Conflict("NO_PRODUCTION_SESSION", "Mulai produksi terlebih dahulu.");
}
