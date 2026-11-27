using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TransLog.Web.Data;

namespace TransLog.Tests;

/// <summary>Part 2: the Owner manages company users and settings; companies never see each other.</summary>
public class Part2Tests(TestApp app) : IClassFixture<TestApp>
{
    private static string Path(HttpResponseMessage r) => r.Headers.Location?.OriginalString ?? "";

    private async Task<Browser> AdminAsync()
    {
        var b = app.NewBrowser();
        Assert.Equal(HttpStatusCode.Redirect, (await b.LoginAsync(TestApp.AdminEmail, TestApp.AdminPassword)).StatusCode);
        return b;
    }

    private async Task<Browser> LoginAsync(string email, string password)
    {
        var b = app.NewBrowser();
        var r = await b.LoginAsync(email, password);
        Assert.True(r.StatusCode == HttpStatusCode.Redirect, $"Login failed for {email}");
        return b;
    }

    /// <summary>A system admin creates a company and its Owner; returns the Owner, logged in.</summary>
    private async Task<Browser> CompanyWithOwnerAsync(string name, string ownerEmail)
    {
        var r = await (await AdminAsync()).PostFormAsync("/sysadmin/companies/create", new()
        {
            ["Form.Name"] = name,
            ["Owner.FullName"] = "Owner of " + name,
            ["Owner.Email"] = ownerEmail,
            ["Owner.Password"] = "owner1",
        });
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        return await LoginAsync(ownerEmail, "owner1");
    }

    private static async Task<HttpResponseMessage> AddUserAsync(Browser owner, string fullName, string email, string role, string password = "user11") =>
        await owner.PostFormAsync("/company/users/create", new()
        {
            ["Form.FullName"] = fullName,
            ["Form.Email"] = email,
            ["Form.Password"] = password,
            ["Form.Role"] = role,
        });

    private async Task<int> UserIdAsync(string email)
    {
        using var scope = app.Services.CreateScope();
        var user = await scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>().FindByEmailAsync(email);
        Assert.NotNull(user);
        return user.Id;
    }

    [Fact]
    public async Task Owner_adds_a_user_and_gets_login_details_to_copy()
    {
        var owner = await CompanyWithOwnerAsync("Kalahari Carriers", "owner@kalahari.test");

        var add = await AddUserAsync(owner, "Sipho Capturer", "sipho@kalahari.test", "Capturer", "sipho1");
        Assert.Equal(HttpStatusCode.Redirect, add.StatusCode);
        Assert.Equal("/company/users", Path(add));

        var list = await (await owner.GetAsync("/company/users")).Content.ReadAsStringAsync();
        Assert.Contains("Copy details", list);
        Assert.Contains("https://localhost/login", list);
        Assert.Contains("Password: sipho1", list);
        Assert.Contains("Kalahari Carriers", list);

        // Shown once only.
        var again = await (await owner.GetAsync("/company/users")).Content.ReadAsStringAsync();
        Assert.DoesNotContain("Password: sipho1", again);

        // The new user logs in and lands on the company home page.
        var capturer = await LoginAsync("sipho@kalahari.test", "sipho1");
        Assert.Equal("/company", Path(await capturer.GetAsync("/go")));
        Assert.Equal(HttpStatusCode.OK, (await capturer.GetAsync("/company")).StatusCode);
    }

    [Fact]
    public async Task Only_the_Owner_opens_users_settings_and_activity()
    {
        var owner = await CompanyWithOwnerAsync("Drakensberg Haulers", "owner@drakensberg.test");
        await AddUserAsync(owner, "Anna Admin", "anna@drakensberg.test", "Admin");
        await AddUserAsync(owner, "Victor Viewer", "victor@drakensberg.test", "Viewer");

        foreach (var email in new[] { "anna@drakensberg.test", "victor@drakensberg.test" })
        {
            var b = await LoginAsync(email, "user11");
            Assert.Equal(HttpStatusCode.OK, (await b.GetAsync("/company")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync("/company/users")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync("/company/users/create")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync("/company/settings")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync("/company/activity")).StatusCode);
        }
    }

    [Fact]
    public async Task Owner_cannot_see_or_open_another_companys_users()
    {
        var ownerA = await CompanyWithOwnerAsync("Atlantic Movers", "owner@atlantic.test");
        var ownerB = await CompanyWithOwnerAsync("Bushveld Express", "owner@bushveld.test");
        await AddUserAsync(ownerA, "Only In A", "only-a@atlantic.test", "Capturer");

        var listB = await (await ownerB.GetAsync("/company/users")).Content.ReadAsStringAsync();
        Assert.DoesNotContain("only-a@atlantic.test", listB);
        Assert.DoesNotContain("owner@atlantic.test", listB);

        // Typing A's user id into the address gives "not found".
        var idInA = await UserIdAsync("only-a@atlantic.test");
        Assert.Equal(HttpStatusCode.NotFound, (await ownerB.GetAsync($"/company/users/edit/{idInA}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ownerA.GetAsync($"/company/users/edit/{idInA}")).StatusCode);
    }

    [Fact]
    public async Task Owners_are_managed_by_TransLog_only()
    {
        var owner = await CompanyWithOwnerAsync("Cape Fleet Services", "owner@capefleet.test");

        // The Owner cannot open their own account (or any Owner) in the company area.
        var ownId = await UserIdAsync("owner@capefleet.test");
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"/company/users/edit/{ownId}")).StatusCode);

        // Nor create another Owner.
        var r = await AddUserAsync(owner, "Second Owner", "second@capefleet.test", "Owner");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Contains("Choose a role", await r.Content.ReadAsStringAsync());
        using var scope = app.Services.CreateScope();
        Assert.Null(await scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>().FindByEmailAsync("second@capefleet.test"));
    }

