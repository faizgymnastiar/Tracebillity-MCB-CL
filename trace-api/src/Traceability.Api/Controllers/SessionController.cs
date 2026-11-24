using Microsoft.AspNetCore.Mvc;
using Traceability.Application;
using Traceability.Domain;

namespace Traceability.Api.Controllers;

/// <summary>
/// Konteks default warisan untuk mode demo. Saat enforcement aktif, konteks
/// transaksi tidak bisa lagi ditimpa dari klien — ia berasal dari sesi produksi
/// (endpoint /production) sehingga penulisan ke endpoint ini ditolak.
/// </summary>
[Route("api/v1/session")]
public sealed class SessionController : BaseApiController
{
    public SessionController(IConfigService config, IAccountService accounts,
        Infrastructure.Foundation.TraceDbOptions options) : base(config, accounts, options)
    {
    }

    [HttpGet("context")]
    public ActionResult<OperatorContext> Get()
    {
        if (!Enforced) return Ok(Config.GetDefaultContext());

        var caller = RequireLoggedIn();
        return Ok(caller.Production is not null
            ? ContextFromProduction(caller.Production, Config.GetDefaultContext().MaterialLot)
            : Config.GetDefaultContext());
    }

    [HttpPut("context")]
    public async Task<ActionResult<OperatorContext>> Put()
    {
        if (Enforced)
        {
            RequireLoggedIn();
            return BadRequest(new
            {
                error = "Konteks produksi diatur lewat sesi produksi (Mulai produksi), bukan dari klien.",
                code = "CONTEXT_MANAGED_BY_SESSION",
            });
        }

        var body = await ReadJsonObjectAsync();
        var current = Config.GetDefaultContext();
        var next = new OperatorContext
        {
            Operator = StringValue(body, "operator") ?? current.Operator,
            WorkOrder = StringValue(body, "workOrder") ?? current.WorkOrder,
            MaterialLot = StringValue(body, "materialLot") ?? current.MaterialLot,
            Line = StringValue(body, "line") ?? current.Line,
            Shift = StringValue(body, "shift") ?? current.Shift,
            Sku = StringValue(body, "sku") ?? current.Sku,
            Destination = StringValue(body, "destination") ?? current.Destination,
        };

        return Ok(Config.SetDefaultContext(next));
    }
}
