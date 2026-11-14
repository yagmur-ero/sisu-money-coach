using Dapper;
using PersonalEconomicCoach.Api.Data;
using PersonalEconomicCoach.Api.Models;

namespace PersonalEconomicCoach.Api.Services;

public interface IEventService
{
    Task<IReadOnlyList<PlannedEventResponse>> GetAsync(CancellationToken cancellationToken);
    Task<PlannedEventResponse> CreateAsync(CreateEventRequest request, CancellationToken cancellationToken);
    Task<bool> DeleteAsync(long id, CancellationToken cancellationToken);
}

public sealed class EventService(IDbConnectionFactory connectionFactory, ICustomerContext customerContext) : IEventService
{
    public async Task<IReadOnlyList<PlannedEventResponse>> GetAsync(CancellationToken cancellationToken)
    {
        const string sql = @"
SELECT id AS Id,
       name AS Name,
       DATE_FORMAT(event_date, '%Y-%m-%d') AS Date,
       amount_eur AS Amount
FROM planned_events
WHERE customer_id = @CustomerId
ORDER BY event_date, id;";

        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<PlannedEventResponse>(
            new CommandDefinition(sql, new { customerContext.CustomerId }, cancellationToken: cancellationToken));
        return rows.ToList();
    }

    public async Task<PlannedEventResponse> CreateAsync(CreateEventRequest request, CancellationToken cancellationToken)
    {
        var name = request.Name.Trim();
        if (name.Length == 0)
            throw new ApiException(400, "name cannot be empty.");
        if (!DateOnly.TryParseExact(request.Date, "yyyy-MM-dd", out var date))
            throw new ApiException(400, "date must use yyyy-MM-dd format.");
        if (request.Amount < 0)
            throw new ApiException(400, "amount must be zero or greater.");

        const string insertSql = @"
INSERT INTO planned_events (customer_id, name, event_date, amount_eur)
VALUES (@CustomerId, @Name, @EventDate, @Amount);";

        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(insertSql, new
        {
            CustomerId = customerContext.CustomerId,
            Name = name,
            EventDate = date.ToDateTime(TimeOnly.MinValue),
            Amount = request.Amount
        }, cancellationToken: cancellationToken));
        var id = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT LAST_INSERT_ID();", cancellationToken: cancellationToken));

        return new PlannedEventResponse(id, name, date.ToString("yyyy-MM-dd"), request.Amount);
    }

    public async Task<bool> DeleteAsync(long id, CancellationToken cancellationToken)
    {
        const string sql = "DELETE FROM planned_events WHERE id = @Id AND customer_id = @CustomerId;";
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        var affected = await connection.ExecuteAsync(new CommandDefinition(sql, new
        {
            Id = id,
            customerContext.CustomerId
        }, cancellationToken: cancellationToken));
        return affected > 0;
    }
}
