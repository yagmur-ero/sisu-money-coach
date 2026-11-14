using Microsoft.AspNetCore.Mvc;
using PersonalEconomicCoach.Api.Models;
using PersonalEconomicCoach.Api.Services;

namespace PersonalEconomicCoach.Api.Controllers;

[ApiController]
[Route("accounts")]
public sealed class AccountsController(IAccountService accountService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AccountResponse>>> Get(CancellationToken cancellationToken) =>
        Ok(await accountService.GetAsync(cancellationToken));
}
