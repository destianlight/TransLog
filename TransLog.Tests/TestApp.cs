using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using TransLog.Web.Data;

namespace TransLog.Tests;

/// <summary>
/// Starts the real app against a throw-away SQL Server database.
/// On GitHub the test workflow provides SQL Server; the database is deleted afterwards.
/// </summary>
public sealed class TestApp : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string AdminEmail = "admin@translog.test";
    public const string AdminPassword = "admin-pass";

    private readonly string _database = "TransLog_Test_" + Guid.NewGuid().ToString("N")[..8];

    /// <summary>Uploaded photos go to a temporary folder per test class (Part 4a), deleted afterwards.</summary>
    public string FilesRoot { get; } = Path.Combine(Path.GetTempPath(), "translog-files-" + Guid.NewGuid().ToString("N")[..8]);

    private string ConnectionString
    {
        get
        {
            var server = Environment.GetEnvironmentVariable("TEST_SQL_SERVER") ?? "localhost,1433";
            var password = Environment.GetEnvironmentVariable("TEST_SQL_PASSWORD") ?? "Test-Passw0rd!";
            return $"Server={server};Database={_database};User Id=sa;Password={password};TrustServerCertificate=True;Encrypt=False";
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", ConnectionString);
        builder.UseSetting("SysAdmin:Email", AdminEmail);
        builder.UseSetting("SysAdmin:Password", AdminPassword);
        builder.UseSetting("SysAdmin:FullName", "Test Admin");
        builder.UseSetting("Storage:Root", FilesRoot);
        // Part 5a2b2: no background reading in ordinary tests (FakeAiWorkerApp switches it on with a fake AI).
        builder.UseSetting("Ai:Worker:Enabled", "false");
        // The live site may wait before reading (appsettings.json); tests read at once.
        builder.UseSetting("Ai:Worker:ReadDelaySeconds", "0");
    }

    public Task InitializeAsync()
    {
        _ = Server; // starts the app: migrations, roles, first system admin
        return Task.CompletedTask;
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        using (var scope = Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureDeletedAsync();
        await base.DisposeAsync();
        try { Directory.Delete(FilesRoot, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    /// <summary>A fresh "phone": its own cookies, redirects not followed so tests can check them.</summary>
    public Browser NewBrowser() => new(CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        BaseAddress = new Uri("https://localhost"),
        HandleCookies = true,
    }));
}

public sealed partial class Browser(HttpClient client)
{
    public HttpClient Client => client;

    public Task<HttpResponseMessage> GetAsync(string url) => client.GetAsync(url);

    /// <summary>Opens a form page, then posts the fields with its anti-forgery token.</summary>
    public async Task<HttpResponseMessage> PostFormAsync(string url, Dictionary<string, string> fields)
    {
        var page = await client.GetAsync(url);
        Assert.True(page.IsSuccessStatusCode, $"GET {url} returned {(int)page.StatusCode}");
        var html = await page.Content.ReadAsStringAsync();
        var match = TokenRegex().Match(html);
        Assert.True(match.Success, $"No anti-forgery token on {url}");

        var all = new Dictionary<string, string>(fields) { ["__RequestVerificationToken"] = match.Groups[1].Value };
        return await client.PostAsync(url, new FormUrlEncodedContent(all));
    }

    /// <summary>Logs in with an email or a cell number (Part 3c).</summary>
    public Task<HttpResponseMessage> LoginAsync(string login, string password) =>
        PostFormAsync("/login", new() { ["Login"] = login, ["Password"] = password });

    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"")]
    private static partial Regex TokenRegex();
}

/// <summary>
/// Fresh cell numbers for test users (Part 3c: every user needs one, unique on the whole platform).
/// They start with 06 so they never clash with the 08 numbers that tests type for drivers.
/// </summary>
public static class Cells
{
    private static int _next = 1000;

    public static string Next() => "06" + Interlocked.Increment(ref _next).ToString("D8");
}
