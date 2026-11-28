using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TransLog.Web.Ai;
using TransLog.Web.Data;
using TransLog.Web.Security;

namespace TransLog.Web.Areas.SysAdmin.Pages.Providers;

/// <summary>What the new-price form sends. Strings: numbers may be typed with "." or ",".</summary>
public sealed class PriceForm
{
    public string? Model { get; set; }
    public string? From { get; set; }
    public string? Input { get; set; }
    public string? Cached { get; set; }
    public string? Output { get; set; }
    public string? OffPeakInput { get; set; }
    public string? OffPeakCached { get; set; }
    public string? OffPeakOutput { get; set; }
    public string? PeakHours { get; set; }
    public string? PeakDays { get; set; }
}

/// <summary>
/// Part 5a2a: a provider's prices, newest first. A new price is a new row (never edited), counting from its date;
/// old slips keep the cost worked out with the price of their time.
/// </summary>
public class PricesModel(AppDbContext db, AuditLogger audit) : PageModel
{
    /// <summary>A typical slip: two calls, about 2 400 input tokens of which 1 000 cached, 600 output (27 Nov 2026 tests).</summary>
    public static readonly AiTokens TypicalSlip = new(2400, 1000, 600);

    [BindProperty]
    public PriceForm Form { get; set; } = new();

    public AiProvider Provider { get; private set; } = new();

    public List<AiPrice> Prices { get; private set; } = [];

    public int? CurrentId { get; private set; }

    public decimal UsdToZar { get; private set; }

