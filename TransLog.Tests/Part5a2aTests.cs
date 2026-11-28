using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TransLog.Web.Ai;
using TransLog.Web.Data;
using TransLog.Web.Areas.SysAdmin.Pages.Providers;

namespace TransLog.Tests;

/// <summary>Part 5a2a: AI providers with encrypted keys, prices with history, platform settings and a company's provider.</summary>
public class Part5a2aTests(TestApp app) : IClassFixture<TestApp>
{
    private const string FakeKey = "sk-test-0123456789abcdWXYZ";

    private readonly Scenario _s = new(app);

    private async Task<T> InDbAsync<T>(Func<AppDbContext, Task<T>> query)
    {
        using var scope = app.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private Task<AiProvider> DeepSeekAsync() => InDbAsync(db => db.AiProviders.AsNoTracking().SingleAsync(p => p.Key == "deepseek"));

    private static Dictionary<string, string> ProviderForm(AiProvider p, string? apiKey = null, bool active = true) => new()
    {
        ["Form.Name"] = p.Name,
        ["Form.BaseUrl"] = p.BaseUrl,
        ["Form.Model"] = p.Model,
        ["Form.OfficialDomain"] = p.OfficialDomain ?? "",
        ["Form.PricingUrl"] = p.PricingUrl ?? "",
        ["Form.IsActive"] = active ? "true" : "false",
        ["Form.ApiKey"] = apiKey ?? "",
    };

    /// <summary>A second provider for this test class, with a key, so a company can choose it.</summary>
    private async Task<int> SecondProviderAsync(Browser admin, string key)
    {
        var r = await admin.PostFormAsync("/sysadmin/providers/edit", new()
        {
            ["Form.Name"] = "DeepSeek second " + key, ["Form.Key"] = key, ["Form.BaseUrl"] = "https://api.deepseek.com",
            ["Form.Model"] = "deepseek-flash", ["Form.IsActive"] = "true", ["Form.ApiKey"] = FakeKey,
        });
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        return Scenario.IdFromRedirect(r);
    }

    [Fact]
    public async Task Only_system_admins_open_the_AI_console_pages()
    {
        var owner = await _s.CompanyWithOwnerAsync("Ai Console Co", "owner@ai-console.test");
        var deepseek = await DeepSeekAsync();
        foreach (var url in new[] { "/sysadmin/providers", $"/sysadmin/providers/edit/{deepseek.Id}", $"/sysadmin/providers/prices/{deepseek.Id}", "/sysadmin/settings" })
            Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync(url)).StatusCode);

        var admin = await _s.AdminAsync();
        var list = await (await admin.GetAsync("/sysadmin/providers")).Content.ReadAsStringAsync();
        Assert.Contains("data-provider=\"deepseek\"", list);
        Assert.Contains("href=\"/sysadmin/settings\"", list);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/sysadmin/providers/prices/{deepseek.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/sysadmin/settings")).StatusCode);
    }

    [Fact]
    public async Task Startup_seeds_DeepSeek_its_price_and_the_settings_without_a_key()
    {
        var deepseek = await DeepSeekAsync();
        Assert.Equal(AiProviderKind.DeepSeekFiles, deepseek.Kind);
        Assert.Equal("deepseek-flash", deepseek.Model);

        var price = await InDbAsync(db => AiPricing.FindAsync(db, deepseek.Id, "deepseek-flash", DateTime.UtcNow));
        Assert.NotNull(price);
        Assert.Equal(0.30m, price.InputPerM);
        Assert.Equal(0.003m, price.OffPeakCachedPerM);
        Assert.Equal("Mon-Fri", price.PeakDays);

        Assert.Equal("deepseek", await InDbAsync(db => PlatformSettings.GetAsync(db, PlatformSettings.ProviderKey)));
        Assert.True(await InDbAsync(db => PlatformSettings.UsdToZarAsync(db)) > 0);
        Assert.False(await InDbAsync(db => PlatformSettings.AutoRotateAsync(db)));
        Assert.Equal(0, await InDbAsync(db => AiSeeds.SeedAsync(db)));
    }

