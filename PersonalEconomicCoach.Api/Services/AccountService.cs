using Dapper;
using PersonalEconomicCoach.Api.Data;
using PersonalEconomicCoach.Api.Models;

namespace PersonalEconomicCoach.Api.Services;

public interface IAccountService
{
    Task<IReadOnlyList<AccountResponse>> GetAsync(CancellationToken cancellationToken);
}

/// <summary>Derives account snapshots from the latest transaction in each account.</summary>
public sealed class AccountService(
    IDbConnectionFactory connectionFactory,
    ICustomerContext customerContext) : IAccountService
{
    public async Task<IReadOnlyList<AccountResponse>> GetAsync(CancellationToken cancellationToken)
    {
        const string sql = @"
WITH ranked_balances AS (
    SELECT
        bank_account_id,
        balance_after_eur,
        tx_date,
        tx_timestamp,
        id,
        ROW_NUMBER() OVER (
            PARTITION BY customer_id, bank_account_id
            ORDER BY tx_timestamp DESC, id DESC
        ) AS row_number_in_account
    FROM transactions
    WHERE customer_id = @CustomerId
      AND balance_after_eur IS NOT NULL
)
SELECT
    CAST(bank_account_id AS CHAR) AS Id,
    'S-Bank' AS Bank,
    CONCAT('Account ', RIGHT(CAST(bank_account_id AS CHAR), 4)) AS Name,
    balance_after_eur AS Balance,
    DATE_FORMAT(tx_date, '%Y-%m-%d') AS BalanceDate
FROM ranked_balances
WHERE row_number_in_account = 1
ORDER BY bank_account_id;";

        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<AccountResponse>(new CommandDefinition(
            sql, new { customerContext.CustomerId }, cancellationToken: cancellationToken));
        return rows.ToList();
    }
}
