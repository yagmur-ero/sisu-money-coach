using Microsoft.AspNetCore.Mvc;
using PersonalEconomicCoach.Api.Models;
using PersonalEconomicCoach.Api.Services;

namespace PersonalEconomicCoach.Api.Controllers;

[ApiController]
[Route("setup")]
public sealed class SetupController(ISetupService setupService) : ControllerBase
{
    [HttpGet("uncertain")]
    public async Task<ActionResult<IReadOnlyList<UncertainSetupItem>>> GetUncertain(CancellationToken cancellationToken) =>
        Ok(await setupService.GetUncertainAsync(cancellationToken));
}
