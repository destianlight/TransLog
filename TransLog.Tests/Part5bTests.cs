using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TransLog.Web.Captures;
using TransLog.Web.Data;
using Capture = TransLog.Web.Data.Capture; // System.Text.RegularExpressions also has a Capture

namespace TransLog.Tests;

/// <summary>Part 5b helpers: a driver with a login, an office upload for a driver, database access.</summary>
public static class Part5bFlow
{
    private static readonly Regex Token = new("name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");

    public static async Task<T> InDbAsync<T>(TestApp app, Func<AppDbContext, Task<T>> query)
    {
        using var scope = app.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    /// <summary>The Owner adds a driver and gives them a login (password "driver1"); returns the driver id.</summary>
    public static async Task<int> AddDriverWithLoginAsync(TestApp app, Browser owner, string first, string surname, string cell)
    {
        var fields = new Dictionary<string, string> { ["Form.FirstName"] = first, ["Form.Surname"] = surname, ["Form.Cell"] = cell };
        Assert.Equal(HttpStatusCode.Redirect, (await owner.PostFormAsync("/company/drivers/create", fields)).StatusCode);
        var id = await InDbAsync(app, db => db.Drivers.IgnoreQueryFilters().Where(d => d.Surname == surname).Select(d => d.Id).SingleAsync());
        var give = await Scenario.PostToAsync(owner, $"/company/drivers/edit/{id}", $"/company/drivers/edit/{id}?handler=GiveLogin",
            new() { ["password"] = "driver1" });
        Assert.Equal(HttpStatusCode.Redirect, give.StatusCode);
        return id;
    }

    /// <summary>One photo through office capture for a driver (as page-office-capture.js sends it); returns the capture id.</summary>
    public static async Task<int> OfficeUploadAsync(Browser owner, int? driverId)
    {
        var page = await owner.GetAsync("/company/capture");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var token = Token.Match(await page.Content.ReadAsStringAsync()).Groups[1].Value;
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(Guid.NewGuid().ToString()), "clientKey");
        if (driverId is int d)
            form.Add(new StringContent(d.ToString(CultureInfo.InvariantCulture)), "driverId");
        var photo = new ByteArrayContent(TestJpeg.Make(1200, 1600));
        photo.Headers.ContentType = new("image/jpeg");
        form.Add(photo, "photo", "photo.jpg");
        var request = new HttpRequestMessage(HttpMethod.Post, "/company/capture?handler=Upload") { Content = form };
        request.Headers.Add("RequestVerificationToken", token);
        var r = await owner.Client.SendAsync(request);
        var body = await r.Content.ReadAsStringAsync();
        Assert.True(r.StatusCode == HttpStatusCode.OK, body);
        return int.Parse(Regex.Match(body, "\"id\"\\s*:\\s*(\\d+)").Groups[1].Value, CultureInfo.InvariantCulture);
    }
}

/// <summary>Keeps cookies for a test client that is not made by WebApplicationFactory (needed for the SignalR client).</summary>
public sealed class CookieJar(CookieContainer jar) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri!;
        var header = jar.GetCookieHeader(uri);
        if (header.Length > 0)
            request.Headers.TryAddWithoutValidation("Cookie", header);
        var response = await base.SendAsync(request, cancellationToken);
        if (response.Headers.TryGetValues("Set-Cookie", out var values))
        {
            foreach (var value in values)
                jar.SetCookies(uri, value);
        }
        return response;
    }
}

/// <summary>A logged-in "phone" with the live connection open (long polling through the test server), recording messages.</summary>
public sealed class LivePhone : IAsyncDisposable
{
    public Browser Browser { get; }

    public HubConnection Hub { get; }

    public ConcurrentQueue<(string Kind, JsonElement Message)> Got { get; } = new();

    private LivePhone(TestServer server)
    {
        var jar = new CookieContainer();
        Browser = new Browser(new HttpClient(new CookieJar(jar) { InnerHandler = server.CreateHandler() }) { BaseAddress = new Uri("https://localhost") });
        Hub = new HubConnectionBuilder()
            .WithUrl("https://localhost" + CaptureHub.Path, o =>
            {
                o.Transports = HttpTransportType.LongPolling;
                o.HttpMessageHandlerFactory = _ => new CookieJar(jar) { InnerHandler = server.CreateHandler() };
            })
            .Build();
        Hub.On<JsonElement>(CaptureLive.Changed, m => Got.Enqueue((CaptureLive.Changed, m.Clone())));
        Hub.On<JsonElement>(CaptureLive.Added, m => Got.Enqueue((CaptureLive.Added, m.Clone())));
    }

