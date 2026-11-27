using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TransLog.Web.Data;

namespace TransLog.Tests;

/// <summary>Part 3b: linking drivers to vehicles, link history, driver logins and the "My vehicle" page.</summary>
public class Part3bTests(TestApp app) : IClassFixture<TestApp>
{
    private readonly Scenario _s = new(app);

    private async Task<T> InDbAsync<T>(Func<AppDbContext, Task<T>> query)
    {
        using var scope = app.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private static async Task<int> AddVehicleAsync(Browser owner, string registration)
    {
        var r = await owner.PostFormAsync("/company/vehicles/create", new() { ["Form.RegistrationNo"] = registration });
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        return Scenario.IdFromRedirect(r);
    }

    private async Task<int> AddDriverAsync(Browser owner, string first, string surname, string cell, string? email = null)
    {
        var fields = new Dictionary<string, string>
        {
            ["Form.FirstName"] = first,
            ["Form.Surname"] = surname,
            ["Form.Cell"] = cell,
        };
        if (email is not null)
            fields["Form.Email"] = email;
        Assert.Equal(HttpStatusCode.Redirect, (await owner.PostFormAsync("/company/drivers/create", fields)).StatusCode);
        return await InDbAsync(db => db.Drivers.IgnoreQueryFilters().Where(d => d.Surname == surname).Select(d => d.Id).SingleAsync());
    }

    private static Task<HttpResponseMessage> LinkAsync(Browser who, int driverId, int vehicleId, bool confirm = false)
    {
        var fields = new Dictionary<string, string> { ["vehicleId"] = vehicleId.ToString() };
        if (confirm)
            fields["confirm"] = "true";
        return Scenario.PostToAsync(who, $"/company/drivers/edit/{driverId}", $"/company/drivers/edit/{driverId}?handler=Link", fields);
    }

    private Task<List<DriverVehicleLink>> OpenLinksAsync(int driverId) =>
        InDbAsync(db => db.DriverVehicleLinks.IgnoreQueryFilters().Where(l => l.DriverId == driverId && l.UnlinkedAt == null).ToListAsync());

    private Task<int> AllLinksAsync(int driverId) =>
        InDbAsync(db => db.DriverVehicleLinks.IgnoreQueryFilters().CountAsync(l => l.DriverId == driverId));

    [Fact]
    public async Task Company_links_and_unlinks_and_the_history_keeps_both()
    {
        var owner = await _s.CompanyWithOwnerAsync("Link Test One", "owner@link-one.test");
        var vehicle = await AddVehicleAsync(owner, "LK 100 GP");
        var driver = await AddDriverAsync(owner, "Thabo", "Linkone", "0821110001");

        Assert.Equal(HttpStatusCode.Redirect, (await LinkAsync(owner, driver, vehicle)).StatusCode);
        var open = Assert.Single(await OpenLinksAsync(driver));
        Assert.Equal(vehicle, open.VehicleId);
        Assert.Equal(LinkSource.Company, open.LinkedBy);
        Assert.NotNull(open.LinkedByUserId);

        var list = await (await owner.GetAsync("/company/drivers")).Content.ReadAsStringAsync();
        Assert.Contains("LK100GP", list);

        var unlink = await Scenario.PostToAsync(owner, $"/company/drivers/edit/{driver}", $"/company/drivers/edit/{driver}?handler=Unlink", new());
        Assert.Equal(HttpStatusCode.Redirect, unlink.StatusCode);
        Assert.Empty(await OpenLinksAsync(driver));
        Assert.Equal(1, await AllLinksAsync(driver)); // closed, not deleted

        var history = await (await owner.GetAsync($"/company/links?driverId={driver}")).Content.ReadAsStringAsync();
        Assert.Contains("Thabo Linkone", history);
        Assert.Contains("LK100GP", history);
        Assert.DoesNotContain(">Now<", history);
    }

    [Fact]
    public async Task Linking_another_vehicle_closes_the_old_link()
    {
        var owner = await _s.CompanyWithOwnerAsync("Link Test Two", "owner@link-two.test");
        var v1 = await AddVehicleAsync(owner, "LK 200 GP");
        var v2 = await AddVehicleAsync(owner, "LK 201 GP");
        var driver = await AddDriverAsync(owner, "Sipho", "Linktwo", "0821110002");

        await LinkAsync(owner, driver, v1);
        await LinkAsync(owner, driver, v2);

        var open = Assert.Single(await OpenLinksAsync(driver));
        Assert.Equal(v2, open.VehicleId);
        Assert.Equal(2, await AllLinksAsync(driver));
    }

    [Fact]
    public async Task Taking_a_vehicle_from_another_driver_needs_a_confirm()
    {
        var owner = await _s.CompanyWithOwnerAsync("Link Test Three", "owner@link-three.test");
        var vehicle = await AddVehicleAsync(owner, "LK 300 GP");
        var first = await AddDriverAsync(owner, "Anna", "Firstdriver", "0821110003");
        var second = await AddDriverAsync(owner, "Ben", "Seconddriver", "0821110004");
        await LinkAsync(owner, first, vehicle);

        var ask = await LinkAsync(owner, second, vehicle);
        Assert.Equal(HttpStatusCode.OK, ask.StatusCode);
        var html = await ask.Content.ReadAsStringAsync();
        Assert.Contains("Take over", html);
        Assert.Contains("Anna Firstdriver", html);
        Assert.Contains("value=\"Ben\"", html); // the driver's details still show under the question
        Assert.Single(await OpenLinksAsync(first)); // nothing changed yet
        Assert.Empty(await OpenLinksAsync(second));

        Assert.Equal(HttpStatusCode.Redirect, (await LinkAsync(owner, second, vehicle, confirm: true)).StatusCode);
        Assert.Empty(await OpenLinksAsync(first));
        Assert.Single(await OpenLinksAsync(second));
    }

    [Fact]
    public async Task A_vehicle_from_another_company_cannot_be_linked()
    {
        var ownerA = await _s.CompanyWithOwnerAsync("Link Test Four A", "owner@link-four-a.test");
        var ownerB = await _s.CompanyWithOwnerAsync("Link Test Four B", "owner@link-four-b.test");
        var theirVehicle = await AddVehicleAsync(ownerA, "LK 400 GP");
        var myDriver = await AddDriverAsync(ownerB, "Carl", "Linkfour", "0821110005");

        var r = await LinkAsync(ownerB, myDriver, theirVehicle);
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
        Assert.Equal(0, await AllLinksAsync(myDriver));
    }

    [Fact]
    public async Task Driver_gets_a_login_and_picks_a_vehicle_unless_the_company_decides()
    {
        var owner = await _s.CompanyWithOwnerAsync("Link Test Five", "owner@link-five.test");
        var vehicle = await AddVehicleAsync(owner, "LK 500 GP");
        var driver = await AddDriverAsync(owner, "Dina", "Linkfive", "0821110006", "dina@link-five.test");

        var give = await Scenario.PostToAsync(owner, $"/company/drivers/edit/{driver}",
            $"/company/drivers/edit/{driver}?handler=GiveLogin", new() { ["password"] = "driver1" });
        Assert.Equal(HttpStatusCode.Redirect, give.StatusCode);
        var after = await (await owner.GetAsync($"/company/drivers/edit/{driver}")).Content.ReadAsStringAsync();
        Assert.Contains("driver1", after); // the one-time details to copy
        Assert.NotNull(await InDbAsync(db => db.Drivers.IgnoreQueryFilters().Where(d => d.Id == driver).Select(d => d.UserId).SingleAsync()));

        var phone = await _s.LoginAsync("dina@link-five.test", "driver1");
        var mine = await (await phone.GetAsync("/company/myvehicle")).Content.ReadAsStringAsync();
        Assert.Contains("You have no vehicle yet", mine);

        var pick = await Scenario.PostToAsync(phone, "/company/myvehicle", "/company/myvehicle?handler=Link", new() { ["vehicleId"] = vehicle.ToString() });
        Assert.Equal(HttpStatusCode.Redirect, pick.StatusCode);
        var open = Assert.Single(await OpenLinksAsync(driver));
        Assert.Equal(LinkSource.Driver, open.LinkedBy);

        // The Owner sets "company only": the driver can no longer change anything.
        var settings = await owner.PostFormAsync("/company/settings", new() { ["Form.DriverLinking"] = "CompanyOnly" });
        Assert.Equal(HttpStatusCode.Redirect, settings.StatusCode);

        var blocked = await Scenario.PostToAsync(phone, "/company/myvehicle", "/company/myvehicle?handler=Unlink", new());
        Assert.Equal(HttpStatusCode.Redirect, blocked.StatusCode);
        Assert.Single(await OpenLinksAsync(driver)); // still linked
        var text = await (await phone.GetAsync("/company/myvehicle")).Content.ReadAsStringAsync();
        Assert.Contains("Your company links drivers to vehicles", text);
    }

    [Fact]
    public async Task Switching_a_driver_off_ends_the_link_and_the_login()
    {
        var owner = await _s.CompanyWithOwnerAsync("Link Test Six", "owner@link-six.test");
        var vehicle = await AddVehicleAsync(owner, "LK 600 GP");
        var driver = await AddDriverAsync(owner, "Eve", "Linksix", "0821110007", "eve@link-six.test");
        await LinkAsync(owner, driver, vehicle);
        await Scenario.PostToAsync(owner, $"/company/drivers/edit/{driver}",
            $"/company/drivers/edit/{driver}?handler=GiveLogin", new() { ["password"] = "driver1" });

        // A browser sends the switch's hidden "false" when it is off.
        var off = await owner.PostFormAsync($"/company/drivers/edit/{driver}", new()
        {
            ["Form.FirstName"] = "Eve",
            ["Form.Surname"] = "Linksix",
            ["Form.Cell"] = "0821110007",
            ["Form.Email"] = "eve@link-six.test",
            ["Form.IsActive"] = "false",
        });
        Assert.Equal(HttpStatusCode.Redirect, off.StatusCode);

        Assert.Empty(await OpenLinksAsync(driver));
        var login = await app.NewBrowser().LoginAsync("eve@link-six.test", "driver1");
        Assert.NotEqual(HttpStatusCode.Redirect, login.StatusCode);
    }

    [Fact]
    public async Task A_login_with_no_driver_record_gets_an_explanation()
    {
        var owner = await _s.CompanyWithOwnerAsync("Link Test Seven", "owner@link-seven.test");
        var capturer = await _s.AddUserAndLoginAsync(owner, "Lone Capturer", "lone@link-seven.test", "Capturer");

        var html = await (await capturer.GetAsync("/company/myvehicle")).Content.ReadAsStringAsync();
        Assert.Contains("not linked to a driver", html);
    }

    [Fact]
    public async Task A_login_cannot_be_given_without_an_email()
    {
        var owner = await _s.CompanyWithOwnerAsync("Link Test Eight", "owner@link-eight.test");
        var driver = await AddDriverAsync(owner, "Fred", "Linkeight", "0821110008");

        var give = await Scenario.PostToAsync(owner, $"/company/drivers/edit/{driver}",
            $"/company/drivers/edit/{driver}?handler=GiveLogin", new() { ["password"] = "driver1" });
        Assert.Equal(HttpStatusCode.Redirect, give.StatusCode);

        var page = await (await owner.GetAsync($"/company/drivers/edit/{driver}")).Content.ReadAsStringAsync();
        Assert.Contains("Add the driver", page);
        Assert.Null(await InDbAsync(db => db.Drivers.IgnoreQueryFilters().Where(d => d.Id == driver).Select(d => d.UserId).SingleAsync()));
    }
}
