using Microsoft.AspNetCore.Mvc;
using Traceability.Application;

namespace Traceability.Api.Controllers;

[Route("api/v1/health")]
public sealed class HealthController : ControllerBase
{
    private readonly ITraceQueryService _queries;

    public HealthController(ITraceQueryService queries)
    {
        _queries = queries;
    }

    [HttpGet]
    public ActionResult<HealthStatus> Get()
    {
        return Ok(_queries.GetHealth());
    }
}