    [Fact]
    public async Task A_typed_key_is_stored_encrypted_shown_only_by_its_end_and_can_be_cleared()
    {
        var admin = await _s.AdminAsync();
        var id = await SecondProviderAsync(admin, "deepseek_keys");

        var stored = await InDbAsync(db => db.AiProviders.AsNoTracking().SingleAsync(p => p.Id == id));
        Assert.NotNull(stored.ApiKeyProtected);
        Assert.DoesNotContain(FakeKey, stored.ApiKeyProtected);
        Assert.Equal("WXYZ", stored.ApiKeyHint);
        using (var scope = app.Services.CreateScope())
            Assert.Equal(FakeKey, scope.ServiceProvider.GetRequiredService<AiKeys>().KeyFor(stored));

        var page = await (await admin.GetAsync($"/sysadmin/providers/edit/{id}")).Content.ReadAsStringAsync();
        Assert.Contains("WXYZ", page); // "set ••••WXYZ" (the dots are HTML-encoded)
        Assert.DoesNotContain(FakeKey, page);
        Assert.False(await InDbAsync(db => db.AuditLogs.IgnoreQueryFilters().AnyAsync(a => a.Details!.Contains(FakeKey))));

        // Saving with an empty key box keeps the key; ticking Clear removes it.
        var form = ProviderForm(stored);
        Assert.Equal(HttpStatusCode.Redirect, (await admin.PostFormAsync($"/sysadmin/providers/edit/{id}", form)).StatusCode);
        Assert.NotNull((await InDbAsync(db => db.AiProviders.AsNoTracking().SingleAsync(p => p.Id == id))).ApiKeyProtected);
        form["Form.ClearKey"] = "true";
        Assert.Equal(HttpStatusCode.Redirect, (await admin.PostFormAsync($"/sysadmin/providers/edit/{id}", form)).StatusCode);
        Assert.Null((await InDbAsync(db => db.AiProviders.AsNoTracking().SingleAsync(p => p.Id == id))).ApiKeyProtected);

        // Test without a key answers at once, without calling anyone.
        var test = await Scenario.PostToAsync(admin, $"/sysadmin/providers/edit/{id}", $"/sysadmin/providers/edit/{id}?handler=Test", new());
        Assert.Equal(HttpStatusCode.Redirect, test.StatusCode);
        Assert.Contains("No API key set.", await (await admin.GetAsync($"/sysadmin/providers/edit/{id}")).Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task The_global_provider_cannot_be_switched_off_and_bad_values_are_refused()
    {
        var admin = await _s.AdminAsync();
        var deepseek = await DeepSeekAsync();
        var r = await admin.PostFormAsync($"/sysadmin/providers/edit/{deepseek.Id}", ProviderForm(deepseek, active: false));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Contains("This is the global provider", await r.Content.ReadAsStringAsync());

        var bad = ProviderForm(deepseek);
        bad["Form.BaseUrl"] = "http://api.deepseek.com";
        r = await admin.PostFormAsync($"/sysadmin/providers/edit/{deepseek.Id}", bad);
        Assert.Contains("must start with https://", await r.Content.ReadAsStringAsync());
        Assert.True((await DeepSeekAsync()).IsActive);
    }

    [Fact]
    public async Task A_new_price_is_a_new_row_and_bad_prices_are_refused()
    {
        var admin = await _s.AdminAsync();
        var id = await SecondProviderAsync(admin, "deepseek_prices");
        var url = $"/sysadmin/providers/prices/{id}";

        var r = await admin.PostFormAsync(url, new()
        {
            ["Form.Model"] = "deepseek-flash", ["Form.From"] = "2026-01-01",
            ["Form.Input"] = "0.30", ["Form.Cached"] = "0,006", ["Form.Output"] = "1.20",
            ["Form.OffPeakInput"] = "0.15", ["Form.OffPeakCached"] = "0.003", ["Form.OffPeakOutput"] = "0.60",
            ["Form.PeakHours"] = "01:00-04:00,06:00-10:00", ["Form.PeakDays"] = "Mon-Fri",
        });
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        r = await admin.PostFormAsync(url, new()
        {
            ["Form.Model"] = "deepseek-flash", ["Form.From"] = "2026-06-01",
            ["Form.Input"] = "0.40", ["Form.Cached"] = "0.01", ["Form.Output"] = "1.50",
        });
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);

        Assert.Equal(2, await InDbAsync(db => db.AiPrices.CountAsync(p => p.AiProviderId == id)));
        var march = await InDbAsync(db => AiPricing.FindAsync(db, id, "deepseek-flash", new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc)));
        var july = await InDbAsync(db => AiPricing.FindAsync(db, id, "deepseek-flash", new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc)));
        Assert.Equal(0.006m, march!.CachedInputPerM);
        Assert.Equal(0.40m, july!.InputPerM);
        Assert.Null(july.OffPeakInputPerM);
        Assert.Null(await InDbAsync(db => AiPricing.FindAsync(db, id, "deepseek-flash", new DateTime(2025, 12, 1, 0, 0, 0, DateTimeKind.Utc))));
        Assert.True(await InDbAsync(db => db.AuditLogs.IgnoreQueryFilters().AnyAsync(a => a.Action == "AiPriceAdded")));

