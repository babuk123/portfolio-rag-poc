namespace PortfolioRag.Api.Services;

/// <summary>Servers run on UTC; advisors read US Eastern time.</summary>
public static class TimeDisplay
{
    private static readonly TimeZoneInfo Eastern_ = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

    public static DateTime ToEastern(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Eastern_);

    public static string Eastern(DateTime utc) => ToEastern(utc).ToString("MMM d, yyyy h:mm tt") + " ET";
}
