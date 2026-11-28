using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using TransLog.Web.Ai;
using TransLog.Web.Data;

namespace TransLog.Tests;

/// <summary>
/// Part 5a2b: stands in for DeepSeek in tests (no paid calls). Answers come from <see cref="Answer"/>; every upload,
/// call and delete is recorded so tests can check the flow.
/// </summary>
public sealed class FakeVisionClient : IVisionClientFactory, IVisionClient
{
    public const string FuelDetect = "{\"type\": \"fuel\", \"confidence\": \"high\", \"description\": \"Diesel slip from Engen\"}";

    public const string FuelRead = """
        {"fields": {
          "registration": {"value": "LLT587NW", "confidence": "high"},
          "litres": {"value": 412.5, "confidence": "high"},
          "amount": {"value": "R8 250.75", "confidence": "medium"},
          "odometer": {"value": 123456, "confidence": "high"},
          "slip_date": {"value": "2026-11-27", "confidence": "high"},
          "vendor": {"value": "Engen Vryburg", "confidence": "high"},
          "reference_no": {"value": "S0230410", "confidence": "low"},
          "pump": {"value": null, "confidence": "low"}
        }}
        """;

    private readonly object _lock = new();
    private int _files;

    public List<string> Uploaded { get; } = [];
    public List<string> Deleted { get; } = [];
    public List<(string System, string FileId, string Prompt, int MaxTokens)> Asked { get; } = [];

    /// <summary>Upload answer; null = a working upload.</summary>
    public Func<VisionUpload?> Upload { get; set; } = () => null;

    /// <summary>Answer per call: (call number from 1, prompt) → answer.</summary>
    public Func<int, string, VisionAnswer> Answer { get; set; } = (_, _) => Ok("{}");

    public static VisionAnswer Ok(string json, int input = 1200, int cached = 0, int output = 200) =>
        new(json, "deepseek-flash", new AiTokens(input, cached, output), "stop", TimeSpan.FromMilliseconds(5));

    public static VisionAnswer Bad(int input = 1200) =>
        new(null, "deepseek-flash", new AiTokens(input, 0, 0), "stop", TimeSpan.FromMilliseconds(5), VisionFailure.BadAnswer, "The answer was empty.");

    public static VisionAnswer Down() =>
        new(null, null, default, null, TimeSpan.FromMilliseconds(5), VisionFailure.Provider, "Call: too many requests (429), try again later.");

    /// <summary>True for the step 1 prompt (its type list always ends with "- other:").</summary>
    public static bool IsDetect(string prompt) => prompt.Contains("- other:", StringComparison.Ordinal);

    /// <summary>Detect says fuel, read gives <see cref="FuelRead"/>; the second call is mostly cached. Works for slips read at the same time.</summary>
    public void FuelSlip() => Answer = (_, prompt) => IsDetect(prompt) ? Ok(FuelDetect, 1200, 0, 40) : Ok(FuelRead, 1400, 1024, 220);

    public void Reset()
    {
        lock (_lock)
        {
            Uploaded.Clear();
            Deleted.Clear();
            Asked.Clear();
        }
        Upload = () => null;
        Answer = (_, _) => Ok("{}");
    }

    public IVisionClient? Create(AiProvider provider, string apiKey) => this;

    public Task<VisionUpload> UploadAsync(byte[] photo, CancellationToken ct = default)
    {
        if (Upload() is VisionUpload u)
            return Task.FromResult(u);
        lock (_lock)
        {
            var id = "file-api-fake-" + ++_files;
            Uploaded.Add(id);
            return Task.FromResult(new VisionUpload(id, TimeSpan.FromMilliseconds(5)));
        }
    }

    public Task<VisionAnswer> AskAsync(string system, string fileId, string prompt, int maxTokens, CancellationToken ct = default)
    {
        int n;
        lock (_lock)
        {
            Asked.Add((system, fileId, prompt, maxTokens));
            n = Asked.Count;
        }
        return Task.FromResult(Answer(n, prompt));
    }

    public Task DeleteAsync(string fileId, CancellationToken ct = default)
    {
        lock (_lock)
            Deleted.Add(fileId);
        return Task.CompletedTask;
    }
}

/// <summary>
/// A test database with two apps on it (Part 5a2b): <see cref="App"/> as usual (DeepSeek has no key), and
/// <see cref="Ai"/>, where DeepSeek has a key from the server settings and <see cref="Fake"/> answers every AI call.
/// </summary>
public class FakeAiApp : IAsyncLifetime
{
    public const string FakeKey = "sk-fake-for-tests-1234";

    public TestApp App { get; } = new();

    public FakeVisionClient Fake { get; } = new();

    public WebApplicationFactory<Program> Ai { get; private set; } = null!;

    /// <summary>Part 5a2b2: the background worker runs in <see cref="Ai"/> (looking every second) when true.</summary>
    protected virtual bool Worker => false;

    public async Task InitializeAsync()
    {
        await App.InitializeAsync();
        Ai = App.WithWebHostBuilder(b =>
        {
            b.UseSetting("Ai:Providers:deepseek:ApiKey", FakeKey);
            b.UseSetting("Ai:Worker:Enabled", Worker ? "true" : "false");
            b.UseSetting("Ai:Worker:EverySeconds", "1");
            b.UseSetting("Ai:Worker:StartDelaySeconds", "0");
            b.ConfigureTestServices(s => s.AddSingleton<IVisionClientFactory>(Fake));
        });
        _ = Ai.Server;
    }

    public async Task DisposeAsync()
    {
        await Ai.DisposeAsync();
        await ((IAsyncLifetime)App).DisposeAsync();
    }

    /// <summary>A browser on the app with the fake AI.</summary>
    public Browser NewAiBrowser() => new(Ai.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        BaseAddress = new Uri("https://localhost"),
        HandleCookies = true,
    }));
}

/// <summary>Part 5a2b2: like <see cref="FakeAiApp"/>, with the background worker reading every waiting slip with the fake AI.</summary>
public sealed class FakeAiWorkerApp : FakeAiApp
{
    protected override bool Worker => true;
}
