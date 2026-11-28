using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TransLog.Web.Ai;
using TransLog.Web.Data;

namespace TransLog.Tests;

/// <summary>Part 5a2b2 helpers: an office upload and waiting for the worker.</summary>
public static class AiFlow
{
    private static readonly Regex Token = new("name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");

    /// <summary>Sends one photo through office capture (as page-office-capture.js does) and returns the capture id.</summary>
    public static async Task<int> OfficeUploadAsync(Browser owner)
    {
        var page = await owner.GetAsync("/company/capture");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var token = Token.Match(await page.Content.ReadAsStringAsync()).Groups[1].Value;
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(Guid.NewGuid().ToString()), "clientKey");
        var photo = new ByteArrayContent(TestJpeg.Make(1200, 1600));
        photo.Headers.ContentType = new("image/jpeg");
        form.Add(photo, "photo", "photo.jpg");
        var request = new HttpRequestMessage(HttpMethod.Post, "/company/capture?handler=Upload") { Content = form };
        request.Headers.Add("RequestVerificationToken", token);
        var r = await owner.Client.SendAsync(request);
        var body = await r.Content.ReadAsStringAsync();
        Assert.True(r.StatusCode == HttpStatusCode.OK, body);
        return int.Parse(Regex.Match(body, "\"id\"\\s*:\\s*(\\d+)").Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
    }

    public static async Task<Capture> LoadAsync(IServiceProvider services, int id)
    {
        using var scope = services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Captures.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(c => c.Id == id);
    }

    /// <summary>Waits (up to 30 s) until the capture matches.</summary>
    public static async Task<Capture> WaitForAsync(IServiceProvider services, int id, Func<Capture, bool> done)
    {
        var until = DateTime.UtcNow.AddSeconds(30);
        while (true)
        {
            var c = await LoadAsync(services, id);
            if (done(c))
                return c;
            Assert.True(DateTime.UtcNow < until, $"Capture {id} was not read in time (status {c.Status}, note {c.ErrorMessage}).");
            await Task.Delay(200);
        }
    }
}

/// <summary>Part 5a2b2: the background worker reads uploaded slips with the fake AI and stores the result.</summary>
public class Part5a2b2Tests(FakeAiWorkerApp f) : IClassFixture<FakeAiWorkerApp>
{
    private readonly Scenario _s = new(f.App);

