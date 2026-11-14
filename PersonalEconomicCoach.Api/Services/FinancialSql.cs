namespace PersonalEconomicCoach.Api.Services;

/// <summary>
/// SQL fragments shared by the financial insight queries.
/// The S-Pankki source table is read directly. No banking table or view is created or modified.
/// Direction is derived from the signed tx_amount_eur value and transaction_type.
/// </summary>
internal static class FinancialSql
{
    public const string SourceTransactionsCte = @"
WITH source_transactions AS (
    SELECT
        t.id,
        t.customer_id,
        t.bank_account_id,
        t.tx_date,
        t.tx_timestamp,
        t.category,
        t.tx_amount_eur AS amount_eur,
        t.balance_after_eur,
        t.transaction_type,
        t.merchant_id,
        t.mcc_code,
        CASE
            WHEN LOWER(COALESCE(t.transaction_type, '')) LIKE '%transfer%' THEN 'TRANSFER'
            WHEN LOWER(COALESCE(t.transaction_type, '')) LIKE '%refund%' THEN 'REFUND'
            WHEN LOWER(COALESCE(t.transaction_type, '')) LIKE '%cashback%' THEN 'REFUND'
            WHEN t.tx_amount_eur < 0 THEN 'EXPENSE'
            WHEN t.tx_amount_eur > 0 THEN 'INCOME'
            ELSE 'UNKNOWN'
        END AS direction
    FROM transactions t
    WHERE t.customer_id = @CustomerId
)";

    public const string ClassifiedExpensesCte = @"
WITH source_transactions AS (
    SELECT
        t.id,
        t.customer_id,
        t.bank_account_id,
        t.tx_date,
        t.tx_timestamp,
        t.category,
        t.tx_amount_eur AS amount_eur,
        t.balance_after_eur,
        t.transaction_type,
        t.merchant_id,
        t.mcc_code,
        CASE
            WHEN LOWER(COALESCE(t.transaction_type, '')) LIKE '%transfer%' THEN 'TRANSFER'
            WHEN LOWER(COALESCE(t.transaction_type, '')) LIKE '%refund%' THEN 'REFUND'
            WHEN LOWER(COALESCE(t.transaction_type, '')) LIKE '%cashback%' THEN 'REFUND'
            WHEN t.tx_amount_eur < 0 THEN 'EXPENSE'
            WHEN t.tx_amount_eur > 0 THEN 'INCOME'
            ELSE 'UNKNOWN'
        END AS direction
    FROM transactions t
    WHERE t.customer_id = @CustomerId
), monthly_merchant_stats AS (
    SELECT
        customer_id,
        merchant_id,
        DATE_FORMAT(tx_date, '%Y-%m') AS month_key,
        COUNT(*) AS month_count,
        AVG(ABS(amount_eur)) AS monthly_average,
        MIN(ABS(amount_eur)) AS monthly_minimum,
        MAX(ABS(amount_eur)) AS monthly_maximum
    FROM source_transactions
    WHERE direction = 'EXPENSE'
      AND tx_date >= @HistoryStart
      AND tx_date <= @AsOfDate
      AND merchant_id IS NOT NULL
      AND merchant_id <> ''
    GROUP BY customer_id, merchant_id, DATE_FORMAT(tx_date, '%Y-%m')
), merchant_stats AS (
    SELECT
        customer_id,
        merchant_id,
        COUNT(*) AS active_months,
        AVG(monthly_average) AS average_amount,
        MIN(monthly_minimum) AS minimum_amount,
        MAX(monthly_maximum) AS maximum_amount
    FROM monthly_merchant_stats
    GROUP BY customer_id, merchant_id
), classified AS (
    SELECT
        t.id,
        t.customer_id,
        t.bank_account_id,
        t.tx_date,
        t.tx_timestamp,
        t.category,
        t.merchant_id,
        t.transaction_type,
        t.amount_eur,
        ABS(t.amount_eur) AS expense_amount,
        CASE
            WHEN merchant_override.basket IS NOT NULL THEN merchant_override.basket
            WHEN category_override.basket IS NOT NULL THEN category_override.basket
            WHEN COALESCE(month_stats.month_count, 0) >= @LeakMinMonthlyTransactions
                 AND COALESCE(month_stats.monthly_average, 999999) <= @LeakMaxAverageTransaction
                 THEN 'leaks'
            WHEN COALESCE(merchant_stats.active_months, 0) >= 2
                 AND (COALESCE(merchant_stats.maximum_amount, 0) - COALESCE(merchant_stats.minimum_amount, 0))
                     <= GREATEST(COALESCE(merchant_stats.average_amount, 0) * @RegularAmountTolerance, 2)
                 AND COALESCE(month_stats.month_count, 0) <= 2
                 THEN 'regular'
            ELSE 'irregular'
        END AS basket,
        CASE
            WHEN merchant_override.basket IS NOT NULL THEN 'USER_MERCHANT_OVERRIDE'
            WHEN category_override.basket IS NOT NULL THEN 'USER_CATEGORY_OVERRIDE'
            WHEN COALESCE(month_stats.month_count, 0) >= @LeakMinMonthlyTransactions
                 AND COALESCE(month_stats.monthly_average, 999999) <= @LeakMaxAverageTransaction
                 THEN 'FREQUENT_SMALL_PURCHASES'
            WHEN COALESCE(merchant_stats.active_months, 0) >= 2
                 AND (COALESCE(merchant_stats.maximum_amount, 0) - COALESCE(merchant_stats.minimum_amount, 0))
                     <= GREATEST(COALESCE(merchant_stats.average_amount, 0) * @RegularAmountTolerance, 2)
                 AND COALESCE(month_stats.month_count, 0) <= 2
                 THEN 'REPEATED_STABLE_MERCHANT_PAYMENT'
            ELSE 'NON_RECURRING_OR_NOT_ENOUGH_PATTERN_EVIDENCE'
        END AS classification_reason
    FROM source_transactions t
    LEFT JOIN monthly_merchant_stats month_stats
        ON month_stats.customer_id = t.customer_id
       AND month_stats.merchant_id = t.merchant_id
       AND month_stats.month_key = DATE_FORMAT(t.tx_date, '%Y-%m')
    LEFT JOIN merchant_stats
        ON merchant_stats.customer_id = t.customer_id
       AND merchant_stats.merchant_id = t.merchant_id
    LEFT JOIN basket_overrides merchant_override
        ON merchant_override.customer_id = t.customer_id
       AND merchant_override.match_type = 'merchant'
       AND LOWER(merchant_override.match_value) = LOWER(COALESCE(t.merchant_id, ''))
    LEFT JOIN basket_overrides category_override
        ON category_override.customer_id = t.customer_id
       AND category_override.match_type = 'category'
       AND LOWER(category_override.match_value) = LOWER(COALESCE(t.category, ''))
    WHERE t.direction = 'EXPENSE'
      AND t.tx_date >= @HistoryStart
      AND t.tx_date <= @AsOfDate
)";
}
