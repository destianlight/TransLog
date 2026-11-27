using System.Net;
using System.Text.RegularExpressions;

namespace TransLog.Tests;

/// <summary>Shared set-up for tests from Part 3 on: companies, owners and logged-in "phones".</summary>
public sealed class Scenario(TestApp app)
{
    private static readonly Regex Token = new("name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");

    public async Task<Browser> AdminAsync()
    {
        var b = app.NewBrowser();
        Assert.Equal(HttpStatusCode.Redirect, (await b.LoginAsync(TestApp.AdminEmail, TestApp.AdminPassword)).StatusCode);
        return b;
    }

    public async Task<Browser> LoginAsync(string email, string password)
    {
        var b = app.NewBrowser();
        var r = await b.LoginAsync(email, password);
        Assert.True(r.StatusCode == HttpStatusCode.Redirect, $"Login failed for {email}");
        return b;
    }

    /// <summary>A system admin creates a company and its Owner; returns the Owner, logged in.</summary>
    public async Task<Browser> CompanyWithOwnerAsync(string name, string ownerEmail)
    {
        var r = await (await AdminAsync()).PostFormAsync("/sysadmin/companies/create", new()
        {
            ["Form.Name"] = name,
            ["Owner.FullName"] = "Owner of " + name,
            ["Owner.Email"] = ownerEmail,
            ["Owner.Password"] = "owner1",
            ["Owner.Cell"] = Cells.Next(),
        });
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        return await LoginAsync(ownerEmail, "owner1");
    }

    /// <summary>The Owner adds a user (password "user11") and returns that user, logged in.</summary>
    public async Task<Browser> AddUserAndLoginAsync(Browser owner, string fullName, string email, string role)
    {
        var r = await owner.PostFormAsync("/company/users/create", new()
        {
            ["Form.FullName"] = fullName,
            ["Form.Email"] = email,
            ["Form.Cell"] = Cells.Next(),
            ["Form.Password"] = "user11",
            ["Form.Role"] = role,
        });
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        return await LoginAsync(email, "user11");
    }

    /// <summary>Opens one page to get its anti-forgery token, then posts to another address (for example a page handler).</summary>
    public static async Task<HttpResponseMessage> PostToAsync(Browser b, string pageUrl, string postUrl, Dictionary<string, string> fields)
    {
        var page = await b.GetAsync(pageUrl);
        Assert.True(page.IsSuccessStatusCode, $"GET {pageUrl} returned {(int)page.StatusCode}");
        var match = Token.Match(await page.Content.ReadAsStringAsync());
        Assert.True(match.Success, $"No anti-forgery token on {pageUrl}");

        var all = new Dictionary<string, string>(fields) { ["__RequestVerificationToken"] = match.Groups[1].Value };
        return await b.Client.PostAsync(postUrl, new FormUrlEncodedContent(all));
    }

    /// <summary>The number at the end of a redirect such as /company/vehicles/edit/12.</summary>
    public static int IdFromRedirect(HttpResponseMessage r)
    {
        var location = r.Headers.Location?.OriginalString ?? "";
        var m = Regex.Match(location, @"/(\d+)$");
        Assert.True(m.Success, $"No id at the end of redirect '{location}' (status {(int)r.StatusCode})");
        return int.Parse(m.Groups[1].Value);
    }
}