        r = await admin.PostFormAsync(url, new()
        {
            ["Form.Model"] = "deepseek-flash", ["Form.From"] = "2026-07-01",
            ["Form.Input"] = "0", ["Form.Cached"] = "0.01", ["Form.Output"] = "1.50", ["Form.OffPeakInput"] = "0.1",
        });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var html = await r.Content.ReadAsStringAsync();
        Assert.Contains("Input: enter a price", html);
        Assert.Contains("all three off-peak prices", html);
        Assert.Equal(2, await InDbAsync(db => db.AiPrices.CountAsync(p => p.AiProviderId == id)));
    }

    [Fact]
    public async Task Platform_settings_save_the_rate_with_an_audit_entry_and_refuse_nonsense()
    {
        var admin = await _s.AdminAsync();
        var r = await admin.PostFormAsync("/sysadmin/settings", new()
        {
            ["Provider"] = "deepseek", ["UsdToZar"] = "17,25", ["AutoRotate"] = "false",
        });
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.Equal(17.25m, await InDbAsync(db => PlatformSettings.UsdToZarAsync(db)));
        Assert.True(await InDbAsync(db => db.AuditLogs.IgnoreQueryFilters()
            .AnyAsync(a => a.Action == "PlatformSettingChanged" && a.Details!.Contains("Ai.UsdToZar") && a.Details.Contains("17.25"))));

        r = await admin.PostFormAsync("/sysadmin/settings", new() { ["Provider"] = "deepseek", ["UsdToZar"] = "abc" });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Contains("rand price of one US dollar", await r.Content.ReadAsStringAsync());

        r = await admin.PostFormAsync("/sysadmin/settings", new() { ["Provider"] = "nobody", ["UsdToZar"] = "17.25" });
        Assert.Contains("Choose a provider that is on and has an API key", await r.Content.ReadAsStringAsync());
        Assert.Equal("deepseek", await InDbAsync(db => PlatformSettings.GetAsync(db, PlatformSettings.ProviderKey)));
    }

    [Fact]
    public async Task A_company_can_get_its_own_provider_only_when_it_has_a_key()
    {
        await _s.CompanyWithOwnerAsync("Ai Provider Co", "owner@ai-provider.test");
        var companyId = await InDbAsync(db => db.Companies.Where(c => c.Name == "Ai Provider Co").Select(c => c.Id).SingleAsync());
        var admin = await _s.AdminAsync();
        var second = await SecondProviderAsync(admin, "deepseek_company");
        var deepseek = await DeepSeekAsync();
        var page = $"/sysadmin/companies/edit/{companyId}";

        // DeepSeek has no key in tests: it cannot be chosen.
        await Scenario.PostToAsync(admin, page, page + "?handler=AiProvider", new() { ["AiProviderId"] = deepseek.Id.ToString() });
        Assert.Null(await InDbAsync(db => db.Companies.Where(c => c.Id == companyId).Select(c => c.AiProviderId).SingleAsync()));

        var r = await Scenario.PostToAsync(admin, page, page + "?handler=AiProvider", new() { ["AiProviderId"] = second.ToString() });
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.Equal(second, await InDbAsync(db => db.Companies.Where(c => c.Id == companyId).Select(c => c.AiProviderId).SingleAsync()));
        Assert.True(await InDbAsync(db => db.AuditLogs.IgnoreQueryFilters().AnyAsync(a => a.Action == "CompanyAiProviderChanged" && a.CompanyId == companyId)));

        using (var scope = app.Services.CreateScope())
        {
            var chosen = await scope.ServiceProvider.GetRequiredService<AiProviders>().ForCompanyAsync(companyId);
            Assert.Equal(second, chosen.Provider?.Id);
            Assert.True(chosen.FromCompany);
        }

        // A provider in use by a company cannot be switched off.
        var stored = await InDbAsync(db => db.AiProviders.AsNoTracking().SingleAsync(p => p.Id == second));
        var off = await admin.PostFormAsync($"/sysadmin/providers/edit/{second}", ProviderForm(stored, active: false));
        Assert.Contains("Some companies use this provider", await off.Content.ReadAsStringAsync());

        // Back to global: DeepSeek without a key gives no usable provider (the slip will wait for review).
        await Scenario.PostToAsync(admin, page, page + "?handler=AiProvider", new() { ["AiProviderId"] = "" });
        using (var scope = app.Services.CreateScope())
        {
            var chosen = await scope.ServiceProvider.GetRequiredService<AiProviders>().ForCompanyAsync(companyId);
            Assert.Null(chosen.Provider);
            Assert.False(chosen.FromCompany);
        }
    }
}