    [Fact]
    public async Task An_uploaded_slip_is_read_in_the_background_and_shown_on_the_captures_list()
    {
        f.Fake.Reset();
        f.Fake.FuelSlip();
        var owner = await _s.CompanyWithOwnerAsync("Worker Fuel Co", "owner@worker-fuel.test");
        var id = await AiFlow.OfficeUploadAsync(owner);

        var c = await AiFlow.WaitForAsync(f.App.Services, id, x => x.Status != CaptureStatus.Processing);
        Assert.Equal(CaptureStatus.NeedsReview, c.Status);
        Assert.Null(c.ErrorMessage);
        Assert.NotNull(c.CaptureTypeId);
        Assert.Equal("high", c.TypeConfidence);
        Assert.Equal(new DateOnly(2026, 11, 27), c.EventDate);
        Assert.Equal(412.5m, c.Quantity);
        Assert.Equal(8250.75m, c.Amount);
        Assert.Equal(123456m, c.Odometer);
        Assert.Equal("Engen Vryburg", c.Vendor);
        Assert.Equal("S0230410", c.ReferenceNo);
        Assert.Contains("\"fuel\"", c.DetectRawJson);
        Assert.Contains("litres", c.AiRawJson);
        Assert.Equal("412.5", CaptureReader.ReadDataJson(c.DataJson)["litres"]);
        Assert.Equal(1, c.SystemVersion);
        Assert.NotNull(c.DetectVersion);
        Assert.NotNull(c.TypeVersion);
        Assert.NotNull(c.AiProviderId);
        Assert.Equal("deepseek-flash", c.AiModel);
        Assert.Equal(2600, c.AiInputTokens);
        Assert.Equal(1024, c.AiCachedTokens);
        Assert.Equal(260, c.AiOutputTokens);
        Assert.NotNull(c.AiPriceId);
        Assert.NotNull(c.AiPeak);
        Assert.True(c.AiCostZar > 0);
        Assert.True(c.AiUsdToZar > 0);
        Assert.NotNull(c.AiReadAt);
        Assert.NotNull(c.AiMilliseconds);
        Assert.Null(c.AiLockedUntil);
        Assert.Equal(0, c.AiAttempts);

        // The company's Captures list shows the type, the slip date and what the AI read.
        var list = await (await owner.GetAsync("/company/captures")).Content.ReadAsStringAsync();
        Assert.Contains("data-read=\"" + id + "\"", list);
        Assert.Contains("Fuel slip", list);
        Assert.Contains("slip date 2026-11-27", list);
        Assert.Contains("What the AI read", list);
        Assert.Contains("Filling station", list);
        Assert.Contains("Engen Vryburg", list);
        Assert.Contains("Being checked", list);

        // AI reads: SysAdmins only, no photos.
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync("/sysadmin/aireads")).StatusCode);
        var admin = await _s.AdminAsync();
        var reads = await (await admin.GetAsync("/sysadmin/aireads")).Content.ReadAsStringAsync();
        Assert.Contains("data-capture=\"" + id + "\"", reads);
        Assert.Contains("Worker Fuel Co", reads);
        Assert.Contains("Fuel slip", reads);
        Assert.Contains("Engen Vryburg", reads); // raw answer
        Assert.DoesNotContain("/files/capture/", reads);
        Assert.Contains("href=\"/sysadmin/aireads\"", reads); // in the AI menu
    }

    [Fact]
    public async Task A_slip_of_no_known_type_goes_to_review_as_type_not_sure()
    {
        f.Fake.Reset();
        f.Fake.Answer = (_, _) => FakeVisionClient.Ok("{\"type\": \"other\", \"confidence\": \"high\", \"description\": \"A dog\"}");
        var owner = await _s.CompanyWithOwnerAsync("Worker Other Co", "owner@worker-other.test");
        var id = await AiFlow.OfficeUploadAsync(owner);

        var c = await AiFlow.WaitForAsync(f.App.Services, id, x => x.Status != CaptureStatus.Processing);
        Assert.Equal(CaptureStatus.NeedsReview, c.Status);
        Assert.Null(c.CaptureTypeId);
        Assert.StartsWith("Type not sure", c.ErrorMessage);
        Assert.Contains("other", c.DetectRawJson);
        Assert.Null(c.AiRawJson);
        Assert.NotNull(c.AiReadAt);

        var list = await (await owner.GetAsync("/company/captures")).Content.ReadAsStringAsync();
        Assert.Contains("Type not sure", list);
    }

    [Fact]
    public async Task A_provider_problem_keeps_the_slip_waiting_for_a_later_try()
    {
        f.Fake.Reset();
        f.Fake.Answer = (_, _) => FakeVisionClient.Down();
        var owner = await _s.CompanyWithOwnerAsync("Worker Down Co", "owner@worker-down.test");
        var id = await AiFlow.OfficeUploadAsync(owner);

        var c = await AiFlow.WaitForAsync(f.App.Services, id, x => x.AiAttempts > 0);
        Assert.Equal(CaptureStatus.Processing, c.Status);
        Assert.Equal(1, c.AiAttempts);
        Assert.True(c.AiNextTryAt > DateTime.UtcNow.AddMinutes(1));
        Assert.StartsWith("Waiting:", c.ErrorMessage);
        Assert.Null(c.AiLockedUntil);
        Assert.Null(c.AiReadAt);
    }
}

/// <summary>Part 5a2b2: the queue in the Capture table, without the worker (the test app has it switched off).</summary>
public class AiQueueTests(TestApp app) : IClassFixture<TestApp>
{
    private readonly Scenario _s = new(app);

