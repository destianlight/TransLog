using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using TransLog.Web.Data;
using TransLog.Web.Security;

namespace TransLog.Web.Ai;

/// <summary>Where a provider's key comes from.</summary>
public enum AiKeySource
{
    None = 0,
    Console = 1,

    /// <summary>Ai:Providers:&lt;key&gt;:ApiKey in the server settings; it wins over the typed key.</summary>
    ServerSettings = 2,
}

/// <summary>
/// API keys for AI providers (Part 5a2a): typed in the console and stored encrypted with ASP.NET Data Protection
/// (the same keys folder as the login, App_Data/keys, purpose TransLog.AiKeys). Never shown again, never logged.
/// </summary>
public class AiKeys(IDataProtectionProvider protection, IConfiguration config)
{
    private readonly IDataProtector _protector = protection.CreateProtector("TransLog.AiKeys");

    public void Set(AiProvider provider, string apiKey)
    {
        apiKey = apiKey.Trim();
        provider.ApiKeyProtected = _protector.Protect(apiKey);
        provider.ApiKeyHint = apiKey.Length >= 4 ? apiKey[^4..] : apiKey;
    }

    public static void Clear(AiProvider provider)
    {
        provider.ApiKeyProtected = null;
        provider.ApiKeyHint = null;
    }

    public string? ServerKey(AiProvider provider)
    {
        var key = config[$"Ai:Providers:{provider.Key}:ApiKey"];
        return string.IsNullOrWhiteSpace(key) ? null : key.Trim();
    }

    public AiKeySource SourceOf(AiProvider provider) =>
        ServerKey(provider) is not null ? AiKeySource.ServerSettings
        : provider.ApiKeyProtected is not null ? AiKeySource.Console
        : AiKeySource.None;

    /// <summary>The key to call the provider with, or null. A key that cannot be decrypted (keys folder lost) counts as none.</summary>
    public string? KeyFor(AiProvider provider)
    {
        if (ServerKey(provider) is string server)
            return server;
        if (provider.ApiKeyProtected is null)
            return null;
        try
        {
            return _protector.Unprotect(provider.ApiKeyProtected);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return null;
        }
    }

    /// <summary>"set ••••abcd", "from server settings" or "no key".</summary>
    public string Describe(AiProvider provider) => SourceOf(provider) switch
    {
        AiKeySource.ServerSettings => "from server settings",
        AiKeySource.Console => "set ••••" + provider.ApiKeyHint,
        _ => "no key",
    };
}

/// <summary>The result of the Test button.</summary>
public sealed record AiProviderTest(bool Ok, string Message);

/// <summary>Which provider reads a company's slips, and the Test button (Part 5a2a).</summary>
public class AiProviders(AppDbContext db, AiKeys keys, IHttpClientFactory http)
{
    /// <summary>Active providers that have a key: the only ones that can be chosen.</summary>
    public async Task<List<AiProvider>> UsableAsync()
    {
        var all = await db.AiProviders.AsNoTracking().Where(p => p.IsActive).OrderBy(p => p.Name).ToListAsync();
        return all.Where(p => keys.SourceOf(p) != AiKeySource.None).ToList();
    }

    public bool IsUsable(AiProvider p) => p.IsActive && keys.SourceOf(p) != AiKeySource.None;

    /// <summary>
    /// The provider for a company: its own choice, else the global one (Platform settings Ai.Provider).
    /// Null when that provider is switched off or has no key (the slip then waits for review in 5a2b).
    /// </summary>
    public async Task<(AiProvider? Provider, bool FromCompany)> ForCompanyAsync(int companyId)
    {
        var chosen = await db.Companies.AsNoTracking().Where(c => c.Id == companyId).Select(c => c.AiProviderId).FirstOrDefaultAsync();
        AiProvider? provider;
        if (chosen is int id)
            provider = await db.AiProviders.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);
        else
        {
            var key = await PlatformSettings.GetAsync(db, PlatformSettings.ProviderKey) ?? PlatformSettings.DefaultProvider;
            provider = await db.AiProviders.AsNoTracking().FirstOrDefaultAsync(p => p.Key == key);
        }
        return (provider is not null && IsUsable(provider) ? provider : null, chosen is not null);
    }

    /// <summary>
    /// One free call (GET /models; no tokens used): checks the key works, the model exists and, when the provider
    /// says so, that it reads images.
    /// </summary>
    public async Task<AiProviderTest> TestAsync(AiProvider provider, CancellationToken ct = default)
    {
        var apiKey = keys.KeyFor(provider);
        if (apiKey is null)
            return new(false, provider.ApiKeyProtected is null ? "No API key set." : "The stored key cannot be read (the keys folder changed). Type the key again.");
        if (provider.Kind != AiProviderKind.DeepSeekFiles)
            return new(false, "This kind of provider is not built yet.");

        try
        {
            var client = http.CreateClient("ai");
            using var request = new HttpRequestMessage(HttpMethod.Get, provider.BaseUrl.TrimEnd('/') + "/models");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            using var response = await client.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                var code = ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture);
                return new(false, response.StatusCode == System.Net.HttpStatusCode.Unauthorized
                    ? "The key was refused (401). Check it on the provider's site and type it again."
                    : $"The provider answered {code}: {Short(body)}");
            }
            return ReadModels(body, provider.Model);
        }
        catch (TaskCanceledException)
        {
            return new(false, "No answer within 20 seconds.");
        }
        catch (HttpRequestException ex)
        {
            return new(false, "Could not reach the provider: " + Short(ex.Message));
        }
    }

    /// <summary>Reads a /models answer ({"data":[{"id":…,"input_modalities":[…]}]}) and checks the model.</summary>
    public static AiProviderTest ReadModels(string json, string model)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                return new(false, "The key works, but the model list was not understood.");

            var ids = new List<string>();
            foreach (var m in data.EnumerateArray())
            {
                if (!m.TryGetProperty("id", out var idEl) || idEl.GetString() is not string id)
                    continue;
                ids.Add(id);
                if (!string.Equals(id, model, StringComparison.Ordinal))
                    continue;
                if (m.TryGetProperty("input_modalities", out var mods) && mods.ValueKind == JsonValueKind.Array
                    && !mods.EnumerateArray().Any(x => x.GetString() == "image"))
                    return new(false, $"The key works and {model} exists, but the provider says it does not read images.");
                return new(true, $"The key works and {model} is available.");
            }
            return new(false, $"The key works, but {model} is not in the provider's list ({string.Join(", ", ids.Take(8))}).");
        }
        catch (JsonException)
        {
            return new(false, "The key works, but the model list was not understood.");
        }
    }

    private static string Short(string text) => text.Length <= 150 ? text : text[..150] + "…";
}

