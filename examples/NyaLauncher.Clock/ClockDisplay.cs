using System.Globalization;

namespace NyaLauncher.Clock;

internal sealed record ClockOptions(
    bool Use24HourFormat,
    bool ShowTimeZone,
    bool ShowSeconds,
    double Scale);

internal sealed record ClockDisplay(
    string Time,
    string TimeZone,
    string Period,
    string Seconds,
    bool ShowTimeZone,
    bool ShowPeriod,
    bool ShowSeconds);

internal static class ClockDisplayFormatter
{
    public static ClockDisplay Create(
        DateTimeOffset now,
        ClockOptions options,
        TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeZone);

        var localTime = TimeZoneInfo.ConvertTime(now, timeZone);
        var period = localTime.ToString("tt", CultureInfo.CurrentCulture);
        if (string.IsNullOrWhiteSpace(period))
            period = localTime.Hour < 12 ? "AM" : "PM";

        return new ClockDisplay(
            localTime.ToString(options.Use24HourFormat ? "HH:mm" : "hh:mm", CultureInfo.InvariantCulture),
            FormatUtcOffset(localTime.Offset),
            period,
            localTime.ToString("ss", CultureInfo.InvariantCulture),
            options.ShowTimeZone,
            !options.Use24HourFormat,
            options.ShowSeconds);
    }

    private static string FormatUtcOffset(TimeSpan offset)
    {
        var sign = offset < TimeSpan.Zero ? '-' : '+';
        var absolute = offset.Duration();
        return $"UTC{sign}{absolute.Hours:00}:{absolute.Minutes:00}";
    }
}