    [Fact]
    public async Task A_slip_is_claimed_once_and_with_no_provider_it_keeps_waiting()
    {
        var owner = await _s.CompanyWithOwnerAsync("Queue Co", "owner@queue.test");
        var id = await AiFlow.OfficeUploadAsync(owner);

        using var scope = app.Services.CreateScope();
        var job = scope.ServiceProvider.GetRequiredService<CaptureReader>();
        int? claimed = null;
        for (var i = 0; i < 20 && claimed != id; i++) // other tests' slips in this database may come first
            claimed = await job.ClaimAsync();
        Assert.Equal(id, claimed);
        Assert.NotNull((await AiFlow.LoadAsync(app.Services, id)).AiLockedUntil);

        // Taken: another worker cannot claim it.
        using (var other = app.Services.CreateScope())
        {
            var second = other.ServiceProvider.GetRequiredService<CaptureReader>();
            for (var i = 0; i < 20; i++)
                Assert.NotEqual(id, await second.ClaimAsync());
        }

        // This app has no DeepSeek key: the slip keeps waiting and is looked at again in 5 minutes.
        Assert.Equal(ReadOutcome.NoProvider, await job.ProcessAsync(id));
        var c = await AiFlow.LoadAsync(app.Services, id);
        Assert.Equal(CaptureStatus.Processing, c.Status);
        Assert.Equal(0, c.AiAttempts);
        Assert.Null(c.AiLockedUntil);
        Assert.True(c.AiNextTryAt > DateTime.UtcNow.AddMinutes(4));
        Assert.StartsWith("Waiting: No AI provider", c.ErrorMessage);

        // Still "Waiting to be read" on the Captures list, with no AI note.
        var list = await (await owner.GetAsync("/company/captures")).Content.ReadAsStringAsync();
        Assert.Contains("Waiting to be read", list);
        Assert.DoesNotContain("No AI provider", list);
    }

    [Fact]
    public async Task Wake_ends_the_wait_at_once()
    {
        var queue = new AiQueue();
        var signal = queue.Signal;
        var started = DateTime.UtcNow;
        var wait = AiQueue.WaitAsync(signal, TimeSpan.FromSeconds(30), CancellationToken.None);
        queue.Wake();
        await wait;
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(5));
        Assert.False(queue.Signal.IsCompleted); // a new signal for the next wait

        // Without a wake it waits until the time is up.
        var t = DateTime.UtcNow;
        await AiQueue.WaitAsync(queue.Signal, TimeSpan.FromMilliseconds(200), CancellationToken.None);
        Assert.True(DateTime.UtcNow - t >= TimeSpan.FromMilliseconds(150));
    }
}

/// <summary>Part 5a2b2: what each way a read can end does to the slip (no database).</summary>
public class CaptureReaderRuleTests
{
    private static readonly DateTime Now = new(2026, 11, 28, 10, 0, 0, DateTimeKind.Utc);

    private static SlipRead Result(ReadOutcome outcome, string? message = null) => new()
    {
        Outcome = outcome,
        Message = message,
        Model = "deepseek-flash",
        SystemVersion = 2,
        DetectVersion = 1,
        TypeVersion = 4,
        Tokens = new AiTokens(2400, 1000, 600),
        Cost = new AiCost(7, true, 0.0015m, 18m, 0.027m),
        Total = TimeSpan.FromSeconds(12.3),
    };

    [Fact]
    public void A_provider_problem_waits_2_10_and_30_minutes_then_goes_to_review()
    {
        var c = new Capture { Status = CaptureStatus.Processing, AiLockedUntil = Now };
        var waits = new[] { 2, 10, 30 };
        for (var i = 0; i < 3; i++)
        {
            CaptureReader.Apply(c, Result(ReadOutcome.ProviderProblem, "Call: too many requests (429)."), Now);
            Assert.Equal(CaptureStatus.Processing, c.Status);
            Assert.Equal(i + 1, c.AiAttempts);
            Assert.Equal(Now.AddMinutes(waits[i]), c.AiNextTryAt);
            Assert.Null(c.AiLockedUntil);
            Assert.Null(c.AiReadAt);
        }

        CaptureReader.Apply(c, Result(ReadOutcome.ProviderProblem, "no answer in time."), Now);
        Assert.Equal(CaptureStatus.NeedsReview, c.Status);
        Assert.Equal(4, c.AiAttempts);
        Assert.Null(c.AiNextTryAt);
        Assert.StartsWith("Could not be read: the AI provider did not answer", c.ErrorMessage);
        Assert.Equal(Now, c.AiReadAt);
    }

