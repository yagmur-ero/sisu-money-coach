namespace PersonalEconomicCoach.Api.Middleware;

public sealed class ApiException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
