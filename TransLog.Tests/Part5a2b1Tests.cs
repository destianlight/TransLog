using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TransLog.Web.Ai;
using TransLog.Web.Data;

namespace TransLog.Tests;

/// <summary>Part 5a2b1: the reading engine (upload once, detect, read, delete, retry once) and the SysAdmin AI test page.</summary>
public class Part5a2b1Tests(FakeAiApp f) : IClassFixture<FakeAiApp>
{
    private readonly Scenario _s = new(f.App);

    private async Task<int> CompanyAsync(string name)
    {
        await _s.CompanyWithOwnerAsync(name, "owner@" + name.ToLowerInvariant().Replace(" ", "-", StringComparison.Ordinal) + ".test");
        using var scope = f.App.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Companies.Where(c => c.Name == name).Select(c => c.Id).SingleAsync();
    }

    private async Task<SlipRead> ReadAsync(int companyId, bool withKey = true)
    {
        using var scope = (withKey ? f.Ai.Services : f.App.Services).CreateScope();
        return await scope.ServiceProvider.GetRequiredService<SlipReader>().ReadAsync(companyId, TestJpeg.Make(800, 600));
    }

    private async Task<PromptSet> PromptsAsync(int companyId)
    {
        using var scope = f.App.Services.CreateScope();
        return await PromptSet.ForCompanyAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(), companyId);
    }

    [Fact]
    public async Task Only_system_admins_open_the_AI_test_page()
    {
        var owner = await _s.CompanyWithOwnerAsync("Ai Test Page Co", "owner@ai-test-page.test");
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync("/sysadmin/aitest")).StatusCode);

        // Without a key the page says so.
        var noKey = await (await (await _s.AdminAsync()).GetAsync("/sysadmin/aitest")).Content.ReadAsStringAsync();
        Assert.Contains("No AI provider is on with an API key", noKey);

        var admin = f.NewAiBrowser();
        Assert.Equal(HttpStatusCode.Redirect, (await admin.LoginAsync(TestApp.AdminEmail, TestApp.AdminPassword)).StatusCode);
        var html = await (await admin.GetAsync("/sysadmin/aitest")).Content.ReadAsStringAsync();
        Assert.DoesNotContain("No AI provider is on with an API key", html);
        Assert.Contains("Ai Test Page Co", html);
        Assert.Contains("/resources/page-ai-test", html);
        Assert.Contains("href=\"/sysadmin/aitest\"", html); // in the AI menu
    }

    [Fact]
    public async Task A_fuel_slip_is_uploaded_once_detected_read_and_deleted()
    {
        f.Fake.Reset();
        f.Fake.FuelSlip();
        var companyId = await CompanyAsync("Read Fuel Co");
        var prompts = await PromptsAsync(companyId);

        var r = await ReadAsync(companyId);

        Assert.Equal(ReadOutcome.Read, r.Outcome);
        Assert.Equal("fuel", r.Type?.Key);
        Assert.Equal("high", r.TypeConfidence);
        Assert.Single(f.Fake.Uploaded);
        Assert.Equal(f.Fake.Uploaded, f.Fake.Deleted);
        Assert.Equal(2, f.Fake.Asked.Count);

        // Same system prompt and photo first in both calls (cache); the texts are SystemText, DetectText and TypeText.
        var (detect, read) = (f.Fake.Asked[0], f.Fake.Asked[1]);
        Assert.Equal(prompts.SystemText(), detect.System);
        Assert.Equal(detect.System, read.System);
        Assert.Equal(f.Fake.Uploaded[0], detect.FileId);
        Assert.Equal(detect.FileId, read.FileId);
        Assert.Equal(prompts.DetectText(), detect.Prompt);
        Assert.Contains("- fuel:", detect.Prompt);
        Assert.Equal(PromptSet.TypeText(prompts.Types.Single(t => t.Key == "fuel")), read.Prompt);
        Assert.Equal(SlipReader.DetectMaxTokens, detect.MaxTokens);
        Assert.Equal(SlipReader.ReadMaxTokens, read.MaxTokens);

        // Core columns from the fields that carry them.
        Assert.Equal(412.5m, r.Quantity);
        Assert.Equal(8250.75m, r.Amount);
        Assert.Equal(123456m, r.Odometer);
        Assert.Equal(new DateOnly(2026, 11, 27), r.EventDate);
        Assert.Equal("Engen Vryburg", r.Vendor);
        Assert.Equal("S0230410", r.ReferenceNo);
        var pump = r.Values.Single(v => v.Key == "pump");
        Assert.Null(pump.Value);
        Assert.Null(pump.Confidence); // no confidence on empty fields
        Assert.Contains(r.Values, v => v.Key == "driver_name" && v.Value is null); // every field listed, missing = null

        // Versions, tokens and cost.
        Assert.Equal(prompts.SystemPrompt!.CurrentVersion, r.SystemVersion);
        Assert.Equal(prompts.Types.Single(t => t.Key == "fuel").CurrentVersion, r.TypeVersion);
        Assert.Equal(new AiTokens(2600, 1024, 260), r.Tokens);
        Assert.NotNull(r.Cost);
        Assert.True(r.Cost.Usd > 0);
        Assert.True(r.Cost.Zar > r.Cost.Usd);
        Assert.Equal(Math.Round(r.Cost.Usd * r.Cost.UsdToZar, 4), Math.Round(r.Cost.Zar, 4));
        Assert.Equal("deepseek", r.Provider?.Key);
    }

    [Fact]
    public async Task A_bad_answer_is_tried_once_more_then_the_slip_could_not_be_read()
    {
        var companyId = await CompanyAsync("Retry Co");

        f.Fake.Reset();
        f.Fake.Answer = (n, _) => n switch
        {
            1 => FakeVisionClient.Ok(FakeVisionClient.FuelDetect),
            2 => FakeVisionClient.Bad(900),
            _ => FakeVisionClient.Ok(FakeVisionClient.FuelRead, 1400, 1024, 220),
        };
        var once = await ReadAsync(companyId);
        Assert.Equal(ReadOutcome.Read, once.Outcome);
        Assert.Equal(3, f.Fake.Asked.Count);
        Assert.Equal(2, once.Steps.Single(s => s.Name == "Read").Tries);
        Assert.Equal(1200 + 900 + 1400, once.Tokens.Input); // both tries are paid for

        f.Fake.Reset();
        f.Fake.Answer = (n, _) => n == 1 ? FakeVisionClient.Ok(FakeVisionClient.FuelDetect) : FakeVisionClient.Bad();
        var twice = await ReadAsync(companyId);
        Assert.Equal(ReadOutcome.CouldNotRead, twice.Outcome);
        Assert.StartsWith("Could not be read", twice.Message);
        Assert.Equal(3, f.Fake.Asked.Count);
        Assert.Single(f.Fake.Deleted);
    }

    [Fact]
    public async Task Other_low_confidence_or_an_unknown_type_is_type_not_sure_and_not_read()
    {
        var companyId = await CompanyAsync("Not Sure Co");
        foreach (var detect in new[]
                 {
                     "{\"type\": \"other\", \"confidence\": \"high\", \"description\": \"A photo of a dog\"}",
                     "{\"type\": \"fuel\", \"confidence\": \"low\", \"description\": \"Maybe a slip\"}",
                     "{\"type\": \"loads\", \"confidence\": \"high\", \"description\": \"A delivery note\"}",
                 })
        {
            f.Fake.Reset();
            f.Fake.Answer = (_, _) => FakeVisionClient.Ok(detect);
            var r = await ReadAsync(companyId);
            Assert.Equal(ReadOutcome.TypeNotSure, r.Outcome);
            Assert.Null(r.Type);
            Assert.Single(f.Fake.Asked); // step 2 not run
            Assert.Single(f.Fake.Deleted);
            Assert.StartsWith("Type not sure", r.Message);
        }
    }

    [Fact]
    public async Task A_provider_problem_is_not_retried_and_no_key_means_no_provider()
    {
        var companyId = await CompanyAsync("Provider Down Co");

        f.Fake.Reset();
        f.Fake.Answer = (_, _) => FakeVisionClient.Down();
        var down = await ReadAsync(companyId);
        Assert.Equal(ReadOutcome.ProviderProblem, down.Outcome);
        Assert.Single(f.Fake.Asked);
        Assert.Single(f.Fake.Deleted);

        f.Fake.Reset();
        f.Fake.Upload = () => new VisionUpload(null, TimeSpan.Zero, VisionFailure.Provider, "Upload: the API key was refused (401).");
        var upload = await ReadAsync(companyId);
        Assert.Equal(ReadOutcome.ProviderProblem, upload.Outcome);
        Assert.Empty(f.Fake.Asked);
        Assert.Empty(f.Fake.Deleted);

        f.Fake.Reset();
        f.Fake.Upload = () => new VisionUpload(null, TimeSpan.Zero, VisionFailure.Refused, "Upload: the provider answered 400.");
        Assert.Equal(ReadOutcome.CouldNotRead, (await ReadAsync(companyId)).Outcome);

        // The normal app has no key for DeepSeek.
        f.Fake.Reset();
        var none = await ReadAsync(companyId, withKey: false);
        Assert.Equal(ReadOutcome.NoProvider, none.Outcome);
        Assert.Empty(f.Fake.Asked);
        Assert.NotEqual("", none.DetectText); // the prompts are still shown
    }

    [Fact]
    public async Task The_test_page_reads_a_photo_shows_the_answer_and_stores_nothing()
    {
        var companyId = await CompanyAsync("Page Run Co");
        f.Fake.Reset();
        f.Fake.FuelSlip();

        var admin = f.NewAiBrowser();
        Assert.Equal(HttpStatusCode.Redirect, (await admin.LoginAsync(TestApp.AdminEmail, TestApp.AdminPassword)).StatusCode);
        var page = await (await admin.GetAsync("/sysadmin/aitest")).Content.ReadAsStringAsync();
        var token = Regex.Match(page, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        Assert.NotEqual("", token);

        int captures;
        using (var scope = f.App.Services.CreateScope())
            captures = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Captures.IgnoreQueryFilters().CountAsync();

        async Task<string> RunAsync(bool withPhoto)
        {
            using var form = new MultipartFormDataContent
            {
                { new StringContent(token), "__RequestVerificationToken" },
                { new StringContent(companyId.ToString(System.Globalization.CultureInfo.InvariantCulture)), "companyId" },
                { new StringContent(""), "providerId" },
            };
            if (withPhoto)
            {
                var photo = new ByteArrayContent(TestJpeg.Make(1600, 1200));
                photo.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
                form.Add(photo, "photo", "slip.jpg");
            }
            var r = await admin.Client.PostAsync("/sysadmin/aitest?handler=Run", form);
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            return await r.Content.ReadAsStringAsync();
        }

        var html = await RunAsync(withPhoto: true);
        Assert.Contains("data-aitest=\"Read\"", html);
        Assert.Contains("Fuel slip", html);
        Assert.Contains("data-field=\"litres\"", html);
        Assert.Contains("412.5", html);
        Assert.Contains("2026-11-27", html);
        Assert.Contains("1600", html); // photo size shown
        Assert.DoesNotContain("<html", html); // a piece of page, not a whole page

        Assert.Contains("data-aitest=\"error\"", await RunAsync(withPhoto: false));

        using (var scope = f.App.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(captures, await db.Captures.IgnoreQueryFilters().CountAsync()); // nothing stored
            Assert.True(await db.AuditLogs.AnyAsync(a => a.Action == "AiTestRun" && a.Details!.Contains("Page Run Co")));
        }
    }
}