    public static async Task<LivePhone> LoginAsync(TestServer server, string login, string password)
    {
        var phone = new LivePhone(server);
        Assert.Equal(HttpStatusCode.Redirect, (await phone.Browser.LoginAsync(login, password)).StatusCode);
        await phone.Hub.StartAsync();
        return phone;
    }

    public bool Has(string kind, int id, Func<JsonElement, bool>? when = null) =>
        Got.Any(g => g.Kind == kind && g.Message.GetProperty("id").GetInt32() == id && (when is null || when(g.Message)));

    public async Task WaitForAsync(string kind, int id, Func<JsonElement, bool>? when = null)
    {
        var until = DateTime.UtcNow.AddSeconds(30);
        while (!Has(kind, id, when))
        {
            Assert.True(DateTime.UtcNow < until, $"No live message '{kind}' for capture {id} (got {Got.Count} messages).");
            await Task.Delay(200);
        }
    }

    public async ValueTask DisposeAsync() => await Hub.DisposeAsync();
}

/// <summary>Part 5b: live messages from the worker (fake AI) reach the driver and his company, nobody else.</summary>
public class Part5bLiveTests(FakeAiWorkerApp f) : IClassFixture<FakeAiWorkerApp>
{
    private readonly Scenario _s = new(f.App);

    [Fact]
    public async Task Live_messages_go_to_the_driver_and_his_company_only()
    {
        f.Fake.Reset();
        f.Fake.FuelSlip();
        var ownerA = await _s.CompanyWithOwnerAsync("Live Co A", "owner@live-a.test");
        await _s.CompanyWithOwnerAsync("Live Co B", "owner@live-b.test");
        var driverId = await Part5bFlow.AddDriverWithLoginAsync(f.App, ownerA, "Thabo", "Liveone", "0828880001");

        // Everything below runs on the app with the worker and the fake AI.
        await using var office = await LivePhone.LoginAsync(f.Ai.Server, "owner@live-a.test", "owner1");
        await using var driver = await LivePhone.LoginAsync(f.Ai.Server, "0828880001", "driver1");
        await using var other = await LivePhone.LoginAsync(f.Ai.Server, "owner@live-b.test", "owner1");

        var id = await Part5bFlow.OfficeUploadAsync(office.Browser, driverId);
        bool Checked(JsonElement m) => m.GetProperty("status").GetInt32() == (int)CaptureStatus.NeedsReview;
        await office.WaitForAsync(CaptureLive.Changed, id, Checked);
        await driver.WaitForAsync(CaptureLive.Changed, id, Checked);

        Assert.True(office.Has(CaptureLive.Added, id));
        Assert.True(driver.Has(CaptureLive.Added, id));
        Assert.True(office.Has(CaptureLive.Changed, id, m => m.GetProperty("reading").GetBoolean())); // "Reading now…"
        await Task.Delay(500);
        Assert.DoesNotContain(other.Got, g => g.Message.GetProperty("id").GetInt32() == id);

        // The page then fetches that one row again.
        var row = await office.Browser.GetAsync($"/company/captures?handler=Row&id={id}");
        Assert.Equal(HttpStatusCode.OK, row.StatusCode);
        var html = await row.Content.ReadAsStringAsync();
        Assert.Contains($"data-row=\"{id}\"", html);
        Assert.Contains("Being checked", html);
        Assert.Contains("Fuel slip", html);
        Assert.Contains("Slip 2026-11-27", html);
        Assert.Contains("What the AI read", html);
        Assert.DoesNotContain("<html", html);

        var mine = await driver.Browser.GetAsync($"/driver/slips?handler=Row&id={id}");
        Assert.Equal(HttpStatusCode.OK, mine.StatusCode);
        var slip = await mine.Content.ReadAsStringAsync();
        Assert.Contains("Fuel slip", slip);
        Assert.Contains("Slip 2026-11-27", slip);
        Assert.DoesNotContain("What the AI read", slip);

        Assert.Equal(HttpStatusCode.NotFound, (await other.Browser.GetAsync($"/company/captures?handler=Row&id={id}")).StatusCode);
    }

    [Fact]
    public async Task The_live_hub_needs_a_login()
    {
        var client = f.Ai.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        var r = await client.PostAsync(CaptureHub.Path + "/negotiate?negotiateVersion=1", null);
        Assert.False(r.IsSuccessStatusCode);
    }
}