    [Fact]
    public void Read_type_not_sure_and_could_not_be_read_go_to_review_with_the_facts()
    {
        var read = new Capture { Status = CaptureStatus.Processing, AiNextTryAt = Now, ErrorMessage = "Waiting: old" };
        var r = Result(ReadOutcome.Read);
        r.Values.Add(new ReadValue("litres", "Litres", "number", "Quantity", "41.5", "high"));
        r.Values.Add(new ReadValue("pump", "Pump", "text", null, null, null));
        r.Quantity = 41.5m;
        r.EventDate = new DateOnly(2026, 11, 20);
        CaptureReader.Apply(read, r, Now);
        Assert.Equal(CaptureStatus.NeedsReview, read.Status);
        Assert.Null(read.ErrorMessage);
        Assert.Null(read.AiNextTryAt);
        Assert.Equal(41.5m, read.Quantity);
        Assert.Equal(new DateOnly(2026, 11, 20), read.EventDate);
        Assert.Null(read.TypeVersion); // no type entry in this test, so no type version
        Assert.Null(read.CaptureTypeId);
        Assert.Equal(2400, read.AiInputTokens);
        Assert.Equal(0.027m, read.AiCostZar);
        Assert.Equal(7, read.AiPriceId);
        Assert.Equal(12300, read.AiMilliseconds);
        var data = CaptureReader.ReadDataJson(read.DataJson);
        Assert.Equal("41.5", data["litres"]);
        Assert.Null(data["pump"]);

        var notSure = new Capture { Status = CaptureStatus.Processing };
        CaptureReader.Apply(notSure, Result(ReadOutcome.TypeNotSure, "Type not sure (answered \"other\", high confidence)."), Now);
        Assert.Equal(CaptureStatus.NeedsReview, notSure.Status);
        Assert.StartsWith("Type not sure", notSure.ErrorMessage);
        Assert.Null(notSure.TypeVersion);

        var bad = new Capture { Status = CaptureStatus.Processing };
        CaptureReader.Apply(bad, Result(ReadOutcome.CouldNotRead, "Upload: the provider answered 400: bad image"), Now);
        Assert.Equal(CaptureStatus.NeedsReview, bad.Status);
        Assert.Equal("Could not be read: Upload: the provider answered 400: bad image", bad.ErrorMessage);

        var none = new Capture { Status = CaptureStatus.Processing };
        CaptureReader.Apply(none, Result(ReadOutcome.NoProvider, "No AI provider that is on and has a working API key."), Now);
        Assert.Equal(CaptureStatus.Processing, none.Status);
        Assert.Equal(0, none.AiAttempts);
        Assert.Equal(Now.AddMinutes(5), none.AiNextTryAt);
        Assert.Null(none.AiReadAt);
    }

    [Fact]
    public void The_captures_list_shows_read_values_with_their_labels()
    {
        var fields = PromptRules.FieldsToJson(new List<PromptField>
        {
            new() { Key = "litres", Label = "Litres", Type = "number" },
            new() { Key = "pump", Label = "Pump" },
        });
        var values = TransLog.Web.Areas.CompanyArea.Pages.Captures.IndexModel.ValuesOf(fields,
            "{\"litres\":\"41.5\",\"pump\":null,\"old_key\":\"x\"}");
        Assert.Equal(2, values.Count);
        Assert.Equal(("Litres", "41.5"), values[0]);
        Assert.Equal(("old_key", "x"), values[1]);
        Assert.Empty(TransLog.Web.Areas.CompanyArea.Pages.Captures.IndexModel.ValuesOf(fields, null));
        Assert.Empty(CaptureReader.ReadDataJson("not json"));
    }
}