/// <summary>Part 5a2b1: DeepSeek answers and slip values read without any call.</summary>
public class VisionRuleTests
{
    private static string Chat(string? content, string finish = "stop") => JsonSerializer.Serialize(new
    {
        model = "deepseek-flash",
        choices = new[] { new { message = new { role = "assistant", content }, finish_reason = finish } },
        usage = new { prompt_tokens = 1300, completion_tokens = 210, prompt_cache_hit_tokens = 1024, prompt_cache_miss_tokens = 276 },
    });

    [Fact]
    public void A_chat_answer_gives_json_and_tokens_and_bad_answers_are_failures()
    {
        var ok = DeepSeekVisionClient.ReadChat(Chat("{\"type\": \"fuel\"}"), TimeSpan.FromSeconds(2));
        Assert.True(ok.Ok);
        Assert.Equal("{\"type\": \"fuel\"}", ok.Json);
        Assert.Equal(new AiTokens(1300, 1024, 210), ok.Tokens);

        var fenced = DeepSeekVisionClient.ReadChat(Chat("```json\n{\"a\": 1}\n```"), TimeSpan.Zero);
        Assert.Equal("{\"a\": 1}", fenced.Json);

        Assert.Equal(VisionFailure.BadAnswer, DeepSeekVisionClient.ReadChat(Chat("{\"a\": 1}", "length"), TimeSpan.Zero).Failure);
        Assert.Equal(VisionFailure.BadAnswer, DeepSeekVisionClient.ReadChat(Chat(""), TimeSpan.Zero).Failure);
        Assert.Equal(VisionFailure.BadAnswer, DeepSeekVisionClient.ReadChat(Chat("no json here"), TimeSpan.Zero).Failure);
        Assert.Equal(VisionFailure.BadAnswer, DeepSeekVisionClient.ReadChat("not json", TimeSpan.Zero).Failure);
        Assert.Equal(1024, ok.Tokens.Cached);

        // OpenAI-style cached tokens are understood too.
        using var doc = JsonDocument.Parse("{\"usage\":{\"prompt_tokens\":10,\"completion_tokens\":2,\"prompt_tokens_details\":{\"cached_tokens\":8}}}");
        Assert.Equal(new AiTokens(10, 8, 2), DeepSeekVisionClient.ReadUsage(doc.RootElement));
    }

