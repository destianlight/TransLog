using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TransLog.Web.Ai;
using TransLog.Web.Data;
using TransLog.Web.Security;

namespace TransLog.Web.Areas.SysAdmin.Pages;

/// <summary>Part 5a2a: app-wide AI values. Every change goes to the audit log with the old value.</summary>
public class SettingsModel(AppDbContext db, AiProviders providers, AuditLogger audit) : PageModel
{
    [BindProperty]
    public string? Provider { get; set; }

    [BindProperty]
    public string? UsdToZar { get; set; }

    [BindProperty]
    public bool AutoRotate { get; set; }

    /// <summary>Providers that can be chosen (on, with a key), plus the current one if it cannot.</summary>
    public List<(string Key, string Name, bool Usable)> Choices { get; private set; } = [];

    public DateTime? RateUpdatedAt { get; private set; }

    public async Task OnGetAsync()
    {
        Provider = await PlatformSettings.GetAsync(db, PlatformSettings.ProviderKey) ?? PlatformSettings.DefaultProvider;
        UsdToZar = AiPricing.Show(await PlatformSettings.UsdToZarAsync(db));
        AutoRotate = await PlatformSettings.AutoRotateAsync(db);
        await LoadAsync();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        ModelState.Clear();
        var current = await PlatformSettings.GetAsync(db, PlatformSettings.ProviderKey) ?? PlatformSettings.DefaultProvider;
        var usable = await providers.UsableAsync();
        var key = (Provider ?? "").Trim();
        if (key != current && usable.All(p => p.Key != key))
            ModelState.AddModelError(string.Empty, "Choose a provider that is on and has an API key.");

        var rate = AiPricing.ParseNumber(UsdToZar);
        if (rate is null || rate < 5 || rate > 100)
            ModelState.AddModelError(string.Empty, "Enter the rand price of one US dollar, for example 16.50.");

        if (!ModelState.IsValid)
        {
            await LoadAsync();
            return Page();
        }

        var userId = User.GetUserId();
        var changed = await PlatformSettings.SetAsync(db, audit, userId, PlatformSettings.ProviderKey, key);
        changed |= await PlatformSettings.SetAsync(db, audit, userId, PlatformSettings.UsdToZarKey, AiPricing.Show(rate));
        changed |= await PlatformSettings.SetAsync(db, audit, userId, PlatformSettings.AutoRotateKey, AutoRotate ? "true" : "false");
        await db.SaveChangesAsync();

        TempData["Message"] = changed ? "Saved." : "Nothing changed.";
        return RedirectToPage();
    }

    private async Task LoadAsync()
    {
        var usable = await providers.UsableAsync();
        Choices = usable.Select(p => (p.Key, p.Name, true)).ToList();
        if (Provider is string current && Choices.All(c => c.Key != current))
        {
            var p = await db.AiProviders.AsNoTracking().FirstOrDefaultAsync(x => x.Key == current);
            Choices.Insert(0, (current, (p?.Name ?? current) + " (off or no key: slips cannot be read)", false));
        }
        RateUpdatedAt = await db.PlatformSettings.AsNoTracking().Where(s => s.Key == PlatformSettings.UsdToZarKey)
            .Select(s => (DateTime?)s.UpdatedAt).FirstOrDefaultAsync();
    }
}