/// <summary>Part 5a2a rules that need no database: peak hours, cost, number reading and the Test answer.</summary>
public class AiPricingTests
{
    private static AiPrice DeepSeek() => new()
    {
        Id = 1, Model = "deepseek-flash",
        InputPerM = 0.30m, CachedInputPerM = 0.006m, OutputPerM = 1.20m,
        OffPeakInputPerM = 0.15m, OffPeakCachedPerM = 0.003m, OffPeakOutputPerM = 0.60m,
        PeakHoursUtc = "01:00-04:00,06:00-10:00", PeakDays = "Mon-Fri",
    };

    private static DateTime Utc(int day, int hour, int minute = 0) => new(2026, 11, day, hour, minute, 0, DateTimeKind.Utc);

    [Fact]
    public void Peak_follows_the_hours_and_days_in_utc()
    {
        var p = DeepSeek();
        Assert.True(AiPricing.IsPeak(p, Utc(30, 7)));         // Monday 07:00 UTC
        Assert.True(AiPricing.IsPeak(p, Utc(30, 1)));         // start included
        Assert.False(AiPricing.IsPeak(p, Utc(30, 4)));        // end not included
        Assert.False(AiPricing.IsPeak(p, Utc(30, 12)));
        Assert.False(AiPricing.IsPeak(p, Utc(28, 7)));        // Saturday
        Assert.False(AiPricing.IsPeak(new AiPrice { InputPerM = 1, OutputPerM = 1, PeakHoursUtc = "01:00-04:00" }, Utc(30, 2))); // one price all day

        var night = new AiPrice { InputPerM = 1, CachedInputPerM = 1, OutputPerM = 1, OffPeakInputPerM = 0.5m, OffPeakCachedPerM = 0.5m, OffPeakOutputPerM = 0.5m, PeakHoursUtc = "22:00-02:00" };
        Assert.True(AiPricing.IsPeak(night, Utc(28, 23)));
        Assert.True(AiPricing.IsPeak(night, Utc(29, 1)));
        Assert.False(AiPricing.IsPeak(night, Utc(29, 3)));
    }