    [Fact]
    public void The_chat_request_has_thinking_off_json_mode_and_the_photo_before_the_text()
    {
        var body = DeepSeekVisionClient.ChatBody("deepseek-flash", "SYSTEM", "file-api-1", "STEP", 300);
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        Assert.Equal("disabled", root.GetProperty("thinking").GetProperty("type").GetString());
        Assert.Equal("json_object", root.GetProperty("response_format").GetProperty("type").GetString());
        Assert.Equal(0, root.GetProperty("temperature").GetInt32());
        Assert.Equal(300, root.GetProperty("max_tokens").GetInt32());
        var messages = root.GetProperty("messages");
        Assert.Equal("system", messages[0].GetProperty("role").GetString());
        Assert.Equal("SYSTEM", messages[0].GetProperty("content").GetString());
        var parts = messages[1].GetProperty("content");
        Assert.Equal("file", parts[0].GetProperty("type").GetString());
        Assert.Equal("file-api-1", parts[0].GetProperty("file_id").GetString());
        Assert.Equal("STEP", parts[1].GetProperty("text").GetString());

        Assert.Equal("file-api-abc", DeepSeekVisionClient.ReadFileId("{\"id\":\"file-api-abc\",\"object\":\"file\"}"));
        Assert.Equal(VisionFailure.Refused, DeepSeekVisionClient.FailureFor(HttpStatusCode.BadRequest));
        Assert.Equal(VisionFailure.Provider, DeepSeekVisionClient.FailureFor(HttpStatusCode.TooManyRequests));
        Assert.Equal(VisionFailure.Provider, DeepSeekVisionClient.FailureFor(HttpStatusCode.Unauthorized));
        Assert.Equal(VisionFailure.Provider, DeepSeekVisionClient.FailureFor(HttpStatusCode.ServiceUnavailable));
        Assert.Equal("image/png", DeepSeekVisionClient.PhotoKind(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0, 0 }).Mime);
        Assert.Equal("image/jpeg", DeepSeekVisionClient.PhotoKind(TestJpeg.Make(10, 10)).Mime);
    }

    [Fact]
    public void Numbers_and_dates_from_slips_are_read_the_South_African_way()
    {
        Assert.Equal(8250.75m, SlipReader.Number("R8 250.75"));
        Assert.Equal(1234.5m, SlipReader.Number("1234,5"));
        Assert.Equal(1234.56m, SlipReader.Number("1,234.56"));
        Assert.Equal(412m, SlipReader.Number("412 l"));
        Assert.Equal(123.4m, SlipReader.Number("123.4"));
        Assert.Null(SlipReader.Number("n/a"));
        Assert.Null(SlipReader.Number("1.2.3"));

        Assert.Equal(new DateOnly(2026, 11, 27), SlipReader.Date("2026-11-27"));
        Assert.Equal(new DateOnly(2026, 11, 27), SlipReader.Date("27/11/2026"));
        Assert.Equal(new DateOnly(2026, 3, 4), SlipReader.Date("04/03/2026")); // day first
        Assert.Equal(new DateOnly(2026, 11, 27), SlipReader.Date("2026-11-27 14:05"));
        Assert.Null(SlipReader.Date("yesterday"));
    }

    [Fact]
    public void Fields_are_read_with_or_without_the_value_wrapper()
    {
        var fields = new List<PromptField>
        {
            new() { Key = "net_kg", Label = "Net", Type = "number", Core = "Quantity" },
            new() { Key = "ticket", Label = "Ticket", Core = "ReferenceNo" },
            new() { Key = "sealed", Label = "Sealed", Type = "yesno" },
            new() { Key = "product", Label = "Product" },
        };
        var r = new SlipRead();
        SlipReader.ReadFields(r, fields, "{\"fields\":{\"net_kg\":{\"value\":\"28 340\",\"confidence\":\"HIGH\"},\"ticket\":\"WB-001\",\"sealed\":{\"value\":true}}}");
        Assert.Equal(28340m, r.Quantity);
        Assert.Equal("WB-001", r.ReferenceNo);
        Assert.Equal("high", r.Values[0].Confidence);
        Assert.Equal("yes", r.Values[2].Value);
        Assert.Null(r.Values[3].Value);
        Assert.Equal(4, r.Values.Count);
    }
}
