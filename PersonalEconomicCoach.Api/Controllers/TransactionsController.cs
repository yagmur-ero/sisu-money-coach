using Microsoft.AspNetCore.Mvc;
using PersonalEconomicCoach.Api.Models;
using PersonalEconomicCoach.Api.Services;

namespace PersonalEconomicCoach.Api.Controllers;

[ApiController]
[Route("transactions")]
public sealed class TransactionsController(ITransactionService transactionService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<TransactionResponse>>> Get(
        [FromQuery] string? from,
        [FromQuery] string? to,
        [FromQuery(Name = "account_id")] string? accountId,
        [FromQuery] string? category,
        CancellationToken cancellationToken) =>
        Ok(await transactionService.GetAsync(from, to, accountId, category, cancellationToken));
}
