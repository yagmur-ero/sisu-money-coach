using System.Text.Json;
using Dapper;
using PersonalEconomicCoach.Api.Data;
using PersonalEconomicCoach.Api.Models;

namespace PersonalEconomicCoach.Api.Services;

public interface IPlanService
{
    Task<PlanResponse> GetAsync(CancellationToken cancellationToken);
    Task<PlanResponse> UpdateAsync(JsonElement payload, CancellationToken cancellationToken);
    Task<BasketOverrideResponse> SaveOverrideAsync(SaveBasketOverrideRequest request, CancellationToken cancellationToken);
}

public sealed class PlanService(
    IDbConnectionFactory connectionFactory,
    ICustomerContext customerContext,
    IInsightService insightService,
    IConfiguration configuration) : IPlanService
{
    public sealed class PlanDbRow
    {
        public PlanDbRow() { }
        public decimal? IrregularMonthlyAdjusted { get; set; }
        public decimal SafetyBuffer { get; set; }
        public decimal SavingsGoal { get; set; }
        public DateTime? Payday { get; set; }
    }

    public async Task<PlanResponse> GetAsync(CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await EnsurePlanRowAsync(connection, cancellationToken);
        var row = await ReadPlanAsync(connection, cancellationToken);
        var estimate = await insightService.GetIrregularAverageAsync(cancellationToken);

        return new PlanResponse(
            estimate,
            row.IrregularMonthlyAdjusted is null ? null : Money(row.IrregularMonthlyAdjusted.Value),
            Money(row.SafetyBuffer),
            Money(row.SavingsGoal),
            row.Payday?.ToString("yyyy-MM-dd"));
    }

    public async Task<PlanResponse> UpdateAsync(JsonElement payload, CancellationToken cancellationToken)
    {
        if (payload.ValueKind != JsonValueKind.Object)
            throw new ApiException(400, "Request body must be a JSON object.");

        var allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            "irregular_monthly_adjusted", "safety_buffer", "savings_goal", "payday"
        };
        foreach (var property in payload.EnumerateObject())
        {
            if (!allowed.Contains(property.Name))
                throw new ApiException(400, $"Unknown plan field: {property.Name}.");
        }
        if (!payload.EnumerateObject().Any())
            throw new ApiException(400, "Provide at least one plan field to update.");

        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await EnsurePlanRowAsync(connection, cancellationToken);
        var current = await ReadPlanAsync(connection, cancellationToken);

        var adjusted = current.IrregularMonthlyAdjusted;
        var safetyBuffer = current.SafetyBuffer;
        var savingsGoal = current.SavingsGoal;
        DateTime? payday = current.Payday;

        if (payload.TryGetProperty("irregular_monthly_adjusted", out var adjustedElement))
            adjusted = adjustedElement.ValueKind == JsonValueKind.Null ? null : ReadNonNegativeDecimal(adjustedElement, "irregular_monthly_adjusted");
        if (payload.TryGetProperty("safety_buffer", out var bufferElement))
            safetyBuffer = ReadNonNegativeDecimal(bufferElement, "safety_buffer");
        if (payload.TryGetProperty("savings_goal", out var goalElement))
            savingsGoal = ReadNonNegativeDecimal(goalElement, "savings_goal");
        if (payload.TryGetProperty("payday", out var paydayElement))
        {
            if (paydayElement.ValueKind == JsonValueKind.Null)
                payday = null;
            else if (paydayElement.ValueKind == JsonValueKind.String
                     && DateOnly.TryParseExact(paydayElement.GetString(), "yyyy-MM-dd", out var parsed))
                payday = parsed.ToDateTime(TimeOnly.MinValue);
            else
                throw new ApiException(400, "payday must be a yyyy-MM-dd date or null.");
        }

        const string updateSql = @"
INSERT INTO user_plans (customer_id, irregular_monthly_adjusted, safety_buffer_eur, savings_goal_eur, payday)
VALUES (@CustomerId, @Adjusted, @SafetyBuffer, @SavingsGoal, @Payday)
ON DUPLICATE KEY UPDATE
    irregular_monthly_adjusted = VALUES(irregular_monthly_adjusted),
    safety_buffer_eur = VALUES(safety_buffer_eur),
    savings_goal_eur = VALUES(savings_goal_eur),
    payday = VALUES(payday),
    updated_at = CURRENT_TIMESTAMP;";

        await connection.ExecuteAsync(new CommandDefinition(updateSql, new
        {
            CustomerId = customerContext.CustomerId,
            Adjusted = adjusted,
            SafetyBuffer = safetyBuffer,
            SavingsGoal = savingsGoal,
            Payday = payday
        }, cancellationToken: cancellationToken));

        var estimateAfterUpdate = await insightService.GetIrregularAverageAsync(cancellationToken);
        return new PlanResponse(estimateAfterUpdate, adjusted is null ? null : Money(adjusted.Value),
            Money(safetyBuffer), Money(savingsGoal), payday?.ToString("yyyy-MM-dd"));
    }

    public async Task<BasketOverrideResponse> SaveOverrideAsync(
        SaveBasketOverrideRequest request,
        CancellationToken cancellationToken)
    {
        var matchType = request.MatchType.Trim().ToLowerInvariant();
        var basket = request.Basket.Trim().ToLowerInvariant();
        var matchValue = request.MatchValue.Trim();
        if (matchValue.Length == 0)
            throw new ApiException(400, "match_value cannot be empty.");
        if (matchType is not ("merchant" or "category"))
            throw new ApiException(400, "match_type must be merchant or category.");
        if (basket is not ("regular" or "irregular" or "leaks"))
            throw new ApiException(400, "basket must be regular, irregular, or leaks.");

        const string sql = @"
INSERT INTO basket_overrides (customer_id, match_type, match_value, basket)
VALUES (@CustomerId, @MatchType, @MatchValue, @Basket)
ON DUPLICATE KEY UPDATE basket = VALUES(basket), updated_at = CURRENT_TIMESTAMP;";
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(sql, new
        {
            CustomerId = customerContext.CustomerId,
            MatchType = matchType,
            MatchValue = matchValue,
            Basket = basket
        }, cancellationToken: cancellationToken));

        return new BasketOverrideResponse(matchType, matchValue, basket);
    }

    private async Task EnsurePlanRowAsync(MySqlConnector.MySqlConnection connection, CancellationToken cancellationToken)
    {
        const string sql = @"
INSERT IGNORE INTO user_plans (customer_id, safety_buffer_eur, savings_goal_eur)
VALUES (@CustomerId, @SafetyBuffer, @SavingsGoal);";
        await connection.ExecuteAsync(new CommandDefinition(sql, new
        {
            CustomerId = customerContext.CustomerId,
            SafetyBuffer = configuration.GetValue<decimal>("App:DefaultSafetyBuffer", 50m),
            SavingsGoal = configuration.GetValue<decimal>("App:DefaultSavingsGoal", 100m)
        }, cancellationToken: cancellationToken));
    }

    private async Task<PlanDbRow> ReadPlanAsync(MySqlConnector.MySqlConnection connection, CancellationToken cancellationToken)
    {
        const string sql = @"
SELECT irregular_monthly_adjusted AS IrregularMonthlyAdjusted,
       safety_buffer_eur AS SafetyBuffer,
       savings_goal_eur AS SavingsGoal,
       payday AS Payday
FROM user_plans WHERE customer_id = @CustomerId;";
        return await connection.QuerySingleAsync<PlanDbRow>(
            new CommandDefinition(sql, new { customerContext.CustomerId }, cancellationToken: cancellationToken));
    }

    private static decimal ReadNonNegativeDecimal(JsonElement element, string field)
    {
        if (element.ValueKind != JsonValueKind.Number || !element.TryGetDecimal(out var value) || value < 0)
            throw new ApiException(400, $"{field} must be a non-negative number.");
        return value;
    }

    private static decimal Money(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