    [Fact]
    public async Task Owner_can_switch_a_user_off_and_set_a_new_password()
    {
        var owner = await CompanyWithOwnerAsync("Highveld Tankers", "owner@highveld.test");
        await AddUserAsync(owner, "Thabo Driver", "thabo@highveld.test", "Capturer", "thabo1");
        var id = await UserIdAsync("thabo@highveld.test");

        // New password: the login details are shown again.
        var save = await owner.PostFormAsync($"/company/users/edit/{id}", new()
        {
            ["Form.FullName"] = "Thabo Driver",
            ["Form.Email"] = "thabo@highveld.test",
            ["Form.Role"] = "Capturer",
            ["Form.IsActive"] = "true",
            ["Form.NewPassword"] = "thabo2",
        });
        Assert.Equal(HttpStatusCode.Redirect, save.StatusCode);
        Assert.Contains("Password: thabo2", await (await owner.GetAsync("/company/users")).Content.ReadAsStringAsync());
        await LoginAsync("thabo@highveld.test", "thabo2");

        // Switched off: login fails.
        var off = await owner.PostFormAsync($"/company/users/edit/{id}", new()
        {
            ["Form.FullName"] = "Thabo Driver",
            ["Form.Email"] = "thabo@highveld.test",
            ["Form.Role"] = "Capturer",
            ["Form.IsActive"] = "false",
        });
        Assert.Equal(HttpStatusCode.Redirect, off.StatusCode);
        var login = await app.NewBrowser().LoginAsync("thabo@highveld.test", "thabo2");
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Contains("Login failed", await login.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Settings_save_contact_details_and_linking_but_never_the_name()
    {
        var owner = await CompanyWithOwnerAsync("Overberg Logistics", "owner@overberg.test");

        var r = await owner.PostFormAsync("/company/settings", new()
        {
            ["Form.Phone"] = "021 555 0101",
            ["Form.DriverLinking"] = "CompanyOnly",
            ["Form.Name"] = "Renamed By Owner", // not on the form; must be ignored
            ["Name"] = "Renamed By Owner",
        });
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);

        using var scope = app.Services.CreateScope();
        var company = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Companies
            .SingleAsync(c => c.Name == "Overberg Logistics");
        Assert.Equal("021 555 0101", company.Phone);
        Assert.Equal(DriverLinking.CompanyOnly, company.DriverLinking);
    }

    [Fact]
    public async Task Activity_shows_only_the_own_companys_entries()
    {
        var ownerA = await CompanyWithOwnerAsync("Namaqua Transport", "owner@namaqua.test");
        var ownerB = await CompanyWithOwnerAsync("Zululand Freight", "owner@zululand.test");
        await AddUserAsync(ownerA, "Mine A", "mine-a@namaqua.test", "Viewer");
        await AddUserAsync(ownerB, "Theirs B", "theirs-b@zululand.test", "Viewer");

        var activityA = await (await ownerA.GetAsync("/company/activity")).Content.ReadAsStringAsync();
        Assert.Contains("mine-a@namaqua.test", activityA);
        Assert.DoesNotContain("theirs-b@zululand.test", activityA);
        Assert.DoesNotContain("Zululand", activityA);

        // The system admin still sees everything.
        var all = await (await (await AdminAsync()).GetAsync("/sysadmin/audit")).Content.ReadAsStringAsync();
        Assert.Contains("mine-a@namaqua.test", all);
        Assert.Contains("theirs-b@zululand.test", all);
    }

    [Fact]
    public async Task Company_names_are_unique()
    {
        var admin = await AdminAsync();
        Assert.Equal(HttpStatusCode.Redirect, (await admin.PostFormAsync("/sysadmin/companies/create", new()
        {
            ["Form.Name"] = "Limpopo Lines",
            ["Owner.FullName"] = "First Owner",
            ["Owner.Email"] = "first@limpopo.test",
            ["Owner.Password"] = "owner1",
        })).StatusCode);

        // Same name in other letters: refused.
        var twin = await admin.PostFormAsync("/sysadmin/companies/create", new()
        {
            ["Form.Name"] = " limpopo lines ",
            ["Owner.FullName"] = "Second Owner",
            ["Owner.Email"] = "second@limpopo.test",
            ["Owner.Password"] = "owner1",
        });
        Assert.Equal(HttpStatusCode.OK, twin.StatusCode);
        Assert.Contains("already exists", await twin.Content.ReadAsStringAsync());

        // Renaming another company to it: refused too.
        var other = await admin.PostFormAsync("/sysadmin/companies/create", new()
        {
            ["Form.Name"] = "Mpumalanga Movers",
            ["Owner.FullName"] = "Third Owner",
            ["Owner.Email"] = "third@mpumalanga.test",
            ["Owner.Password"] = "owner1",
        });
        var editUrl = Regex.Match(Path(other), @"/sysadmin/companies/edit/\d+").Value;
        var rename = await admin.PostFormAsync(editUrl, new() { ["Form.Name"] = "Limpopo Lines", ["IsActive"] = "true" });
        Assert.Equal(HttpStatusCode.OK, rename.StatusCode);
        Assert.Contains("already exists", await rename.Content.ReadAsStringAsync());
    }
}