/// <summary>Part 5b: slip date in the lists and filters, one row on its own, "Reading now…", AI reads paging.</summary>
public class Part5bListTests(TestApp app) : IClassFixture<TestApp>
{
    private readonly Scenario _s = new(app);

    private static async Task<string> HtmlAsync(Browser b, string url)
    {
        var r = await b.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return await r.Content.ReadAsStringAsync();
    }

    [Fact]
    public async Task Lists_show_and_filter_by_the_slip_date_and_fall_back_to_the_photo_date()
    {
        var owner = await _s.CompanyWithOwnerAsync("Slip Date Co", "owner@slip-date.test");
        var driverId = await Part5bFlow.AddDriverWithLoginAsync(app, owner, "Naledi", "Sliptwo", "0828880002");
        var old = await Part5bFlow.OfficeUploadAsync(owner, driverId);   // read: slip date 2026-03-30
        var fresh = await Part5bFlow.OfficeUploadAsync(owner, driverId); // not read yet: counts by its photo date

        var photoAt = new DateTime(2026, 11, 28, 8, 0, 0, DateTimeKind.Utc); // 10:00 in South Africa
        await Part5bFlow.InDbAsync(app, db => db.Captures.IgnoreQueryFilters().Where(c => c.Id == old || c.Id == fresh)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.CapturedAt, photoAt)));
        DateOnly? slipDate = new DateOnly(2026, 3, 30);
        await Part5bFlow.InDbAsync(app, db => db.Captures.IgnoreQueryFilters().Where(c => c.Id == old)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.EventDate, slipDate).SetProperty(c => c.Status, CaptureStatus.NeedsReview)));

        var all = await HtmlAsync(owner, "/company/captures");
        Assert.Contains("Slip 2026-03-30", all);
        Assert.Contains("Photo 2026-11-28 10:00", all);
        Assert.Contains("From (slip date)", all);
        Assert.Contains("/resources/lib-signalr", all);
        Assert.Contains("/resources/page-capture-live", all);
        Assert.Contains("data-live=\"/company/captures?handler=Row&amp;id=\"", all);

        var november = await HtmlAsync(owner, "/company/captures?from=2026-11-01&to=2026-11-30");
        Assert.Contains($"data-row=\"{fresh}\"", november);
        Assert.DoesNotContain($"data-row=\"{old}\"", november);

        var march = await HtmlAsync(owner, "/company/captures?from=2026-03-30&to=2026-03-30");
        Assert.Contains($"data-row=\"{old}\"", march);
        Assert.DoesNotContain($"data-row=\"{fresh}\"", march);

        // My slips: the same rule for the day filter.
        var phone = await _s.LoginAsync("0828880002", "driver1");
        var slips = await HtmlAsync(phone, "/driver/slips");
        Assert.Contains("Slip 2026-03-30", slips);
        Assert.Contains("Photo 2026-11-28 10:00", slips);
        Assert.Contains("/resources/page-capture-live", slips);
        var day1 = await HtmlAsync(phone, "/driver/slips?day=2026-03-30");
        Assert.Contains($"data-row=\"{old}\"", day1);
        Assert.DoesNotContain($"data-row=\"{fresh}\"", day1);
        var day2 = await HtmlAsync(phone, "/driver/slips?day=2026-11-28");
        Assert.Contains($"data-row=\"{fresh}\"", day2);
        Assert.DoesNotContain($"data-row=\"{old}\"", day2);
    }

    [Fact]
    public async Task One_row_shows_reading_now_and_only_to_its_own_company_and_driver()
    {
        var ownerA = await _s.CompanyWithOwnerAsync("Row Co A", "owner@row-a.test");
        var ownerB = await _s.CompanyWithOwnerAsync("Row Co B", "owner@row-b.test");
        var driverId = await Part5bFlow.AddDriverWithLoginAsync(app, ownerA, "Sizwe", "Rowone", "0828880003");
        await Part5bFlow.AddDriverWithLoginAsync(app, ownerA, "Palesa", "Rowtwo", "0828880004");
        var id = await Part5bFlow.OfficeUploadAsync(ownerA, driverId);

        // The worker holds the slip: the AI is reading it now.
        DateTime? until = DateTime.UtcNow.AddMinutes(5);
        await Part5bFlow.InDbAsync(app, db => db.Captures.IgnoreQueryFilters().Where(c => c.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.AiLockedUntil, until)));

        var row = await HtmlAsync(ownerA, $"/company/captures?handler=Row&id={id}");
        Assert.Contains("Reading now", row);
        Assert.Contains("tl-scan", row);
        Assert.Contains("data-reading=\"1\"", row);
        Assert.Equal(HttpStatusCode.NotFound, (await ownerB.GetAsync($"/company/captures?handler=Row&id={id}")).StatusCode);

        var driver = await _s.LoginAsync("0828880003", "driver1");
        Assert.Contains("Reading now", await HtmlAsync(driver, $"/driver/slips?handler=Row&id={id}"));
        var otherDriver = await _s.LoginAsync("0828880004", "driver1");
        Assert.Equal(HttpStatusCode.NotFound, (await otherDriver.GetAsync($"/driver/slips?handler=Row&id={id}")).StatusCode);

        // The lock ends: back to waiting.
        DateTime? none = null;
        await Part5bFlow.InDbAsync(app, db => db.Captures.IgnoreQueryFilters().Where(c => c.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.AiLockedUntil, none)));
        var again = await HtmlAsync(ownerA, $"/company/captures?handler=Row&id={id}");
        Assert.Contains("Waiting to be read", again);
        Assert.DoesNotContain("tl-scan", again);
    }

    [Fact]
    public async Task Ai_reads_shows_20_reads_per_page()
    {
        var owner = await _s.CompanyWithOwnerAsync("Reads Paging Co", "owner@reads-paging.test");
        var ids = new List<int>();
        for (var i = 0; i < 21; i++)
            ids.Add(await Part5bFlow.OfficeUploadAsync(owner, null));
        var now = DateTime.UtcNow;
        for (var i = 0; i < ids.Count; i++)
        {
            var id = ids[i];
            DateTime? readAt = now.AddMinutes(-i); // ids[0] is the newest read
            await Part5bFlow.InDbAsync(app, db => db.Captures.IgnoreQueryFilters().Where(c => c.Id == id)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.AiReadAt, readAt).SetProperty(c => c.Status, CaptureStatus.NeedsReview)));
        }
        var companyId = await Part5bFlow.InDbAsync(app, db => db.Companies.Where(c => c.Name == "Reads Paging Co").Select(c => c.Id).SingleAsync());

        var admin = await _s.AdminAsync();
        var page1 = await HtmlAsync(admin, $"/sysadmin/aireads?companyId={companyId}");
        Assert.Contains($"data-capture=\"{ids[0]}\"", page1);
        Assert.Contains($"data-capture=\"{ids[19]}\"", page1);
        Assert.DoesNotContain($"data-capture=\"{ids[20]}\"", page1);
        Assert.Contains("Older", page1);
        Assert.Contains("p=2", page1);

        var page2 = await HtmlAsync(admin, $"/sysadmin/aireads?companyId={companyId}&p=2");
        Assert.Contains($"data-capture=\"{ids[20]}\"", page2);
        Assert.DoesNotContain($"data-capture=\"{ids[0]}\"", page2);
        Assert.Contains("Newer", page2);
    }
}

