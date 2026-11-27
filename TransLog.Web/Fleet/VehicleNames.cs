using Microsoft.EntityFrameworkCore;
using TransLog.Web.Data;

namespace TransLog.Web.Fleet;

/// <summary>One known make, model and variant, with how often it is used (the starting list counts as most used).</summary>
public sealed record VehicleName(string Make, string? Model, string? Variant, int Uses);

/// <summary>A "Did you mean …?" question for one box of the vehicle form.</summary>
public sealed record NameSuggestion(string Field, string Typed, string Suggestion);

/// <summary>
/// Part 3d: make, model and variant are free text with suggestions. The suggestions are the starting list below plus
/// whatever any company has typed, so the list builds itself; nobody maintains it. Values that differ only in capitals,
/// spaces and dashes count as the same (see <see cref="Key"/>), and on save they take the known spelling.
/// When a make or model matches nothing, a near match (a letter or two off, or the start of a known name) is offered
/// as a question, never changed silently. Variants are only matched exactly: "2645" and "2644" are different trucks.
/// </summary>
public static class VehicleNames
{
    /// <summary>What two spellings must share to count as the same: upper case letters and digits only.</summary>
    public static string Key(string? text) =>
        string.IsNullOrWhiteSpace(text) ? "" : new string(text.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    /// <summary>The starting suggestions: common South African trucks, trailers, bakkies and cars.</summary>
    private static readonly Dictionary<string, string[]> Starting = new()
    {
        ["Volvo"] = ["FH", "FM", "FMX"],
        ["Scania"] = ["R-series", "G-series", "P-series", "S-series"],
        ["Mercedes-Benz"] = ["Actros", "Arocs", "Axor", "Atego", "Sprinter"],
        ["MAN"] = ["TGX", "TGS", "TGM", "TGL"],
        ["DAF"] = ["XF", "CF", "LF"],
        ["Iveco"] = ["S-Way", "Stralis", "Eurocargo", "Daily"],
        ["Isuzu"] = ["FX", "F-series", "N-series", "D-Max"],
        ["UD Trucks"] = ["Quester", "Quon", "Croner"],
        ["Hino"] = ["700", "500", "300"],
        ["FAW"] = ["JH6", "J5"],
        ["Fuso"] = ["Canter", "FJ"],
        ["Tata"] = ["Prima", "LPT"],
        ["Powerstar"] = ["VX"],
        ["Sinotruk"] = ["HOWO"],
        ["Shacman"] = ["X3000"],
        ["Toyota"] = ["Hilux", "Land Cruiser 79", "Quantum", "Corolla Cross"],
        ["Ford"] = ["Ranger"],
        ["Nissan"] = ["Navara", "NP200"],
        ["Volkswagen"] = ["Amarok", "Polo"],
        ["Mahindra"] = ["Pik Up"],
        ["GWM"] = ["P-Series"],
        ["Mitsubishi"] = ["Triton"],
        ["Afrit"] = ["Tautliner", "Flat deck", "Side tipper"],
        ["Henred Fruehauf"] = ["Tautliner", "Flat deck", "Side tipper", "Tanker"],
        ["SA Truck Bodies"] = ["Tautliner", "Refrigerated"],
        ["Serco"] = ["Tautliner", "Refrigerated"],
        ["GRW"] = ["Tanker"],
    };

    /// <summary>Spellings from the starting list win over typed ones with the same key.</summary>
    private const int StartingUses = 1_000_000;

    /// <summary>
    /// Every known make, model and variant: the starting list plus every company's vehicles. Only names are used,
    /// never which company has them, so the tenant filter is switched off on purpose.
    /// </summary>
    public static async Task<List<VehicleName>> AllAsync(AppDbContext db)
    {
        var used = await db.Vehicles.IgnoreQueryFilters().AsNoTracking()
            .Where(v => v.Make != null)
            .GroupBy(v => new { v.Make, v.Model, v.Variant })
            .Select(g => new { g.Key.Make, g.Key.Model, g.Key.Variant, Uses = g.Count() })
            .ToListAsync();

        var all = new List<VehicleName>();
        foreach (var (make, models) in Starting)
        {
            all.Add(new VehicleName(make, null, null, StartingUses));
            all.AddRange(models.Select(m => new VehicleName(make, m, null, StartingUses)));
        }
        all.AddRange(used.Select(u => new VehicleName(u.Make!, u.Model, u.Variant, u.Uses)));
        return all;
    }

    /// <summary>The known makes, one spelling per key (the most used).</summary>
    public static List<string> Makes(IEnumerable<VehicleName> all) =>
        Pick(all.Select(n => (n.Make, n.Uses)));

    /// <summary>The known models of a make.</summary>
    public static List<string> Models(IEnumerable<VehicleName> all, string? make)
    {
        var makeKey = Key(make);
        return Pick(all.Where(n => n.Model is not null && Key(n.Make) == makeKey).Select(n => (n.Model!, n.Uses)));
    }

    /// <summary>The known variants of a make and model.</summary>
    public static List<string> Variants(IEnumerable<VehicleName> all, string? make, string? model)
    {
        var (makeKey, modelKey) = (Key(make), Key(model));
        return Pick(all.Where(n => n.Variant is not null && Key(n.Make) == makeKey && Key(n.Model) == modelKey)
            .Select(n => (n.Variant!, n.Uses)));
    }

    /// <summary>
    /// Looks a typed value up in the known spellings. Exact (same key): the known spelling to store.
    /// Otherwise, when <paramref name="nearMatch"/> is on: one clear near match to ask about, or nothing.
    /// </summary>
    public static (string? Exact, string? Suggestion) Find(string typed, IReadOnlyList<string> known, bool nearMatch)
    {
        var key = Key(typed);
        if (key.Length == 0)
            return (null, null);

        var exact = known.FirstOrDefault(k => Key(k) == key);
        if (exact is not null)
            return (exact, null);
        if (!nearMatch || key.Length < 4)
            return (null, null); // short names ("MAN", "FH") are too easy to mix up

        // The start of exactly one known name ("Merc" -> "Mercedes-Benz").
        var starts = known.Where(k => Key(k).StartsWith(key, StringComparison.Ordinal)).ToList();
        if (starts.Count == 1)
            return (null, starts[0]);

        // A letter or two off ("Mercedez-Benz", "Scnaia"): only when one name is clearly the closest.
        var allowed = key.Length <= 6 ? 1 : 2;
        var close = known
            .Select(k => (Name: k, Distance: Distance(Key(k), key)))
            .Where(x => x.Distance <= allowed)
            .OrderBy(x => x.Distance)
            .ToList();
        if (close.Count == 1 || (close.Count > 1 && close[0].Distance < close[1].Distance))
            return (null, close[0].Name);
        return (null, null);
    }

    /// <summary>One spelling per key, the most used first, then by name.</summary>
    private static List<string> Pick(IEnumerable<(string Value, int Uses)> names) =>
        names.Where(n => Key(n.Value).Length > 0)
            .GroupBy(n => Key(n.Value))
            .Select(g => g.GroupBy(n => n.Value).Select(s => (s.Key, Uses: s.Sum(x => x.Uses))).OrderByDescending(s => s.Uses).First().Key)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>Edit distance (Damerau–Levenshtein, optimal string alignment): swapped letters count as one change.</summary>
    public static int Distance(string a, string b)
    {
        var d = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (var j = 0; j <= b.Length; j++) d[0, j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1])
                    d[i, j] = Math.Min(d[i, j], d[i - 2, j - 2] + 1);
            }
        }
        return d[a.Length, b.Length];
    }
}
