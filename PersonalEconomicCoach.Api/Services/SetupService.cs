using Dapper;
using PersonalEconomicCoach.Api.Data;
using PersonalEconomicCoach.Api.Models;

namespace PersonalEconomicCoach.Api.Services;

public interface ISetupService
{
    Task<IReadOnlyList<UncertainSetupItem>> GetUncertainAsync(CancellationToken cancellationToken);
}

public sealed class SetupService(
    IDbConnectionFactory connectionFactory,
    ICustomerContext customerContext,
    IDateProvider dateProvider,
    IConfiguration configuration) : ISetupService
{
    public async Task<IReadOnlyList<UncertainSetupItem>> GetUncertainAsync(CancellationToken cancellationToken)
    {
        var today = dateProvider.Today;
        var ninetyDaysAgo = today.AddDays(-90);
        var currentMonthStart = new DateOnly(today.Year, today.Month, 1);
        var leakMinimum = configuration.GetValue<int>("App:LeakMinMonthlyTransactions", 3);
        var leakMaximum = configuration.GetValue<decimal>("App:LeakMaxAverageTransaction", 20m);

        const string sql = @"
WITH source_transactions AS (
    SELECT
        customer_id,
        tx_date,
        category,
        merchant_id,
        ABS(tx_amount_eur) AS expense_amount,
        CASE
            WHEN LOWER(COALESCE(transaction_type, '')) LIKE '%transfer%' THEN 'TRANSFER'
            WHEN LOWER(COALESCE(transaction_type, '')) LIKE '%refund%' THEN 'REFUND'
            WHEN LOWER(COALESCE(transaction_type, '')) LIKE '%cashback%' THEN 'REFUND'
            WHEN tx_amount_eur < 0 THEN 'EXPENSE'
            WHEN tx_amount_eur > 0 THEN 'INCOME'
            ELSE 'UNKNOWN'
        END AS direction
    FROM transactions
    WHERE customer_id = @CustomerId
), merchant_patterns AS (
    SELECT
        t.merchant_id,
        COALESCE(MAX(t.category), 'Uncategorized') AS category,
        COUNT(*) AS transaction_count,
        SUM(t.expense_amount) AS total_amount,
        AVG(t.expense_amount) AS average_amount,
        COUNT(DISTINCT DATE_FORMAT(t.tx_date, '%Y-%m')) AS active_months,
        SUM(CASE WHEN t.tx_date >= @CurrentMonthStart THEN 1 ELSE 0 END) AS current_month_count,
        AVG(CASE WHEN t.tx_date >= @CurrentMonthStart THEN t.expense_amount ELSE NULL END) AS current_month_average
    FROM source_transactions t
    WHERE t.direction = 'EXPENSE'
      AND t.tx_date >= @NinetyDaysAgo
      AND t.tx_date <= @Today
      AND t.merchant_id IS NOT NULL
      AND t.merchant_id <> ''
      AND NOT EXISTS (
          SELECT 1 FROM basket_overrides o
          WHERE o.customer_id = t.customer_id
            AND ((o.match_type = 'merchant' AND LOWER(o.match_value) = LOWER(t.merchant_id))
              OR (o.match_type = 'category' AND LOWER(o.match_value) = LOWER(COALESCE(t.category, ''))))
      )
    GROUP BY t.merchant_id
    HAVING COUNT(*) >= 2
)
SELECT
    'merchant' AS MatchType,
    merchant_id AS MatchValue,
    category AS Category,
    transaction_count AS TransactionCount,
    total_amount AS TotalAmount,
    CASE
        WHEN current_month_count >= @LeakMinimum
             AND COALESCE(current_month_average, 999999) <= @LeakMaximum THEN 'leaks'
        WHEN active_months >= 2 THEN 'regular'
        ELSE 'irregular'
    END AS SuggestedBasket,
    CASE
        WHEN current_month_count >= @LeakMinimum
             AND COALESCE(current_month_average, 999999) <= @LeakMaximum THEN 'Several small purchases from the same merchant this month'
        WHEN active_months >= 2 THEN 'Repeated merchant activity across multiple months'
        ELSE 'Occasional merchant activity; review the suggested basket'
    END AS Reason
FROM merchant_patterns
ORDER BY transaction_count DESC, total_amount DESC
LIMIT 20;";

        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<UncertainSetupItem>(new CommandDefinition(sql, new
        {
            customerContext.CustomerId,
            Today = today.ToDateTime(TimeOnly.MinValue),
            CurrentMonthStart = currentMonthStart.ToDateTime(TimeOnly.MinValue),
            NinetyDaysAgo = ninetyDaysAgo.ToDateTime(TimeOnly.MinValue),
            LeakMinimum = leakMinimum,
            LeakMaximum = leakMaximum
        }, cancellationToken: cancellationToken));
        return rows.ToList();
    }
}
