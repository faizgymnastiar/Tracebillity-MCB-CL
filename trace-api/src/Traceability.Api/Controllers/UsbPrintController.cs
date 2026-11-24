using Microsoft.AspNetCore.Mvc;
using Traceability.Application;
using Traceability.Domain;
using Traceability.Infrastructure.Foundation;

namespace Traceability.Api.Controllers;

[Route("api/v1/labels/print-jobs/{jobId}/usb")]
public sealed class UsbPrintController(IConfigService config, IAccountService accounts, TraceDbOptions options,
    UsbPrintService printer, IConfiguration configuration) : BaseApiController(config, accounts, options)
{
    public sealed record ReportInput(string? State);

    [HttpGet]
    public IActionResult Status(string jobId)
    {
        RequireCaller(PermissionCatalog.PrintManage, PermissionCatalog.HistoryRead);
        return Ok(printer.Status(jobId));
    }

    [HttpPost("claim")]
    public IActionResult Claim(string jobId)
    {
        var caller = RequireCaller(PermissionCatalog.PrintManage);
        if (!configuration.GetValue<bool>("Trace:UsbPrint:Enabled"))
            throw FoundationErrors.Conflict("USB_DISABLED", "Cetak USB belum diaktifkan. Gunakan NiceLabel.");
        return Ok(printer.Claim(jobId, caller.AccountId));
    }

    [HttpPost("report")]
    public IActionResult Report(string jobId, ReportInput input)
    {
        var caller = RequireCaller(PermissionCatalog.PrintManage);
        return Ok(printer.Report(jobId, caller.AccountId, input.State ?? ""));
    }
}