public class CaptureLiveRuleTests
{
    [Fact]
    public void Reading_now_and_group_names()
    {
        Assert.Equal("Reading now…", new CaptureProgress(1, CaptureStatus.Processing, Reading: true).Label);
        Assert.Equal("Waiting to be read", new CaptureProgress(1, CaptureStatus.Processing).Label);
        Assert.Equal("Being checked", new CaptureProgress(1, CaptureStatus.NeedsReview, Reading: true).Label);
        Assert.Equal("company-7", CaptureHub.CompanyGroup(7));
        Assert.Equal("user-12", CaptureHub.UserGroup(12));
    }

    [Fact]
    public void A_slip_counts_by_its_slip_date_and_by_the_photo_date_until_read()
    {
        var day = new DateOnly(2026, 3, 30);
        var slips = new List<Capture>
        {
            new() { Id = 1, EventDate = day, CapturedAt = new DateTime(2026, 11, 28, 8, 0, 0, DateTimeKind.Utc) },
            new() { Id = 2, EventDate = null, CapturedAt = new DateTime(2026, 3, 29, 22, 30, 0, DateTimeKind.Utc) }, // 00:30 on the 30th in SA
            new() { Id = 3, EventDate = null, CapturedAt = new DateTime(2026, 3, 29, 21, 30, 0, DateTimeKind.Utc) }, // 23:30 on the 29th
            new() { Id = 4, EventDate = day.AddDays(1), CapturedAt = new DateTime(2026, 3, 30, 8, 0, 0, DateTimeKind.Utc) },
        }.AsQueryable();
        Assert.Equal(new[] { 1, 2 }, CaptureProgress.OnDay(slips, day).Select(c => c.Id).OrderBy(x => x).ToArray());
    }
}
