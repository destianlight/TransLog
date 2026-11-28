using System.Globalization;
using Microsoft.EntityFrameworkCore;
using TransLog.Web.Data;

namespace TransLog.Web.Ai;

/// <summary>Tokens one AI call reported (DeepSeek counts cached tokens inside the input tokens).</summary>
public readonly record struct AiTokens(int Input, int Cached, int Output);

/// <summary>What one capture cost (Part 5a2a works it out; 5a2b stores it on the capture).</summary>
public sealed record AiCost(int? PriceId, bool Peak, decimal Usd, decimal UsdToZar, decimal Zar);

/// <summary>
/// Prices and cost (Part 5a2a). Costs are estimates from the tokens the provider reports, not its bill.
/// Peak hours are UTC; Chinese public holidays are not known, so they count as peak (a slight over-estimate, never under).
/// </summary>
public static class AiPricing
{
    private static readonly string[] Days = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];

    /// <summary>The price row that counts at that time: the latest EffectiveFrom on or before it.</summary>
    public static Task<AiPrice?> FindAsync(AppDbContext db, int providerId, string model, DateTime atUtc) =>
        db.AiPrices.AsNoTracking()
            .Where(p => p.AiProviderId == providerId && p.Model == model && p.EffectiveFrom <= atUtc)
            .OrderByDescending(p => p.EffectiveFrom).ThenByDescending(p => p.Id)
            .FirstOrDefaultAsync();

    public static bool HasOffPeak(AiPrice p) =>
        p.OffPeakInputPerM is not null && p.OffPeakCachedPerM is not null && p.OffPeakOutputPerM is not null;

    /// <summary>True when the peak price applies. A price without off-peak prices is the same all day (false).</summary>
    public static bool IsPeak(AiPrice p, DateTime atUtc)
    {
        if (!HasOffPeak(p))
            return false;
        var days = ParseDays(p.PeakDays) ?? new HashSet<int>(Enumerable.Range(0, 7));
        if (!days.Contains((int)atUtc.DayOfWeek))
            return false;
        var hours = ParseHours(p.PeakHoursUtc);
        if (hours is null || hours.Count == 0)
            return true; // peak all day on peak days
        var minute = atUtc.Hour * 60 + atUtc.Minute;
        return hours.Any(h => h.From <= h.To ? minute >= h.From && minute < h.To : minute >= h.From || minute < h.To);
    }

    public static AiCost Cost(AiPrice p, AiTokens t, DateTime atUtc, decimal usdToZar)
    {
        var peak = IsPeak(p, atUtc);
        var offPeak = HasOffPeak(p) && !peak;
        var input = offPeak ? p.OffPeakInputPerM!.Value : p.InputPerM;
        var cached = offPeak ? p.OffPeakCachedPerM!.Value : p.CachedInputPerM;
        var output = offPeak ? p.OffPeakOutputPerM!.Value : p.OutputPerM;

        var cachedTokens = Math.Clamp(t.Cached, 0, Math.Max(0, t.Input));
        var fresh = Math.Max(0, t.Input - cachedTokens);
        var usd = (fresh * input + cachedTokens * cached + Math.Max(0, t.Output) * output) / 1_000_000m;
        usd = Math.Round(usd, 8);
        return new AiCost(p.Id, peak, usd, usdToZar, Math.Round(usd * usdToZar, 6));
    }

    /// <summary>"Mon-Fri" or "Mon,Wed,Sat-Sun" as day numbers (Sunday = 0); null when empty or not understood.</summary>
    public static HashSet<int>? ParseDays(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var result = new HashSet<int>();
        foreach (var part in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var ends = part.Split('-', StringSplitOptions.TrimEntries);
            var a = DayIndex(ends[0]);
            var b = ends.Length == 2 ? DayIndex(ends[1]) : a;
            if (a < 0 || b < 0 || ends.Length > 2)
                return null;
            for (var d = a; ; d = (d + 1) % 7)
            {
                result.Add(d);
                if (d == b)
                    break;
            }
        }
        return result;
    }

    /// <summary>"01:00-04:00,06:00-10:00" as minutes after midnight (end not included); null when not understood.</summary>
    public static List<(int From, int To)>? ParseHours(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];
        var result = new List<(int, int)>();
        foreach (var part in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var ends = part.Split('-', StringSplitOptions.TrimEntries);
            if (ends.Length != 2 || Minutes(ends[0]) is not int start || Minutes(ends[1]) is not int end || start == end)
                return null;
            result.Add((start, end));
        }
        return result;
    }

    private static int DayIndex(string day) =>
        Array.FindIndex(Days, d => string.Equals(d, day.Length >= 3 ? day[..3] : day, StringComparison.OrdinalIgnoreCase));

    private static int? Minutes(string hhmm)
    {
        var bits = hhmm.Split(':');
        if (bits.Length != 2
            || !int.TryParse(bits[0], NumberStyles.None, CultureInfo.InvariantCulture, out var h)
            || !int.TryParse(bits[1], NumberStyles.None, CultureInfo.InvariantCulture, out var m)
            || h < 0 || m < 0 || m > 59 || h > 24 || (h == 24 && m != 0))
            return null;
        return h * 60 + m;
    }

    /// <summary>Reads a number typed with "." or "," as the decimal point (no thousands separators).</summary>
    public static decimal? ParseNumber(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var clean = text.Trim().Replace(" ", "", StringComparison.Ordinal).Replace(',', '.');
        return decimal.TryParse(clean, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var n) ? n : null;
    }

    /// <summary>A price or rate for display and form boxes: "." and no trailing zeros ("0.3", "0.006").</summary>
    public static string Show(decimal? n) => n?.ToString("0.########", CultureInfo.InvariantCulture) ?? "";

    /// <summary>A price in US dollars: "$0.3".</summary>
    public static string Usd(decimal? n) => "$" + Show(n);

    /// <summary>A small rand amount: "R0.0123".</summary>
    public static string Rand(decimal zar) => "R" + zar.ToString("0.0000", CultureInfo.InvariantCulture);
}
