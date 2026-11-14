using System.Text.Json;
using MySqlConnector;
using PersonalEconomicCoach.Api.Models;

namespace PersonalEconomicCoach.Api.Middleware;

public sealed class ApiExceptionMiddleware(RequestDelegate next, ILogger<ApiExceptionMiddleware> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // The client disconnected; there is no response to write.
        }
        catch (Exception exception)
        {
            var (statusCode, message) = exception switch
            {
                ApiException apiException => (apiException.StatusCode, apiException.Message),
                MySqlException => (503, "The database is temporarily unavailable."),
                _ => (500, "An unexpected server error occurred.")
            };

            if (statusCode >= 500)
                logger.LogError(exception, "API request failed with status {StatusCode}.", statusCode);
            else
                logger.LogWarning("API request rejected: {Message}", message);

            if (context.Response.HasStarted) throw;

            context.Response.Clear();
            context.Response.StatusCode = statusCode;
            context.Response.ContentType = "application/json";
            await JsonSerializer.SerializeAsync(
                context.Response.Body,
                new ErrorResponse(message),
                JsonOptions,
                context.RequestAborted);
        }
    }
}
