namespace PersonalEconomicCoach.Api.Services;

public interface IDateProvider
{
    DateOnly Today { get; }
}

public sealed class HelsinkiDateProvider(IConfiguration configuration) : IDateProvider
{
    private readonly TimeZoneInfo _timeZone = ResolveTimeZone(configuration["App:TimeZoneId"] ?? "Europe/Helsinki");

    public DateOnly Today
    {
        get
        {
            var local = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, _timeZone);
            return DateOnly.FromDateTime(local.DateTime);
        }
    }

    private static TimeZoneInfo ResolveTimeZone(string id)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.Utc; }
        catch (InvalidTimeZoneException) { return TimeZoneInfo.Utc; }
    }
}
