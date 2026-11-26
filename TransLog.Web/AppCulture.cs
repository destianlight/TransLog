using System.Globalization;

namespace TransLog.Web;

/// <summary>
/// South Africa only. One fixed culture for display and form input,
/// and the South African time zone for showing times.
/// Rules: store times in UTC; parse AI output, JSON and stored data with InvariantCulture.
/// </summary>
public static class AppCulture
{
    public static readonly CultureInfo ZA = Build();

    public static readonly TimeZoneInfo Sast = FindSast();

    /// <summary>Converts a UTC time to South African time for display.</summary>
    public static DateTime ToSast(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Sast);

    private static CultureInfo Build()
    {
        var ci = new CultureInfo("en-ZA");

        var n = ci.NumberFormat;
        n.NumberDecimalSeparator = ".";
        n.CurrencyDecimalSeparator = ".";
        n.PercentDecimalSeparator = ".";
        n.NumberGroupSeparator = " ";
        n.CurrencyGroupSeparator = " ";
        n.PercentGroupSeparator = " ";
        n.CurrencySymbol = "R";

        var d = ci.DateTimeFormat;
        d.ShortDatePattern = "yyyy-MM-dd";
        d.ShortTimePattern = "HH:mm";
        d.LongTimePattern = "HH:mm:ss";

        return CultureInfo.ReadOnly(ci);
    }

    private static TimeZoneInfo FindSast()
    {
        // The IANA name works on Linux and on Windows with .NET 6 or later;
        // the Windows name is a fallback for older hosts.
        foreach (var id in new[] { "Africa/Johannesburg", "South Africa Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        // South Africa has no daylight saving, so a fixed UTC+2 is a safe last resort.
        return TimeZoneInfo.CreateCustomTimeZone("SAST", TimeSpan.FromHours(2), "South Africa", "SAST");
    }
}