    public Dictionary<int, string> Users { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(int id)
    {
        if (!await LoadAsync(id))
            return NotFound();
        var current = Prices.FirstOrDefault(p => p.Id == CurrentId);
        Form = new PriceForm
        {
            Model = Provider.Model,
            From = AppCulture.ToSast(DateTime.UtcNow).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            Input = AiPricing.Show(current?.InputPerM), Cached = AiPricing.Show(current?.CachedInputPerM), Output = AiPricing.Show(current?.OutputPerM),
            OffPeakInput = AiPricing.Show(current?.OffPeakInputPerM), OffPeakCached = AiPricing.Show(current?.OffPeakCachedPerM),
            OffPeakOutput = AiPricing.Show(current?.OffPeakOutputPerM),
            PeakHours = current?.PeakHoursUtc, PeakDays = current?.PeakDays,
        };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        ModelState.Clear();
        if (!await LoadAsync(id))
            return NotFound();

        var (price, errors) = Read(Form, Provider.Id);
        if (price is null)
        {
            foreach (var e in errors)
                ModelState.AddModelError(string.Empty, e);
            return Page();
        }

        price.CreatedAt = DateTime.UtcNow;
        price.CreatedByUserId = User.GetUserId();
        db.AiPrices.Add(price);
        audit.Add("AiPriceAdded", $"{Provider.Key} {price.Model} from {AppCulture.ToSast(price.EffectiveFrom):yyyy-MM-dd}: "
            + $"{AiPricing.Show(price.InputPerM)} / {AiPricing.Show(price.CachedInputPerM)} / {AiPricing.Show(price.OutputPerM)}"
            + (AiPricing.HasOffPeak(price) ? $", off-peak {AiPricing.Show(price.OffPeakInputPerM)} / {AiPricing.Show(price.OffPeakCachedPerM)} / {AiPricing.Show(price.OffPeakOutputPerM)}" : ""));
        await db.SaveChangesAsync();
        TempData["Message"] = "Price added.";
        return RedirectToPage(new { id });
    }

    /// <summary>Checks the form; a price row (not saved) or the problems.</summary>
    public static (AiPrice? Price, List<string> Errors) Read(PriceForm f, int providerId)
    {
        var errors = new List<string>();
        var model = (f.Model ?? "").Trim();
        if (model.Length == 0 || model.Length > 100)
            errors.Add("Enter the model name.");
        var day = AppCulture.ParseDay(f.From);
        if (day is null)
            errors.Add("Enter the from date as YYYY-MM-DD.");

        decimal? Need(string? text, string name, bool zeroOk)
        {
            var n = AiPricing.ParseNumber(text);
            if (n is null || n < 0 || (!zeroOk && n == 0) || n > 1000)
            {
                errors.Add($"{name}: enter a price in US dollars per 1M tokens{(zeroOk ? "" : " above 0")}, for example 0.30.");
                return null;
            }
            return n;
        }

        var input = Need(f.Input, "Input", false);
        var cached = Need(f.Cached, "Cached input", true);
        var output = Need(f.Output, "Output", false);

        var offGiven = new[] { f.OffPeakInput, f.OffPeakCached, f.OffPeakOutput }.Count(t => !string.IsNullOrWhiteSpace(t));
        decimal? offInput = null, offCached = null, offOutput = null;
        string? hours = null, days = null;
        if (offGiven is > 0 and < 3)
            errors.Add("Give all three off-peak prices, or none (one price all day).");
        else if (offGiven == 3)
        {
            offInput = Need(f.OffPeakInput, "Off-peak input", false);
            offCached = Need(f.OffPeakCached, "Off-peak cached input", true);
            offOutput = Need(f.OffPeakOutput, "Off-peak output", false);
            hours = string.IsNullOrWhiteSpace(f.PeakHours) ? null : f.PeakHours.Trim();
            days = string.IsNullOrWhiteSpace(f.PeakDays) ? null : f.PeakDays.Trim();
            if (AiPricing.ParseHours(hours) is null || hours is { Length: > 100 })
                errors.Add("Peak hours (UTC) look like 01:00-04:00,06:00-10:00.");
            if (days is not null && (AiPricing.ParseDays(days) is null || days.Length > 50))
                errors.Add("Peak days look like Mon-Fri or Mon,Wed,Fri.");
            if (hours is null && days is null)
                errors.Add("With off-peak prices, give the peak hours or the peak days.");
        }

        if (errors.Count > 0)
            return (null, errors);
        return (new AiPrice
        {
            AiProviderId = providerId,
            Model = model,
            EffectiveFrom = AppCulture.SastDayStartUtc(day!.Value),
            InputPerM = input!.Value, CachedInputPerM = cached!.Value, OutputPerM = output!.Value,
            OffPeakInputPerM = offInput, OffPeakCachedPerM = offCached, OffPeakOutputPerM = offOutput,
            PeakHoursUtc = hours, PeakDays = days,
            Source = AiPriceSource.Manual,
        }, errors);
    }

    /// <summary>"R0.0153 peak · R0.0077 off-peak" for a typical slip.</summary>
    public string Typical(AiPrice p)
    {
        var peakTime = new DateTime(2026, 11, 30, 7, 0, 0, DateTimeKind.Utc);    // a Monday, 09:00 in South Africa
        var offTime = new DateTime(2026, 11, 30, 12, 0, 0, DateTimeKind.Utc);    // a Monday, 14:00
        var one = AiPricing.Cost(p, TypicalSlip, peakTime, UsdToZar);
        if (!AiPricing.HasOffPeak(p))
            return AiPricing.Rand(one.Zar);
        var off = AiPricing.Cost(p, TypicalSlip, offTime, UsdToZar);
        return $"{AiPricing.Rand(one.Zar)} peak · {AiPricing.Rand(off.Zar)} off-peak";
    }

    private async Task<bool> LoadAsync(int id)
    {
        var provider = await db.AiProviders.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);
        if (provider is null)
            return false;
        Provider = provider;
        Prices = await db.AiPrices.AsNoTracking().Where(p => p.AiProviderId == id)
            .OrderByDescending(p => p.EffectiveFrom).ThenByDescending(p => p.Id).ToListAsync();
        var now = DateTime.UtcNow;
        CurrentId = Prices.FirstOrDefault(p => p.Model == provider.Model && p.EffectiveFrom <= now)?.Id;
        UsdToZar = await PlatformSettings.UsdToZarAsync(db);
        var userIds = Prices.Where(p => p.CreatedByUserId != null).Select(p => p.CreatedByUserId!.Value).Distinct().ToList();
        Users = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName);
        return true;
    }
}
