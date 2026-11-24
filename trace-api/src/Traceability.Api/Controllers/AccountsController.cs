using Microsoft.AspNetCore.Mvc;
using Traceability.Application;
using Traceability.Domain;

namespace Traceability.Api.Controllers;

/// <summary>Manajemen akun &amp; hak operasi — hanya untuk pemegang admin.accounts.</summary>
[Route("api/v1/accounts")]
public sealed class AccountsController : BaseApiController
{
    public AccountsController(IConfigService config, IAccountService accounts,
        Infrastructure.Foundation.TraceDbOptions options) : base(config, accounts, options)
    {
    }

    [HttpGet("permissions")]
    public IActionResult Permissions()
    {
        RequireLoggedIn();
        return Ok(PermissionCatalog.All.Select(key => new
        {
            key,
            label = PermissionCatalog.Labels[key],
        }));
    }

    [HttpGet]
    public IActionResult List()
    {
        RequireCaller(PermissionCatalog.AdminAccounts);
        return Ok(Accounts.ListAccounts());
    }

    [HttpPost]
    public IActionResult Create([FromBody] CreateAccountInput input)
    {
        RequireCaller(PermissionCatalog.AdminAccounts);
        return StatusCode(StatusCodes.Status201Created, Accounts.CreateAccount(input));
    }

    [HttpPut]
    public IActionResult Update([FromBody] UpdateAccountInput input)
    {
        RequireCaller(PermissionCatalog.AdminAccounts);
        return Ok(Accounts.UpdateAccount(input));
    }
}
