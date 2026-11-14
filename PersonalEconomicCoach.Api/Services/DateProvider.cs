using System.Globalization;

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
            // Demo override: the challenge dataset ends on 2026-09-30, so a fixed "today"
            // keeps month-based insights populated. Leave App:DemoToday empty to use the real date.
            var demoToday = configuration["App:DemoToday"];
            if (!string.IsNullOrWhiteSpace(demoToday)
                && DateOnly.TryParseExact(demoToday, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var fixedToday))
            {
                return fixedToday;
            }

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
