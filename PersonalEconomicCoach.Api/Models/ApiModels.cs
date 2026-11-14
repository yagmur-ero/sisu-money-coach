using System.ComponentModel.DataAnnotations;

namespace PersonalEconomicCoach.Api.Models;

public sealed record ErrorResponse(string Error);

public sealed class AskRequest
{
    [Range(1, 5)]
    public int? QuestionId { get; init; }

    [Required, StringLength(2000, MinimumLength = 1)]
    public string Text { get; init; } = string.Empty;

    [Required]
    [RegularExpression("^(gentle|honest|roast)$", ErrorMessage = "tone must be gentle, honest, or roast.")]
    public string Tone { get; init; } = "gentle";

    public WhatIfRequest? WhatIf { get; init; }

    // Null starts a new conversation. A supplied ID continues a conversation owned by this customer.
    public long? ConversationId { get; init; }
}

public sealed class WhatIfRequest
{
    [Range(typeof(decimal), "0", "100")]
    public decimal RestaurantsPct { get; init; }

    [Range(typeof(decimal), "0", "1000000")]
    public decimal ShoppingEur { get; init; }

    [Range(typeof(decimal), "0", "1000000")]
    public decimal ExtraSavingsEur { get; init; }
}

public sealed record AskResponse(string Type, string Answer, object Data, long ConversationId);

public sealed class CreateEventRequest
{
    [Required, StringLength(120, MinimumLength = 1)]
    public string Name { get; init; } = string.Empty;

    [Required]
    public string Date { get; init; } = string.Empty;

    [Range(typeof(decimal), "0", "1000000")]
    public decimal Amount { get; init; }
}

public sealed record PlannedEventResponse(long Id, string Name, string Date, decimal Amount);

public sealed record PlanResponse(
    decimal IrregularMonthlyEstimate,
    decimal? IrregularMonthlyAdjusted,
    decimal SafetyBuffer,
    decimal SavingsGoal,
    string? Payday);

public sealed class SaveBasketOverrideRequest
{
    [Required]
    [RegularExpression("^(merchant|category)$", ErrorMessage = "match_type must be merchant or category.")]
    public string MatchType { get; init; } = string.Empty;

    [Required, StringLength(120, MinimumLength = 1)]
    public string MatchValue { get; init; } = string.Empty;

    [Required]
    [RegularExpression("^(regular|irregular|leaks)$", ErrorMessage = "basket must be regular, irregular, or leaks.")]
    public string Basket { get; init; } = string.Empty;
}

public sealed record BasketOverrideResponse(string MatchType, string MatchValue, string Basket);

public sealed record AccountResponse(string Id, string Bank, string Name, decimal Balance, string? BalanceDate);

public sealed record TransactionResponse(
    long Id,
    string AccountId,
    string Date,
    decimal Amount,
    string Merchant,
    string Category);

public sealed record BasketTotals(decimal Regular, decimal Irregular, decimal Leaks);
public sealed record IrregularEstimate(decimal AveragePerMonth, decimal? Adjusted);

public sealed record BasketsInsight(
    string PeriodStart,
    string PeriodEnd,
    decimal Income,
    decimal Expenses,
    BasketTotals Baskets,
    IrregularEstimate IrregularEstimate);

public sealed record PeriodSpending(
    string PeriodStart,
    string PeriodEnd,
    decimal Income,
    decimal Expenses);

public sealed record ComparisonInsight(
    PeriodSpending Current,
    PeriodSpending Previous,
    decimal ExpenseChange,
    decimal ExpenseChangePercent);

public sealed record LeakItem(
    string Merchant,
    string Category,
    int TransactionCount,
    decimal TotalAmount,
    decimal AverageTransaction);

public sealed record LeaksInsight(
    string PeriodStart,
    string PeriodEnd,
    decimal Total,
    IReadOnlyList<LeakItem> Items);

public sealed record MonthEndProjection(
    decimal ExpectedMonthlyIncome,
    int IncomeHistoryMonths,
    decimal IncomeReceivedThisMonth,
    decimal EstimatedIncomeRemaining,
    decimal AverageMonthlyExpenses,
    decimal ExpensesPaidThisMonth,
    decimal EstimatedExpensesRemaining,
    decimal PlannedEventsRemaining,
    decimal? ProjectedBalance);

public sealed record SafeToSpendInsight(
    decimal? CurrentBalance,
    string? BalanceAsOf,
    decimal UpcomingRegularExpenses,
    decimal PlannedEvents,
    decimal IrregularReserve,
    decimal SafetyBuffer,
    decimal SavingsGoal,
    decimal? SafeToSpendNow,
    string PlanningUntil,
    MonthEndProjection MonthEndProjection,
    IReadOnlyList<string> Advice);

public sealed record WhatIfInsight(decimal Monthly, decimal In6Months, decimal In12Months);

public sealed record UncertainSetupItem(
    string MatchType,
    string MatchValue,
    string Category,
    int TransactionCount,
    decimal TotalAmount,
    string SuggestedBasket,
    string Reason);

public sealed record ConversationSummaryResponse(
    long Id,
    string Title,
    string CreatedAt,
    string UpdatedAt,
    int MessageCount);

public sealed record ConversationMessageResponse(
    long Id,
    string Role,
    string Content,
    string? InsightType,
    string? DataJson,
    string CreatedAt);
