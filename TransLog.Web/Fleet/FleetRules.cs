using System.Globalization;

namespace TransLog.Web.Fleet;

/// <summary>Registration plates and aliases: upper case, letters and digits only.</summary>
public static class Plates
{
    /// <summary>"llt 587-nw" becomes "LLT587NW".</summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";
        var chars = text.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant);
        return new string(chars.ToArray());
    }

    /// <summary>A normalised plate is 2 to 20 letters or digits, ASCII only.</summary>
    public static bool IsValid(string normalised) =>
        normalised.Length is >= 2 and <= 20 && normalised.All(c => c is (>= 'A' and <= 'Z') or (>= '0' and <= '9'));
}

/// <summary>South African cell numbers, stored as 10 digits starting with 0.</summary>
public static class CellNumbers
{
    /// <summary>"+27 82 123 4567", "27821234567" and "082 123 4567" all become "0821234567". Anything else comes back as typed digits.</summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";
        var digits = new string(text.Where(char.IsAsciiDigit).ToArray());
        if (digits.Length == 11 && digits.StartsWith("27", StringComparison.Ordinal))
            digits = "0" + digits[2..];
        return digits;
    }

    public static bool IsValid(string normalised) =>
        normalised.Length == 10 && normalised[0] == '0' && normalised.All(char.IsAsciiDigit);
}

/// <summary>South African ID numbers: 13 digits, a real birth date and a correct check digit.</summary>
public static class SaId
{
    public static string Normalize(string? text) =>
        string.IsNullOrWhiteSpace(text) ? "" : new string(text.Where(char.IsAsciiDigit).ToArray());

    public static bool IsValid(string? id)
    {
        if (id is null || id.Length != 13 || !id.All(char.IsAsciiDigit))
            return false;

        // YYMMDD: the century is not printed, so accept the date in either century.
        var yy = id[..2];
        var mmdd = id.Substring(2, 4);
        var okDate = new[] { "19", "20" }.Any(century =>
            DateTime.TryParseExact(century + yy + mmdd, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _));
        if (!okDate)
            return false;

        // Luhn check over all 13 digits (the last one is the check digit).
        var sum = 0;
        var doubleIt = false;
        for (var i = id.Length - 1; i >= 0; i--)
        {
            var d = id[i] - '0';
            if (doubleIt)
            {
                d *= 2;
                if (d > 9) d -= 9;
            }
            sum += d;
            doubleIt = !doubleIt;
        }
        return sum % 10 == 0;
    }
}

public enum ExpiryState
{
    /// <summary>No date entered.</summary>
    None,
    Ok,
    DueSoon,
    Expired,
}

/// <summary>What the lists show next to an expiry date.</summary>
public readonly record struct ExpiryInfo(ExpiryState State, string Text)
{
    /// <summary>Bootstrap badge classes; empty when there is nothing to flag.</summary>
    public string BadgeCss => State switch
    {
        ExpiryState.Expired => "text-bg-danger",
        ExpiryState.DueSoon => "text-bg-warning",
        _ => "",
    };
}

public static class Expiry
{
    public const int WarnDays = 30;

    /// <summary>Today in South African time.</summary>
    public static DateOnly Today() => DateOnly.FromDateTime(AppCulture.ToSast(DateTime.UtcNow));

    public static ExpiryInfo Describe(DateOnly? date, DateOnly today)
    {
        if (date is not DateOnly d)
            return new ExpiryInfo(ExpiryState.None, "");

        var days = d.DayNumber - today.DayNumber;
        if (days < 0)
            return new ExpiryInfo(ExpiryState.Expired, "Expired");
        if (days == 0)
            return new ExpiryInfo(ExpiryState.DueSoon, "Expires today");
        if (days <= WarnDays)
            return new ExpiryInfo(ExpiryState.DueSoon, days == 1 ? "Due tomorrow" : $"Due in {days} days");
        return new ExpiryInfo(ExpiryState.Ok, "");
    }

    public static string Format(DateOnly? date) =>
        date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "";
}

/// <summary>Choice lists for the vehicle form.</summary>
public static class VehicleOptions
{
    public static readonly string[] Types =
        ["Truck tractor", "Rigid truck", "Trailer", "Bakkie", "Car", "Other"];

    public static readonly string[] FuelTypes = ["Diesel", "Petrol", "Other"];
}

/// <summary>Choice lists for the driver form.</summary>
public static class DriverOptions
{
    /// <summary>South African driving licence codes.</summary>
    public static readonly string[] LicenceCodes = ["A1", "A", "B", "C1", "C", "EB", "EC1", "EC"];
}