    [Fact]
    public void Cost_counts_cached_tokens_inside_the_input_and_converts_to_rand()
    {
        var tokens = new AiTokens(Input: 2400, Cached: 1000, Output: 600);
        var peak = AiPricing.Cost(DeepSeek(), tokens, Utc(30, 7), 16.50m);
        // 1 400 × 0.30 + 1 000 × 0.006 + 600 × 1.20 = 1 146 per million = $0.001146
        Assert.True(peak.Peak);
        Assert.Equal(0.001146m, peak.Usd);
        Assert.Equal(Math.Round(0.001146m * 16.50m, 6), peak.Zar);

        var off = AiPricing.Cost(DeepSeek(), tokens, Utc(30, 12), 16.50m);
        Assert.False(off.Peak);
        Assert.Equal(0.000573m, off.Usd);
    }

    [Fact]
    public void Hours_days_and_numbers_are_read_strictly()
    {
        Assert.Equal(5, AiPricing.ParseDays("Mon-Fri")!.Count);
        Assert.Equal(new[] { 0, 6 }, AiPricing.ParseDays("Sat-Sun")!.Order());
        Assert.Null(AiPricing.ParseDays("Funday"));
        Assert.Equal(2, AiPricing.ParseHours("01:00-04:00,06:00-10:00")!.Count);
        Assert.Null(AiPricing.ParseHours("1-4"));
        Assert.Null(AiPricing.ParseHours("25:00-26:00"));
        Assert.Equal(0.006m, AiPricing.ParseNumber("0,006"));
        Assert.Equal(16.5m, AiPricing.ParseNumber(" 16.50 "));
        Assert.Null(AiPricing.ParseNumber("-1"));
        Assert.Equal("0.006", AiPricing.Show(0.006000m));
    }

    [Fact]
    public void The_model_list_answer_is_checked_for_the_model_and_images()
    {
        const string list = """{"object":"list","data":[{"id":"deepseek-flash","object":"model","owned_by":"deepseek","input_modalities":["text","image"]},{"id":"deepseek-v4-pro","object":"model","owned_by":"deepseek","input_modalities":["text"]}]}""";
        Assert.True(AiProviders.ReadModels(list, "deepseek-flash").Ok);
        var textOnly = AiProviders.ReadModels(list, "deepseek-v4-pro");
        Assert.False(textOnly.Ok);
        Assert.Contains("does not read images", textOnly.Message);
        Assert.Contains("not in the provider's list", AiProviders.ReadModels(list, "deepseek-gone").Message);
        Assert.False(AiProviders.ReadModels("not json", "x").Ok);
    }

    [Fact]
    public void The_price_form_needs_all_or_no_off_peak_prices()
    {
        var (ok, _) = PricesModel.Read(new PriceForm { Model = "m", From = "2026-11-28", Input = "0.3", Cached = "0", Output = "1.2" }, 1);
        Assert.NotNull(ok);
        Assert.Null(ok.OffPeakInputPerM);
        var (bad, errors) = PricesModel.Read(new PriceForm { Model = "m", From = "2026-11-28", Input = "0.3", Cached = "0", Output = "1.2", OffPeakInput = "0.1", OffPeakCached = "0", OffPeakOutput = "0.6", PeakHours = "nonsense" }, 1);
        Assert.Null(bad);
        Assert.Contains(errors, e => e.Contains("Peak hours", StringComparison.Ordinal));
    }
}