/// <summary>App-wide values managed on Platform settings (Part 5a2a). Every change goes to the audit log with the old value.</summary>
public static class PlatformSettings
{
    public const string ProviderKey = "Ai.Provider";
    public const string UsdToZarKey = "Ai.UsdToZar";
    public const string AutoRotateKey = "Ai.AutoRotate";

    public const string DefaultProvider = "deepseek";

    /// <summary>Starting value only (late November 2026, about R16.50); the SysAdmin keeps it up to date.</summary>
    public const decimal DefaultUsdToZar = 16.50m;

    public static async Task<string?> GetAsync(AppDbContext db, string key) =>
        await db.PlatformSettings.AsNoTracking().Where(s => s.Key == key).Select(s => s.Value).FirstOrDefaultAsync();

    public static async Task<decimal> UsdToZarAsync(AppDbContext db) =>
        AiPricing.ParseNumber(await GetAsync(db, UsdToZarKey)) is decimal n && n > 0 ? n : DefaultUsdToZar;

    public static async Task<bool> AutoRotateAsync(AppDbContext db) =>
        string.Equals(await GetAsync(db, AutoRotateKey), "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>Sets a value and writes "Key: old → new" to the audit log; returns false when nothing changed.</summary>
    public static async Task<bool> SetAsync(AppDbContext db, AuditLogger audit, int? userId, string key, string value)
    {
        var row = await db.PlatformSettings.FirstOrDefaultAsync(s => s.Key == key);
        var old = row?.Value;
        if (old == value)
            return false;
        if (row is null)
        {
            row = new PlatformSetting { Key = key };
            db.PlatformSettings.Add(row);
        }
        row.Value = value;
        row.UpdatedAt = DateTime.UtcNow;
        row.UpdatedByUserId = userId;
        audit.Add("PlatformSettingChanged", $"{key}: {old ?? "(none)"} → {value}");
        return true;
    }
}

/// <summary>The DeepSeek provider, its price row and the platform settings, added at startup only when missing.</summary>
public static class AiSeeds
{
    public static async Task<int> SeedAsync(AppDbContext db)
    {
        var added = 0;
        var now = DateTime.UtcNow;
        var deepseek = await db.AiProviders.FirstOrDefaultAsync(p => p.Key == PlatformSettings.DefaultProvider);
        if (deepseek is null)
        {
            deepseek = new AiProvider
            {
                Key = PlatformSettings.DefaultProvider,
                Name = "DeepSeek",
                Kind = AiProviderKind.DeepSeekFiles,
                BaseUrl = "https://api.deepseek.com",
                Model = "deepseek-flash",
                OfficialDomain = "deepseek.com",
                PricingUrl = "https://api-docs.deepseek.com/quick_start/pricing",
                IsActive = true,
                UpdatedAt = now,
            };
            db.AiProviders.Add(deepseek);
            await db.SaveChangesAsync();
            added++;
        }

        if (!await db.AiPrices.AnyAsync(p => p.AiProviderId == deepseek.Id))
        {
            // DeepSeek's prices since 10 Sep 2026 (checked 28 Nov 2026), US dollars per 1M tokens.
            db.AiPrices.Add(new AiPrice
            {
                AiProviderId = deepseek.Id,
                Model = deepseek.Model,
                EffectiveFrom = new DateTime(2026, 9, 10, 0, 0, 0, DateTimeKind.Utc),
                InputPerM = 0.30m, CachedInputPerM = 0.006m, OutputPerM = 1.20m,
                OffPeakInputPerM = 0.15m, OffPeakCachedPerM = 0.003m, OffPeakOutputPerM = 0.60m,
                PeakHoursUtc = "01:00-04:00,06:00-10:00",
                PeakDays = "Mon-Fri",
                Source = AiPriceSource.Manual,
                CreatedAt = now,
            });
            added++;
        }

        foreach (var (key, value) in new[]
                 {
                     (PlatformSettings.ProviderKey, PlatformSettings.DefaultProvider),
                     (PlatformSettings.UsdToZarKey, AiPricing.Show(PlatformSettings.DefaultUsdToZar)),
                     (PlatformSettings.AutoRotateKey, "false"),
                 })
        {
            if (await db.PlatformSettings.AnyAsync(s => s.Key == key))
                continue;
            db.PlatformSettings.Add(new PlatformSetting { Key = key, Value = value, UpdatedAt = now });
            added++;
        }
        await db.SaveChangesAsync();
        return added;
    }
}
