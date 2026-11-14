using System.Text.Json;
using Dapper;
using PersonalEconomicCoach.Api.Data;
using PersonalEconomicCoach.Api.Middleware;
using PersonalEconomicCoach.Api.Models;

namespace PersonalEconomicCoach.Api.Services;

public interface IConversationService
{
    Task<long> SaveExchangeAsync(
        long? conversationId,
        string userText,
        string assistantText,
        string insightType,
        object data,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ConversationSummaryResponse>> GetConversationsAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<ConversationMessageResponse>> GetMessagesAsync(long conversationId, CancellationToken cancellationToken);
}

/// <summary>
/// Stores application chat history in the same spankki database, associated with the existing customer_id.
/// It does not create a new user identity or modify S-Pankki banking tables.
/// </summary>
public sealed class ConversationService(
    IDbConnectionFactory connectionFactory,
    ICustomerContext customerContext) : IConversationService
{
    private static readonly JsonSerializerOptions DataJsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public async Task<long> SaveExchangeAsync(
        long? conversationId,
        string userText,
        string assistantText,
        string insightType,
        object data,
        CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        long resolvedConversationId;
        if (conversationId.HasValue)
        {
            const string ownershipSql = @"
SELECT COUNT(*)
FROM conversation_threads
WHERE id = @ConversationId AND customer_id = @CustomerId;";

            var owned = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                ownershipSql,
                new { ConversationId = conversationId.Value, customerContext.CustomerId },
                transaction,
                cancellationToken: cancellationToken));

            if (owned == 0)
                throw new ApiException(404, "Conversation not found.");

            resolvedConversationId = conversationId.Value;
        }
        else
        {
            const string createSql = @"
INSERT INTO conversation_threads (customer_id, title)
VALUES (@CustomerId, @Title);";

            await connection.ExecuteAsync(new CommandDefinition(createSql, new
            {
                customerContext.CustomerId,
                Title = CreateTitle(userText)
            }, transaction, cancellationToken: cancellationToken));

            resolvedConversationId = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
                "SELECT LAST_INSERT_ID();", transaction: transaction, cancellationToken: cancellationToken));
        }

        const string insertMessagesSql = @"
INSERT INTO conversation_messages
    (conversation_id, customer_id, role, content, insight_type, data_json)
VALUES
    (@ConversationId, @CustomerId, 'user', @UserText, NULL, NULL),
    (@ConversationId, @CustomerId, 'assistant', @AssistantText, @InsightType, CAST(@DataJson AS JSON));";

        await connection.ExecuteAsync(new CommandDefinition(insertMessagesSql, new
        {
            ConversationId = resolvedConversationId,
            customerContext.CustomerId,
            UserText = userText.Trim(),
            AssistantText = assistantText,
            InsightType = insightType,
            DataJson = JsonSerializer.Serialize(data, DataJsonOptions)
        }, transaction, cancellationToken: cancellationToken));

        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE conversation_threads SET updated_at = CURRENT_TIMESTAMP WHERE id = @ConversationId AND customer_id = @CustomerId;",
            new { ConversationId = resolvedConversationId, customerContext.CustomerId },
            transaction,
            cancellationToken: cancellationToken));

        await transaction.CommitAsync(cancellationToken);
        return resolvedConversationId;
    }

    public async Task<IReadOnlyList<ConversationSummaryResponse>> GetConversationsAsync(CancellationToken cancellationToken)
    {
        const string sql = @"
SELECT
    t.id AS Id,
    t.title AS Title,
    DATE_FORMAT(t.created_at, '%Y-%m-%d %H:%i:%s') AS CreatedAt,
    DATE_FORMAT(t.updated_at, '%Y-%m-%d %H:%i:%s') AS UpdatedAt,
    COUNT(m.id) AS MessageCount
FROM conversation_threads t
LEFT JOIN conversation_messages m ON m.conversation_id = t.id AND m.customer_id = t.customer_id
WHERE t.customer_id = @CustomerId
GROUP BY t.id, t.title, t.created_at, t.updated_at
ORDER BY t.updated_at DESC, t.id DESC
LIMIT 100;";

        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<ConversationSummaryResponse>(new CommandDefinition(
            sql, new { customerContext.CustomerId }, cancellationToken: cancellationToken));
        return rows.ToList();
    }

    public async Task<IReadOnlyList<ConversationMessageResponse>> GetMessagesAsync(
        long conversationId,
        CancellationToken cancellationToken)
    {
        if (conversationId <= 0)
            throw new ApiException(400, "conversation_id must be greater than zero.");

        const string sql = @"
SELECT
    id AS Id,
    role AS Role,
    content AS Content,
    insight_type AS InsightType,
    CAST(data_json AS CHAR) AS DataJson,
    DATE_FORMAT(created_at, '%Y-%m-%d %H:%i:%s') AS CreatedAt
FROM conversation_messages
WHERE conversation_id = @ConversationId
  AND customer_id = @CustomerId
ORDER BY id;";

        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        var exists = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM conversation_threads WHERE id = @ConversationId AND customer_id = @CustomerId;",
            new { ConversationId = conversationId, customerContext.CustomerId },
            cancellationToken: cancellationToken));

        if (exists == 0)
            throw new ApiException(404, "Conversation not found.");

        var rows = await connection.QueryAsync<ConversationMessageResponse>(new CommandDefinition(
            sql, new { ConversationId = conversationId, customerContext.CustomerId },
            cancellationToken: cancellationToken));
        return rows.ToList();
    }

    private static string CreateTitle(string text)
    {
        var title = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return title.Length <= 150 ? title : title[..147] + "...";
    }
}
