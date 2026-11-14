using Dapper;
using MySqlConnector;
using PersonalEconomicCoach.Api.Data;
using PersonalEconomicCoach.Api.Models;
using PersonalEconomicCoach.Api.Middleware;

namespace PersonalEconomicCoach.Api.Services;

public interface IInsightService
{
    Task<BasketsInsight> GetBasketsAsync(CancellationToken cancellationToken);
    Task<ComparisonInsight> GetComparisonAsync(CancellationToken cancellationToken);
    Task<LeaksInsight> GetLeaksAsync(CancellationToken cancellationToken);
    Task<SafeToSpendInsight> GetSafeToSpendAsync(CancellationToken cancellationToken);
    Task<WhatIfInsight> GetWhatIfAsync(WhatIfRequest request, CancellationToken cancellationToken);
    Task<decimal> GetIrregularAverageAsync(CancellationToken cancellationToken);
}

/// <summary>
/// All monetary calculations are deterministic SQL/application logic.
/// The existing spankki.transactions table is read directly and is never modified.
/// </summary>
public sealed class InsightService(
    IDbConnectionFactory connectionFactory,
    ICustomerContext customerContext,
    IDateProvider dateProvider,
    IConfiguration configuration) : IInsightService
{
    private sealed class BasketAggregateRow
    {
        public decimal Income { get; set; }
        public decimal Expenses { get; set; }
        public decimal Regular { get; set; }
        public decimal Irregular { get; set; }
        public decimal Leaks { get; set; }
        public decimal IrregularTwelveMonthTotal { get; set; }
    }

    private sealed class ComparisonRow
    {
        public decimal CurrentIncome { get; set; }
        public decimal CurrentExpenses { get; set; }
        public decimal PreviousIncome { get; set; }
        public decimal PreviousExpenses { get; set; }
    }

    private sealed class PlanDbRow
    {
        public decimal? IrregularMonthlyAdjusted { get; set; }
        public decimal SafetyBuffer { get; set; }
        public decimal SavingsGoal { get; set; }
        public DateTime? Payday { get; set; }
    }

    private sealed class BalanceRow
    {
        public decimal? Balance { get; set; }
        public DateTime? BalanceDate { get; set; }
    }

    private sealed class EventTotalsRow
    {
        public decimal ThroughPlanningHorizon { get; set; }
        public decimal ThroughMonthEnd { get; set; }
    }

    private sealed class HistoricalAveragesRow
    {
        public DateTime? FirstHistoricalTransactionDate { get; set; }
        public decimal IncomeMonth3 { get; set; }
        public decimal IncomeMonth2 { get; set; }
        public decimal IncomeMonth1 { get; set; }
        public decimal ExpenseMonth3 { get; set; }
        public decimal ExpenseMonth2 { get; set; }
        public decimal ExpenseMonth1 { get; set; }
        public decimal IncomeReceivedThisMonth { get; set; }
        public decimal ExpensesPaidThisMonth { get; set; }
    }

    private sealed class WhatIfRow
    {
        public decimal RestaurantSpend { get; set; }
    }

    public async Task<BasketsInsight> GetBasketsAsync(CancellationToken cancellationToken)
    {
        var today = dateProvider.Today;
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var historyStart = monthStart.AddMonths(-12);
        var parameters = BuildClassificationParameters(today, monthStart, historyStart);
        parameters.Add("PeriodStart", monthStart.ToDateTime(TimeOnly.MinValue));
        parameters.Add("PeriodEndExclusive", today.AddDays(1).ToDateTime(TimeOnly.MinValue));

        const string sql = FinancialSql.ClassifiedExpensesCte + @"
SELECT
    COALESCE((SELECT SUM(amount_eur) FROM source_transactions
              WHERE direction = 'INCOME'
                AND tx_date >= @PeriodStart AND tx_date < @PeriodEndExclusive), 0) AS Income,
    COALESCE((SELECT SUM(ABS(amount_eur)) FROM source_transactions
              WHERE direction = 'EXPENSE'
                AND tx_date >= @PeriodStart AND tx_date < @PeriodEndExclusive), 0) AS Expenses,
    COALESCE(SUM(CASE WHEN basket = 'regular' AND tx_date >= @PeriodStart AND tx_date < @PeriodEndExclusive THEN expense_amount ELSE 0 END), 0) AS Regular,
    COALESCE(SUM(CASE WHEN basket = 'irregular' AND tx_date >= @PeriodStart AND tx_date < @PeriodEndExclusive THEN expense_amount ELSE 0 END), 0) AS Irregular,
    COALESCE(SUM(CASE WHEN basket = 'leaks' AND tx_date >= @PeriodStart AND tx_date < @PeriodEndExclusive THEN expense_amount ELSE 0 END), 0) AS Leaks,
    COALESCE(SUM(CASE WHEN basket = 'irregular'
                           AND tx_date >= @HistoryStart AND tx_date < @CurrentMonthStart
                      THEN expense_amount ELSE 0 END), 0) AS IrregularTwelveMonthTotal
FROM classified;";

        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleAsync<BasketAggregateRow>(new CommandDefinition(
            sql, parameters, cancellationToken: cancellationToken));
        var adjusted = await GetAdjustedIrregularAsync(connection, cancellationToken);

        return new BasketsInsight(
            monthStart.ToString("yyyy-MM-dd"),
            today.ToString("yyyy-MM-dd"),
            Money(row.Income),
            Money(row.Expenses),
            new BasketTotals(Money(row.Regular), Money(row.Irregular), Money(row.Leaks)),
            new IrregularEstimate(Money(row.IrregularTwelveMonthTotal / 12m), adjusted));
    }

    public async Task<ComparisonInsight> GetComparisonAsync(CancellationToken cancellationToken)
    {
        var today = dateProvider.Today;
        var currentStart = new DateOnly(today.Year, today.Month, 1);
        var previousStart = currentStart.AddMonths(-1);
        var comparableDays = Math.Min(today.Day, DateTime.DaysInMonth(previousStart.Year, previousStart.Month));
        var previousEndExclusive = previousStart.AddDays(comparableDays);
        var currentEndExclusive = today.AddDays(1);

        const string sql = FinancialSql.SourceTransactionsCte + @"
SELECT
    COALESCE(SUM(CASE WHEN tx_date >= @CurrentStart AND tx_date < @CurrentEndExclusive AND direction = 'INCOME' THEN amount_eur ELSE 0 END), 0) AS CurrentIncome,
    COALESCE(SUM(CASE WHEN tx_date >= @CurrentStart AND tx_date < @CurrentEndExclusive AND direction = 'EXPENSE' THEN ABS(amount_eur) ELSE 0 END), 0) AS CurrentExpenses,
    COALESCE(SUM(CASE WHEN tx_date >= @PreviousStart AND tx_date < @PreviousEndExclusive AND direction = 'INCOME' THEN amount_eur ELSE 0 END), 0) AS PreviousIncome,
    COALESCE(SUM(CASE WHEN tx_date >= @PreviousStart AND tx_date < @PreviousEndExclusive AND direction = 'EXPENSE' THEN ABS(amount_eur) ELSE 0 END), 0) AS PreviousExpenses
FROM source_transactions
WHERE tx_date >= @PreviousStart AND tx_date < @CurrentEndExclusive
  AND direction IN ('INCOME', 'EXPENSE');";

        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleAsync<ComparisonRow>(new CommandDefinition(sql, new
        {
            customerContext.CustomerId,
            CurrentStart = currentStart.ToDateTime(TimeOnly.MinValue),
            CurrentEndExclusive = currentEndExclusive.ToDateTime(TimeOnly.MinValue),
            PreviousStart = previousStart.ToDateTime(TimeOnly.MinValue),
            PreviousEndExclusive = previousEndExclusive.ToDateTime(TimeOnly.MinValue)
        }, cancellationToken: cancellationToken));

        var change = row.CurrentExpenses - row.PreviousExpenses;
        var changePercent = row.PreviousExpenses == 0 ? 0 : change / row.PreviousExpenses * 100m;
        return new ComparisonInsight(
            new PeriodSpending(currentStart.ToString("yyyy-MM-dd"), today.ToString("yyyy-MM-dd"), Money(row.CurrentIncome), Money(row.CurrentExpenses)),
            new PeriodSpending(previousStart.ToString("yyyy-MM-dd"), previousEndExclusive.AddDays(-1).ToString("yyyy-MM-dd"), Money(row.PreviousIncome), Money(row.PreviousExpenses)),
            Money(change), Money(changePercent));
    }

    public async Task<LeaksInsight> GetLeaksAsync(CancellationToken cancellationToken)
    {
        var today = dateProvider.Today;
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var parameters = BuildClassificationParameters(today, monthStart, monthStart.AddMonths(-12));
        parameters.Add("PeriodStart", monthStart.ToDateTime(TimeOnly.MinValue));
        parameters.Add("PeriodEndExclusive", today.AddDays(1).ToDateTime(TimeOnly.MinValue));

        const string sql = FinancialSql.ClassifiedExpensesCte + @"
SELECT
    -- Grouped by category: small purchases are usually spread across many shops.
    CASE WHEN COUNT(DISTINCT merchant_id) = 1 THEN COALESCE(NULLIF(MAX(merchant_id), ''), 'Unknown merchant')
         ELSE CONCAT(COUNT(DISTINCT merchant_id), ' different places') END AS Merchant,
    COALESCE(NULLIF(category, ''), 'Uncategorized') AS Category,
    COUNT(*) AS TransactionCount,
    COALESCE(SUM(expense_amount), 0) AS TotalAmount,
    COALESCE(AVG(expense_amount), 0) AS AverageTransaction
FROM classified
WHERE basket = 'leaks'
  AND tx_date >= @PeriodStart AND tx_date < @PeriodEndExclusive
GROUP BY category
ORDER BY TotalAmount DESC
LIMIT 10;";

        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        var rows = (await connection.QueryAsync<LeakItem>(new CommandDefinition(
            sql, parameters, cancellationToken: cancellationToken))).ToList();

        return new LeaksInsight(
            monthStart.ToString("yyyy-MM-dd"),
            today.ToString("yyyy-MM-dd"),
            Money(rows.Sum(row => row.TotalAmount)),
            rows.Select(row => row with
            {
                TotalAmount = Money(row.TotalAmount),
                AverageTransaction = Money(row.AverageTransaction)
            }).ToList());
    }

    public async Task<SafeToSpendInsight> GetSafeToSpendAsync(CancellationToken cancellationToken)
    {
        var today = dateProvider.Today;
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var nextMonthStart = monthStart.AddMonths(1);

        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await EnsurePlanRowAsync(connection, cancellationToken);
        var plan = await ReadPlanAsync(connection, cancellationToken);

        // Latest balance per bank account. This reads transactions only; it creates no bank views.
        const string balanceSql = @"
WITH ranked_balances AS (
    SELECT
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
    SUM(balance_after_eur) AS Balance,
    MIN(tx_date) AS BalanceDate
FROM ranked_balances
WHERE row_number_in_account = 1;";

        var balanceRow = await connection.QuerySingleAsync<BalanceRow>(new CommandDefinition(
            balanceSql, new { customerContext.CustomerId }, cancellationToken: cancellationToken));
        var hasBalance = balanceRow.Balance.HasValue;
        var currentBalance = balanceRow.Balance is null ? (decimal?)null : Money(balanceRow.Balance.Value);

        var planningUntil = ResolveNextPayday(today, plan.Payday);
        var horizonDays = Math.Max(1, planningUntil.DayNumber - today.DayNumber + 1);

        const string eventSql = @"
SELECT
    COALESCE(SUM(CASE WHEN event_date >= @Today AND event_date <= @PlanningUntil THEN amount_eur ELSE 0 END), 0) AS ThroughPlanningHorizon,
    COALESCE(SUM(CASE WHEN event_date >= @Today AND event_date < @NextMonthStart THEN amount_eur ELSE 0 END), 0) AS ThroughMonthEnd
FROM planned_events
WHERE customer_id = @CustomerId;";
        var eventTotals = await connection.QuerySingleAsync<EventTotalsRow>(new CommandDefinition(
            eventSql, new
            {
                customerContext.CustomerId,
                Today = today.ToDateTime(TimeOnly.MinValue),
                PlanningUntil = planningUntil.ToDateTime(TimeOnly.MinValue),
                NextMonthStart = nextMonthStart.ToDateTime(TimeOnly.MinValue)
            }, cancellationToken: cancellationToken));

        var irregularAverage = await GetIrregularAverageAsync(connection, today, cancellationToken);
        var irregularMonthly = plan.IrregularMonthlyAdjusted ?? irregularAverage;
        var irregularReserve = Money(irregularMonthly * horizonDays / 30m);
        var regularDue = await GetUpcomingRegularExpensesAsync(connection, today, planningUntil, cancellationToken);
        var safetyBuffer = Money(plan.SafetyBuffer);
        var savingsGoalForHorizon = Money(plan.SavingsGoal * horizonDays / 30m);
        var eventsThroughHorizon = Money(eventTotals.ThroughPlanningHorizon);

        // This figure uses only money already represented by the latest recorded account balances.
        // Expected future salary is intentionally NOT added to safe-to-spend-now.
        decimal? safeToSpend = null;
        if (hasBalance)
        {
            safeToSpend = Money(currentBalance!.Value
                - regularDue
                - eventsThroughHorizon
                - irregularReserve
                - safetyBuffer
                - savingsGoalForHorizon);
        }

        var historical = await GetHistoricalAveragesAsync(connection, today, cancellationToken);
        var (incomeAverage, expenseAverage, historyMonths) = CalculateHistoricalAverages(historical, monthStart);
        var incomeRemaining = Money(Math.Max(0, incomeAverage - historical.IncomeReceivedThisMonth));
        var expensesRemaining = Money(Math.Max(0, expenseAverage - historical.ExpensesPaidThisMonth));
        decimal? projectedMonthEnd = null;
        if (hasBalance)
        {
            // This is a projected account balance, not safe-to-spend. Buffer and savings goal remain in the account.
            projectedMonthEnd = Money(currentBalance!.Value
                + incomeRemaining
                - expensesRemaining
                - eventTotals.ThroughMonthEnd);
        }

        var monthEndProjection = new MonthEndProjection(
            incomeAverage,
            historyMonths,
            Money(historical.IncomeReceivedThisMonth),
            incomeRemaining,
            expenseAverage,
            Money(historical.ExpensesPaidThisMonth),
            expensesRemaining,
            Money(eventTotals.ThroughMonthEnd),
            projectedMonthEnd);

        var advice = new List<string>();
        if (!hasBalance)
        {
            advice.Add("No account balance is available from the transaction history, so safe-to-spend cannot be calculated.");
        }
        else
        {
            var maxBalanceAgeDays = configuration.GetValue<int>("App:MaxBalanceAgeDays", 30);
            if (balanceRow.BalanceDate.HasValue
                && DateOnly.FromDateTime(balanceRow.BalanceDate.Value) < today.AddDays(-maxBalanceAgeDays))
            {
                advice.Add($"The latest recorded account balance is from {balanceRow.BalanceDate:yyyy-MM-dd}. Treat current-balance calculations as historical estimates, not live bank information.");
            }

            if (safeToSpend < 0)
                advice.Add($"Your planned commitments and reserves exceed the latest recorded balance by €{Math.Abs(safeToSpend.Value):0.00}. Review upcoming bills, planned events or reserves.");
            else if (safeToSpend == 0)
                advice.Add("Your current balance is fully allocated to commitments and reserves. There is no estimated flexible spending money in this planning period.");
        }

        if (historyMonths < 3)
            advice.Add($"The income and expense estimate uses only {historyMonths} complete month(s) of available history. Treat the month-end projection as preliminary.");

        return new SafeToSpendInsight(
            currentBalance,
            balanceRow.BalanceDate?.ToString("yyyy-MM-dd"),
            regularDue,
            eventsThroughHorizon,
            irregularReserve,
            safetyBuffer,
            savingsGoalForHorizon,
            safeToSpend,
            planningUntil.ToString("yyyy-MM-dd"),
            monthEndProjection,
            advice);
    }

    public async Task<WhatIfInsight> GetWhatIfAsync(WhatIfRequest request, CancellationToken cancellationToken)
    {
        var today = dateProvider.Today;
        var currentMonthStart = new DateOnly(today.Year, today.Month, 1);
        var historyStart = currentMonthStart.AddMonths(-3);

        const string sql = FinancialSql.SourceTransactionsCte + @"
SELECT COALESCE(SUM(ABS(amount_eur)), 0) / 3.0 AS RestaurantSpend
FROM source_transactions
WHERE direction = 'EXPENSE'
  AND tx_date >= @HistoryStart
  AND tx_date < @CurrentMonthStart
  AND (LOWER(COALESCE(category, '')) LIKE '%restaurant%'
       OR LOWER(COALESCE(category, '')) LIKE '%dining%'
       OR LOWER(COALESCE(category, '')) LIKE '%cafe%');";

        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleAsync<WhatIfRow>(new CommandDefinition(sql, new
        {
            customerContext.CustomerId,
            HistoryStart = historyStart.ToDateTime(TimeOnly.MinValue),
            CurrentMonthStart = currentMonthStart.ToDateTime(TimeOnly.MinValue)
        }, cancellationToken: cancellationToken));

        var restaurantSaving = row.RestaurantSpend * request.RestaurantsPct / 100m;
        var monthly = Money(restaurantSaving + request.ShoppingEur + request.ExtraSavingsEur);
        return new WhatIfInsight(monthly, Money(monthly * 6), Money(monthly * 12));
    }

    public async Task<decimal> GetIrregularAverageAsync(CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        return await GetIrregularAverageAsync(connection, dateProvider.Today, cancellationToken);
    }

    private async Task<decimal> GetIrregularAverageAsync(
        MySqlConnection connection,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var historyStart = monthStart.AddMonths(-12);
        var parameters = BuildClassificationParameters(today, monthStart, historyStart);
        const string sql = FinancialSql.ClassifiedExpensesCte + @"
SELECT COALESCE(SUM(expense_amount), 0) / 12.0
FROM classified
WHERE basket = 'irregular'
  AND tx_date >= @HistoryStart
  AND tx_date < @CurrentMonthStart;";

        var total = await connection.ExecuteScalarAsync<decimal>(new CommandDefinition(
            sql, parameters, cancellationToken: cancellationToken));
        return Money(total);
    }

    private async Task<decimal> GetUpcomingRegularExpensesAsync(
        MySqlConnection connection,
        DateOnly today,
        DateOnly horizonEnd,
        CancellationToken cancellationToken)
    {
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var nextMonthStart = monthStart.AddMonths(1);
        var historyStart = monthStart.AddMonths(-12);
        var parameters = BuildClassificationParameters(today, monthStart, historyStart);
        parameters.Add("Today", today.ToDateTime(TimeOnly.MinValue));
        parameters.Add("HorizonEnd", horizonEnd.ToDateTime(TimeOnly.MinValue));

        const string sql = FinancialSql.ClassifiedExpensesCte + @",
regular_patterns AS (
    SELECT
        merchant_id,
        AVG(expense_amount) AS average_amount,
        LEAST(31, GREATEST(1, ROUND(AVG(DAYOFMONTH(tx_date))))) AS due_day
    FROM classified
    WHERE basket = 'regular'
      AND merchant_id IS NOT NULL
      AND merchant_id <> ''
    GROUP BY merchant_id
    HAVING COUNT(DISTINCT DATE_FORMAT(tx_date, '%Y-%m')) >= 2
), candidate_months AS (
    SELECT @CurrentMonthStart AS month_start
    UNION ALL
    SELECT @NextMonthStart AS month_start
), expected_due AS (
    SELECT
        r.merchant_id,
        r.average_amount,
        m.month_start,
        DATE_ADD(m.month_start, INTERVAL (LEAST(DAY(LAST_DAY(m.month_start)), r.due_day) - 1) DAY) AS due_date
    FROM regular_patterns r
    CROSS JOIN candidate_months m
)
SELECT COALESCE(SUM(d.average_amount), 0)
FROM expected_due d
WHERE d.due_date >= @Today
  AND d.due_date <= @HorizonEnd
  AND NOT EXISTS (
      SELECT 1
      FROM source_transactions paid
      WHERE paid.merchant_id = d.merchant_id
        AND paid.direction = 'EXPENSE'
        AND paid.tx_date >= d.month_start
        AND paid.tx_date < DATE_ADD(d.month_start, INTERVAL 1 MONTH)
  );";

        var amount = await connection.ExecuteScalarAsync<decimal>(new CommandDefinition(
            sql, parameters, cancellationToken: cancellationToken));
        return Money(amount);
    }

    private async Task<HistoricalAveragesRow> GetHistoricalAveragesAsync(
        MySqlConnection connection,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var month1 = monthStart.AddMonths(-1);
        var month2 = monthStart.AddMonths(-2);
        var month3 = monthStart.AddMonths(-3);
        var tomorrow = today.AddDays(1);

        const string sql = FinancialSql.SourceTransactionsCte + @"
SELECT
    MIN(CASE WHEN tx_date >= @Month3 AND tx_date < @MonthStart THEN tx_date ELSE NULL END) AS FirstHistoricalTransactionDate,
    COALESCE(SUM(CASE WHEN tx_date >= @Month3 AND tx_date < @Month2 AND direction = 'INCOME' THEN amount_eur ELSE 0 END), 0) AS IncomeMonth3,
    COALESCE(SUM(CASE WHEN tx_date >= @Month2 AND tx_date < @Month1 AND direction = 'INCOME' THEN amount_eur ELSE 0 END), 0) AS IncomeMonth2,
    COALESCE(SUM(CASE WHEN tx_date >= @Month1 AND tx_date < @MonthStart AND direction = 'INCOME' THEN amount_eur ELSE 0 END), 0) AS IncomeMonth1,
    COALESCE(SUM(CASE WHEN tx_date >= @Month3 AND tx_date < @Month2 AND direction = 'EXPENSE' THEN ABS(amount_eur) ELSE 0 END), 0) AS ExpenseMonth3,
    COALESCE(SUM(CASE WHEN tx_date >= @Month2 AND tx_date < @Month1 AND direction = 'EXPENSE' THEN ABS(amount_eur) ELSE 0 END), 0) AS ExpenseMonth2,
    COALESCE(SUM(CASE WHEN tx_date >= @Month1 AND tx_date < @MonthStart AND direction = 'EXPENSE' THEN ABS(amount_eur) ELSE 0 END), 0) AS ExpenseMonth1,
    COALESCE(SUM(CASE WHEN tx_date >= @MonthStart AND tx_date < @Tomorrow AND direction = 'INCOME' THEN amount_eur ELSE 0 END), 0) AS IncomeReceivedThisMonth,
    COALESCE(SUM(CASE WHEN tx_date >= @MonthStart AND tx_date < @Tomorrow AND direction = 'EXPENSE' THEN ABS(amount_eur) ELSE 0 END), 0) AS ExpensesPaidThisMonth
FROM source_transactions
WHERE tx_date >= @Month3 AND tx_date < @Tomorrow;";

        return await connection.QuerySingleAsync<HistoricalAveragesRow>(new CommandDefinition(sql, new
        {
            customerContext.CustomerId,
            Month3 = month3.ToDateTime(TimeOnly.MinValue),
            Month2 = month2.ToDateTime(TimeOnly.MinValue),
            Month1 = month1.ToDateTime(TimeOnly.MinValue),
            MonthStart = monthStart.ToDateTime(TimeOnly.MinValue),
            Tomorrow = tomorrow.ToDateTime(TimeOnly.MinValue)
        }, cancellationToken: cancellationToken));
    }

    private static (decimal IncomeAverage, decimal ExpenseAverage, int Months) CalculateHistoricalAverages(
        HistoricalAveragesRow row,
        DateOnly currentMonthStart)
    {
        if (row.FirstHistoricalTransactionDate is null)
            return (0m, 0m, 0);

        var firstDate = DateOnly.FromDateTime(row.FirstHistoricalTransactionDate.Value);
        var month3Start = currentMonthStart.AddMonths(-3);
        var month2Start = currentMonthStart.AddMonths(-2);
        var month1Start = currentMonthStart.AddMonths(-1);

        // Use only fully covered previous calendar months. A partially covered oldest month is excluded.
        if (firstDate <= month3Start)
            return (Money((row.IncomeMonth3 + row.IncomeMonth2 + row.IncomeMonth1) / 3m),
                    Money((row.ExpenseMonth3 + row.ExpenseMonth2 + row.ExpenseMonth1) / 3m), 3);
        if (firstDate <= month2Start)
            return (Money((row.IncomeMonth2 + row.IncomeMonth1) / 2m),
                    Money((row.ExpenseMonth2 + row.ExpenseMonth1) / 2m), 2);
        if (firstDate <= month1Start)
            return (Money(row.IncomeMonth1), Money(row.ExpenseMonth1), 1);
        return (0m, 0m, 0);
    }

    private async Task<decimal?> GetAdjustedIrregularAsync(
        MySqlConnection connection,
        CancellationToken cancellationToken)
    {
        const string sql = "SELECT irregular_monthly_adjusted FROM user_plans WHERE customer_id = @CustomerId;";
        var value = await connection.QuerySingleOrDefaultAsync<decimal?>(new CommandDefinition(
            sql, new { customerContext.CustomerId }, cancellationToken: cancellationToken));
        return value is null ? null : Money(value.Value);
    }

    private async Task EnsurePlanRowAsync(MySqlConnection connection, CancellationToken cancellationToken)
    {
        const string sql = @"
INSERT IGNORE INTO user_plans (customer_id, safety_buffer_eur, savings_goal_eur)
VALUES (@CustomerId, @DefaultSafetyBuffer, @DefaultSavingsGoal);";
        await connection.ExecuteAsync(new CommandDefinition(sql, new
        {
            CustomerId = customerContext.CustomerId,
            DefaultSafetyBuffer = configuration.GetValue<decimal>("App:DefaultSafetyBuffer", 50m),
            DefaultSavingsGoal = configuration.GetValue<decimal>("App:DefaultSavingsGoal", 100m)
        }, cancellationToken: cancellationToken));
    }

    private async Task<PlanDbRow> ReadPlanAsync(MySqlConnection connection, CancellationToken cancellationToken)
    {
        const string sql = @"
SELECT irregular_monthly_adjusted AS IrregularMonthlyAdjusted,
       safety_buffer_eur AS SafetyBuffer,
       savings_goal_eur AS SavingsGoal,
       payday AS Payday
FROM user_plans
WHERE customer_id = @CustomerId;";
        return await connection.QuerySingleAsync<PlanDbRow>(
            new CommandDefinition(sql, new { customerContext.CustomerId }, cancellationToken: cancellationToken));
    }

    private DynamicParameters BuildClassificationParameters(
        DateOnly asOfDate,
        DateOnly currentMonthStart,
        DateOnly historyStart)
    {
        var parameters = new DynamicParameters();
        parameters.Add("CustomerId", customerContext.CustomerId);
        parameters.Add("AsOfDate", asOfDate.ToDateTime(TimeOnly.MinValue));
        parameters.Add("CurrentMonthStart", currentMonthStart.ToDateTime(TimeOnly.MinValue));
        parameters.Add("NextMonthStart", currentMonthStart.AddMonths(1).ToDateTime(TimeOnly.MinValue));
        parameters.Add("HistoryStart", historyStart.ToDateTime(TimeOnly.MinValue));
        parameters.Add("LeakMaxAverageTransaction", configuration.GetValue<decimal>("App:LeakMaxAverageTransaction", 20m));
        parameters.Add("LeakMinMonthlyTransactions", configuration.GetValue<int>("App:LeakMinMonthlyTransactions", 3));
        parameters.Add("RegularAmountTolerance", configuration.GetValue<decimal>("App:RegularAmountTolerance", 0.30m));
        return parameters;
    }

    private static DateOnly ResolveNextPayday(DateOnly today, DateTime? configuredPayday)
    {
        if (configuredPayday is null)
            return today.AddDays(30);

        var candidate = DateOnly.FromDateTime(configuredPayday.Value);
        while (candidate < today)
            candidate = candidate.AddMonths(1);
        return candidate;
    }

    private static decimal Money(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
