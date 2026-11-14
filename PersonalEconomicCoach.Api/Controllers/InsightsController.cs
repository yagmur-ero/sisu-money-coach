using Microsoft.AspNetCore.Mvc;
using PersonalEconomicCoach.Api.Models;
using PersonalEconomicCoach.Api.Services;

namespace PersonalEconomicCoach.Api.Controllers;

[ApiController]
[Route("insights")]
public sealed class InsightsController(IInsightService insightService) : ControllerBase
{
    [HttpGet("baskets")]
    public async Task<ActionResult<BasketsInsight>> Baskets(CancellationToken cancellationToken) =>
        Ok(await insightService.GetBasketsAsync(cancellationToken));

    [HttpGet("comparison")]
    public async Task<ActionResult<ComparisonInsight>> Comparison(CancellationToken cancellationToken) =>
        Ok(await insightService.GetComparisonAsync(cancellationToken));

    [HttpGet("leaks")]
    public async Task<ActionResult<LeaksInsight>> Leaks(CancellationToken cancellationToken) =>
        Ok(await insightService.GetLeaksAsync(cancellationToken));

    [HttpGet("safe-to-spend")]
    public async Task<ActionResult<SafeToSpendInsight>> SafeToSpend(CancellationToken cancellationToken) =>
        Ok(await insightService.GetSafeToSpendAsync(cancellationToken));

    [HttpPost("what-if")]
    public async Task<ActionResult<WhatIfInsight>> WhatIf(
        [FromBody] WhatIfRequest request,
        CancellationToken cancellationToken) =>
        Ok(await insightService.GetWhatIfAsync(request, cancellationToken));
}
