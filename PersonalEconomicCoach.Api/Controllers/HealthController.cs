
using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using PersonalEconomicCoach.Api.Data;
using PersonalEconomicCoach.Api.Middleware;

namespace PersonalEconomicCoach.Api.Controllers;

[ApiController]
[Route("health")]
public sealed class HealthController(
    IDbConnectionFactory connectionFactory,
    ILogger<HealthController> logger) : ControllerBase
{
    // Liveness: confirms that the API process responds.
    [HttpGet]
    public IActionResult Get() =>
        Ok(new { status = "ok" });

    // Readiness: verifies MySQL and the existing banking table.
    [HttpGet("ready")]
    public async Task<IActionResult> Ready(
        CancellationToken cancellationToken)
    {
        try
        {
            await using var connection =
                await connectionFactory.OpenConnectionAsync(
                    cancellationToken);

            // Check that the existing S-Pankki transactions table exists.
            const string sql = @"
                SELECT COUNT(*)
                FROM information_schema.tables
                WHERE table_schema = DATABASE()
                  AND table_type = 'BASE TABLE'
                  AND table_name = 'transactions';";

            var tableExists =
                await connection.ExecuteScalarAsync<int>(
                    new CommandDefinition(
                        sql,
                        cancellationToken: cancellationToken));

            if (tableExists != 1)
            {
                throw new ApiException(
                    503,
                    "MySQL is reachable, but the existing transactions table was not found in spankki.");
            }

            // Read-only: verify access to existing banking data.
            var transactionCount =
                await connection.ExecuteScalarAsync<long>(
                    new CommandDefinition(
                        "SELECT COUNT(*) FROM transactions;",
                        cancellationToken: cancellationToken));

            return Ok(new
            {
                status = "ready",
                database = "spankki",
                banking_source = "transactions",
                transaction_count = transactionCount
            });
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "MySQL readiness check failed.");

            throw;
        }
    }
}
