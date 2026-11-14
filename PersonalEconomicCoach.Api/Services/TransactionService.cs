using Dapper;
using PersonalEconomicCoach.Api.Data;
using PersonalEconomicCoach.Api.Models;

namespace PersonalEconomicCoach.Api.Services;

public interface ITransactionService
{
    Task<IReadOnlyList<TransactionResponse>> GetAsync(
        string? from, string? to, string? accountId, string? category,
        CancellationToken cancellationToken);
}

/// <summary>Reads the existing processed S-Pankki transactions table without modifying it.</summary>
public sealed class TransactionService(
    IDbConnectionFactory connectionFactory,
    ICustomerContext customerContext) : ITransactionService
{
    public async Task<IReadOnlyList<TransactionResponse>> GetAsync(
        string? from, string? to, string? accountId, string? category,
        CancellationToken cancellationToken)
    {
        var fromDate = ParseDate(from, "from");
        var toDate = ParseDate(to, "to");
        if (fromDate.HasValue && toDate.HasValue && fromDate.Value > toDate.Value)
            throw new ApiException(400, "from must be on or before to.");

        const string sql = @"
SELECT
    id AS Id,
    CAST(bank_account_id AS CHAR) AS AccountId,
    DATE_FORMAT(tx_date, '%Y-%m-%d') AS Date,
    tx_amount_eur AS Amount,
    COALESCE(NULLIF(merchant_id, ''), NULLIF(category, ''), 'Unknown') AS Merchant,
    COALESCE(category, 'Uncategorized') AS Category
FROM transactions
WHERE customer_id = @CustomerId
  AND (@FromDate IS NULL OR tx_date >= @FromDate)
  AND (@ToDate IS NULL OR tx_date <= @ToDate)
  AND (@AccountId IS NULL OR CAST(bank_account_id AS CHAR) = @AccountId)
  AND (@Category IS NULL OR LOWER(category) = LOWER(@Category))
ORDER BY tx_date DESC, tx_timestamp DESC, id DESC
LIMIT 5000;";

        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<TransactionResponse>(new CommandDefinition(sql, new
        {
            CustomerId = customerContext.CustomerId,
            FromDate = fromDate,
            ToDate = toDate,
            AccountId = string.IsNullOrWhiteSpace(accountId) ? null : accountId.Trim(),
            Category = string.IsNullOrWhiteSpace(category) ? null : category.Trim()
        }, cancellationToken: cancellationToken));

        return rows.ToList();
    }

    private static DateTime? ParseDate(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (!DateOnly.TryParseExact(value, "yyyy-MM-dd", out var date))
            throw new ApiException(400, $"{field} must use yyyy-MM-dd format.");
        return date.ToDateTime(TimeOnly.MinValue);
    }
}
