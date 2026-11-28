using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TransLog.Web.Ai;
using TransLog.Web.Data;
using TransLog.Web.Security;

namespace TransLog.Web.Areas.SysAdmin.Pages.Providers;

/// <summary>What the provider form sends. Strings only: the page checks them itself.</summary>
public sealed class ProviderForm
{
    public string? Name { get; set; }
    public string? Key { get; set; }
    public string? BaseUrl { get; set; }
    public string? Model { get; set; }
    public string? OfficialDomain { get; set; }
    public string? PricingUrl { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>Typed key; empty = keep the stored one.</summary>
    public string? ApiKey { get; set; }
    public bool ClearKey { get; set; }
}

/// <summary>Part 5a2a: add or edit an AI provider, type or clear its API key, and Test it.</summary>
public partial class EditModel(AppDbContext db, AiKeys keys, AiProviders providers, AuditLogger audit) : PageModel
{
    [BindProperty]
    public ProviderForm Form { get; set; } = new();

    public AiProvider? Provider { get; private set; }

    public string KeyStatus { get; private set; } = "no key";

    public bool KeyFromServer { get; private set; }

    public async Task<IActionResult> OnGetAsync(int? id)
    {
        if (id is int existing)
        {
            Provider = await db.AiProviders.FirstOrDefaultAsync(p => p.Id == existing);
            if (Provider is null)
                return NotFound();
            Form = new ProviderForm
            {
                Name = Provider.Name, Key = Provider.Key, BaseUrl = Provider.BaseUrl, Model = Provider.Model,
                OfficialDomain = Provider.OfficialDomain, PricingUrl = Provider.PricingUrl, IsActive = Provider.IsActive,
            };
            Load();
        }
        else
            Form = new ProviderForm { BaseUrl = "https://api.deepseek.com", Model = "deepseek-flash", IsActive = true };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int? id)
    {
        ModelState.Clear();
        AiProvider provider;
        var isNew = id is null;
        if (id is int existing)
        {
            var found = await db.AiProviders.FirstOrDefaultAsync(p => p.Id == existing);
            if (found is null)
                return NotFound();
            provider = found;
        }
        else
            provider = new AiProvider { Kind = AiProviderKind.DeepSeekFiles };

        var errors = await CheckAsync(provider, isNew);
        if (errors.Count > 0)
        {
            foreach (var e in errors)
                ModelState.AddModelError(string.Empty, e);
            Provider = isNew ? null : provider;
            if (Provider is not null)
                Load();
            return Page();
        }

        if (isNew)
        {
            provider.Key = Form.Key!.Trim().ToLowerInvariant();
            db.AiProviders.Add(provider);
        }
        provider.Name = Form.Name!.Trim();
        provider.BaseUrl = Form.BaseUrl!.Trim().TrimEnd('/');
        provider.Model = Form.Model!.Trim();
        provider.OfficialDomain = Blank(Form.OfficialDomain)?.ToLowerInvariant();
        provider.PricingUrl = Blank(Form.PricingUrl);
        provider.IsActive = Form.IsActive;
        provider.UpdatedAt = DateTime.UtcNow;
        provider.UpdatedByUserId = User.GetUserId();

        var keyNote = "";
        if (Blank(Form.ApiKey) is string typed)
        {
            keys.Set(provider, typed);
            keyNote = ", API key set";
        }
        else if (Form.ClearKey && provider.ApiKeyProtected is not null)
        {
            AiKeys.Clear(provider);
            keyNote = ", API key cleared";
        }

        // Never the key itself, only that it changed.
        audit.Add(isNew ? "AiProviderCreated" : "AiProviderUpdated",
            $"{provider.Key} ({provider.Name}) {provider.Model}{(provider.IsActive ? "" : ", off")}{keyNote}");
        await db.SaveChangesAsync();

        TempData["Message"] = "Saved" + keyNote + ".";
        return RedirectToPage(new { id = provider.Id });
    }

    /// <summary>One free call to the provider with the stored key (GET /models).</summary>
    public async Task<IActionResult> OnPostTestAsync(int id)
    {
        var provider = await db.AiProviders.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);
        if (provider is null)
            return NotFound();
        var result = await providers.TestAsync(provider, HttpContext.RequestAborted);
        TempData[result.Ok ? "Message" : "TestError"] = "Test: " + result.Message;
        return RedirectToPage(new { id });
    }

    private async Task<List<string>> CheckAsync(AiProvider provider, bool isNew)
    {
        var errors = new List<string>();
        if (isNew)
        {
            var key = (Form.Key ?? "").Trim().ToLowerInvariant();
            if (!KeyRegex().IsMatch(key))
                errors.Add("The key must start with a letter and have 2 to 40 lower case letters, digits or _.");
            else if (await db.AiProviders.AnyAsync(p => p.Key == key))
                errors.Add("A provider with this key already exists.");
        }
        if (string.IsNullOrWhiteSpace(Form.Name) || Form.Name.Trim().Length > 100)
            errors.Add("Enter a name (at most 100 characters).");
        if (!Uri.TryCreate((Form.BaseUrl ?? "").Trim(), UriKind.Absolute, out var url) || url.Scheme != Uri.UriSchemeHttps)
            errors.Add("The base URL must start with https://, for example https://api.deepseek.com.");
        if (string.IsNullOrWhiteSpace(Form.Model) || Form.Model.Trim().Length > 100)
            errors.Add("Enter the model name (at most 100 characters).");
        if (Blank(Form.OfficialDomain) is string domain && (domain.Length > 100 || domain.Contains('/') || !domain.Contains('.')))
            errors.Add("The official domain is a plain domain such as deepseek.com.");
        if (Blank(Form.PricingUrl) is string pricing && (pricing.Length > 300 || !Uri.TryCreate(pricing, UriKind.Absolute, out var pricingUri) || pricingUri.Scheme != Uri.UriSchemeHttps))
            errors.Add("The pricing page must be a full https:// address.");
        if (Blank(Form.ApiKey) is string typedKey && (typedKey.Length < 8 || typedKey.Length > 500 || typedKey.Any(char.IsWhiteSpace)))
            errors.Add("That API key does not look right (8 to 500 characters, no spaces).");

        if (!isNew && provider.IsActive && !Form.IsActive)
        {
            var global = await PlatformSettings.GetAsync(db, PlatformSettings.ProviderKey) ?? PlatformSettings.DefaultProvider;
            if (global == provider.Key)
                errors.Add("This is the global provider: choose another one on Platform settings before switching it off.");
            else if (await db.Companies.AnyAsync(c => c.AiProviderId == provider.Id))
                errors.Add("Some companies use this provider: set them back to the global one first.");
        }
        return errors;
    }

    private void Load()
    {
        KeyStatus = keys.Describe(Provider!);
        KeyFromServer = keys.SourceOf(Provider!) == AiKeySource.ServerSettings;
    }

    private static string? Blank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    [GeneratedRegex("^[a-z][a-z0-9_]{1,39}$")]
    private static partial Regex KeyRegex();
}
