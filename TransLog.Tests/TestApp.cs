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

    public Task<HttpResponseMessage> LoginAsync(string email, string password) =>
        PostFormAsync("/login", new() { ["Email"] = email, ["Password"] = password });

    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"")]
    private static partial Regex TokenRegex();
}
