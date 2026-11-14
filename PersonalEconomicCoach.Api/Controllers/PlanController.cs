using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using PersonalEconomicCoach.Api.Models;
using PersonalEconomicCoach.Api.Services;

namespace PersonalEconomicCoach.Api.Controllers;

[ApiController]
[Route("plan")]
public sealed class PlanController(IPlanService planService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PlanResponse>> Get(CancellationToken cancellationToken) =>
        Ok(await planService.GetAsync(cancellationToken));

    [HttpPut]
    public async Task<ActionResult<PlanResponse>> Update([FromBody] JsonElement payload, CancellationToken cancellationToken) =>
        Ok(await planService.UpdateAsync(payload, cancellationToken));

    [HttpPut("overrides")]
    public async Task<ActionResult<BasketOverrideResponse>> SaveOverride(
        [FromBody] SaveBasketOverrideRequest request,
        CancellationToken cancellationToken) =>
        Ok(await planService.SaveOverrideAsync(request, cancellationToken));
}
