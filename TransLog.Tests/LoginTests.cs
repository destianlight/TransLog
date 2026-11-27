using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TransLog.Tests;

/// <summary>One login per account, and logins that survive an app restart ("Keep me logged in").</summary>
public class LoginTests(TestApp app) : IClassFixture<TestApp>
{
    private const string CookieName = "translog.auth";

    private static Browser NewBrowser(WebApplicationFactory<Program> factory) => new(factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        BaseAddress = new Uri("https://localhost"),
        HandleCookies = true,
    }));

    /// <summary>The login cookie(s) from a login answer, as "name=value" parts.</summary>
    private static List<string> LoginCookies(HttpResponseMessage r) =>
        r.Headers.TryGetValues("Set-Cookie", out var all)
            ? all.Where(c => c.StartsWith(CookieName, StringComparison.Ordinal)).ToList()
            : [];

    [Fact]
    public async Task Logging_in_on_a_second_phone_logs_out_the_first()
    {
        // Check the cookie on every request, so the test needs no waiting.
        var factory = app.WithWebHostBuilder(b => b.UseSetting("Login:RecheckSeconds", "0"));

        var first = NewBrowser(factory);
        Assert.Equal(HttpStatusCode.Redirect, (await first.LoginAsync(TestApp.AdminEmail, TestApp.AdminPassword)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await first.GetAsync("/sysadmin")).StatusCode);

        var second = NewBrowser(factory);
        Assert.Equal(HttpStatusCode.Redirect, (await second.LoginAsync(TestApp.AdminEmail, TestApp.AdminPassword)).StatusCode);

        var firstAgain = await first.GetAsync("/sysadmin");
        Assert.Equal(HttpStatusCode.Redirect, firstAgain.StatusCode);
        Assert.Contains("/login", firstAgain.Headers.Location?.OriginalString ?? "");

        Assert.Equal(HttpStatusCode.OK, (await second.GetAsync("/sysadmin")).StatusCode);
    }

    [Fact]
    public async Task Login_survives_an_app_restart_when_keys_are_saved()
    {
        var keys = Path.Combine(Path.GetTempPath(), "translog-keys-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            var before = app.WithWebHostBuilder(b => b.UseSetting("DataProtection:KeysPath", keys));
            var login = await NewBrowser(before).LoginAsync(TestApp.AdminEmail, TestApp.AdminPassword);
            Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
            var cookies = LoginCookies(login).Select(c => c.Split(';')[0]).ToList();
            Assert.NotEmpty(cookies);
            Assert.NotEmpty(Directory.GetFiles(keys, "*.xml"));

            // A second app with its own memory, like the site after an app pool recycle or a deploy.
            var after = app.WithWebHostBuilder(b => b.UseSetting("DataProtection:KeysPath", keys));
            var client = after.CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                BaseAddress = new Uri("https://localhost"),
                HandleCookies = false,
            });
            using var request = new HttpRequestMessage(HttpMethod.Get, "/sysadmin");
            request.Headers.Add("Cookie", string.Join("; ", cookies));
            var page = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        }
        finally
        {
            try { Directory.Delete(keys, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    [Fact]
    public async Task Keep_me_logged_in_sets_a_lasting_cookie_and_unticked_does_not()
    {
        var kept = await app.NewBrowser().PostFormAsync("/login", new()
        {
            ["Login"] = TestApp.AdminEmail, ["Password"] = TestApp.AdminPassword, ["RememberMe"] = "true",
        });
        var notKept = await app.NewBrowser().PostFormAsync("/login", new()
        {
            ["Login"] = TestApp.AdminEmail, ["Password"] = TestApp.AdminPassword, ["RememberMe"] = "false",
        });

        Assert.Equal(HttpStatusCode.Redirect, kept.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, notKept.StatusCode);
        Assert.Contains(LoginCookies(kept), c => c.Contains("expires=", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(LoginCookies(notKept), c => c.Contains("expires=", StringComparison.OrdinalIgnoreCase));
        Assert.NotEmpty(LoginCookies(notKept));
    }
}
