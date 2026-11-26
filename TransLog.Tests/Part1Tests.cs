using System.Net;
using System.Text.RegularExpressions;

namespace TransLog.Tests;

public class Part1Tests(TestApp app) : IClassFixture<TestApp>
{
    private static string Path(HttpResponseMessage r) => r.Headers.Location?.OriginalString ?? "";

    private async Task<Browser> AdminAsync()
    {
        var b = app.NewBrowser();
        var r = await b.LoginAsync(TestApp.AdminEmail, TestApp.AdminPassword);
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        return b;
    }

    /// <summary>Creates a company with an owner; returns the company id.</summary>
    private static async Task<int> CreateCompanyAsync(Browser admin, string name, string ownerEmail, string password)
    {
        var r = await admin.PostFormAsync("/sysadmin/companies/create", new()
        {
            ["Form.Name"] = name,
            ["Owner.FullName"] = "Owner of " + name,
            ["Owner.Email"] = ownerEmail,
            ["Owner.Password"] = password,
        });
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        var m = Regex.Match(Path(r), @"/sysadmin/companies/edit/(\d+)");
        Assert.True(m.Success, "Unexpected redirect: " + Path(r));
        return int.Parse(m.Groups[1].Value);
    }

    [Fact]
    public async Task Landing_page_has_the_WhatsApp_register_link()
    {
        var html = await app.NewBrowser().Client.GetStringAsync("/");
        Assert.Contains("https://wa.me/27713398433", html);
    }

    [Fact]
    public async Task Company_area_needs_a_login()
    {
        var r = await app.NewBrowser().GetAsync("/company");
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.Contains("/login", Path(r));
    }

    [Fact]
    public async Task Wrong_password_shows_one_generic_message()
    {
        var b = app.NewBrowser();
        var wrongPassword = await b.LoginAsync(TestApp.AdminEmail, "not-the-password");
        var unknownUser = await b.LoginAsync("nobody@translog.test", "whatever1");

        Assert.Equal(HttpStatusCode.OK, wrongPassword.StatusCode);
        Assert.Contains("Login failed", await wrongPassword.Content.ReadAsStringAsync());
        Assert.Contains("Login failed", await unknownUser.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task System_admin_is_sent_to_the_SysAdmin_area()
    {
        var b = app.NewBrowser();
        var login = await b.LoginAsync(TestApp.AdminEmail, TestApp.AdminPassword);
        Assert.Equal("/go", Path(login));

        var go = await b.GetAsync("/go");
        Assert.Equal("/sysadmin", Path(go));

        var page = await b.GetAsync("/sysadmin");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("Companies", await page.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Owner_can_log_in_but_cannot_open_SysAdmin()
    {
        var admin = await AdminAsync();
        await CreateCompanyAsync(admin, "Karoo Haulage", "owner1@karoo.test", "karoo1");

        var owner = app.NewBrowser();
        Assert.Equal(HttpStatusCode.Redirect, (await owner.LoginAsync("owner1@karoo.test", "karoo1")).StatusCode);
        Assert.Equal("/company", Path(await owner.GetAsync("/go")));

        var home = await owner.GetAsync("/company");
        Assert.Equal(HttpStatusCode.OK, home.StatusCode);
        Assert.Contains("Karoo Haulage", await home.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync("/sysadmin")).StatusCode);
    }

    [Fact]
    public async Task Switched_off_company_cannot_log_in()
    {
        var admin = await AdminAsync();
        var id = await CreateCompanyAsync(admin, "Vaal Freight", "owner2@vaal.test", "vaal12");

        var save = await admin.PostFormAsync($"/sysadmin/companies/edit/{id}", new()
        {
            ["Form.Name"] = "Vaal Freight",
            ["IsActive"] = "false",
        });
        Assert.Equal(HttpStatusCode.Redirect, save.StatusCode);

        var login = await app.NewBrowser().LoginAsync("owner2@vaal.test", "vaal12");
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Contains("Login failed", await login.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task System_admin_can_set_any_password_for_a_user()
    {
        var admin = await AdminAsync();
        var id = await CreateCompanyAsync(admin, "Bosveld Trucking", "owner3@bosveld.test", "first1");

        var companyPage = await (await admin.GetAsync($"/sysadmin/companies/edit/{id}")).Content.ReadAsStringAsync();
        var userId = Regex.Match(companyPage, @"/sysadmin/users/edit/(\d+)").Groups[1].Value;
        Assert.False(string.IsNullOrEmpty(userId), "No user link on the company page");

        var save = await admin.PostFormAsync($"/sysadmin/users/edit/{userId}", new()
        {
            ["Form.FullName"] = "Owner of Bosveld Trucking",
            ["Form.Email"] = "owner3@bosveld.test",
            ["Form.Role"] = "Owner",
            ["Form.IsActive"] = "true",
            ["Form.NewPassword"] = "second2",
        });
        Assert.Equal(HttpStatusCode.Redirect, save.StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await app.NewBrowser().LoginAsync("owner3@bosveld.test", "first1")).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await app.NewBrowser().LoginAsync("owner3@bosveld.test", "second2")).StatusCode);
    }

    [Fact]
    public async Task User_can_change_their_own_password()
    {
        var admin = await AdminAsync();
        await CreateCompanyAsync(admin, "Lowveld Logistics", "owner4@lowveld.test", "start1");

        var owner = app.NewBrowser();
        await owner.LoginAsync("owner4@lowveld.test", "start1");
        var change = await owner.PostFormAsync("/account/changepassword", new()
        {
            ["CurrentPassword"] = "start1",
            ["NewPassword"] = "mine22",
            ["ConfirmPassword"] = "mine22",
        });
        Assert.Equal(HttpStatusCode.Redirect, change.StatusCode);

        Assert.Equal(HttpStatusCode.Redirect, (await app.NewBrowser().LoginAsync("owner4@lowveld.test", "mine22")).StatusCode);
    }

    [Fact]
    public async Task Five_wrong_passwords_lock_the_account()
    {
        var admin = await AdminAsync();
        await CreateCompanyAsync(admin, "Garden Route Transport", "owner5@garden.test", "right1");

        var b = app.NewBrowser();
        for (var i = 0; i < 5; i++)
            await b.LoginAsync("owner5@garden.test", "wrong!");

        var correct = await b.LoginAsync("owner5@garden.test", "right1");
        Assert.Equal(HttpStatusCode.OK, correct.StatusCode);
        Assert.Contains("Too many attempts", await correct.Content.ReadAsStringAsync());
    }
}
