using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TransLog.Web.Data;

namespace TransLog.Tests;

/// <summary>Part 3c: unique cell numbers, login by email or cell, the Driver role and area, area bars, the vehicle model list.</summary>
public class Part3cTests(TestApp app) : IClassFixture<TestApp>
{
    private readonly Scenario _s = new(app);

    private async Task<T> InDbAsync<T>(Func<AppDbContext, Task<T>> query)
    {
        using var scope = app.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private async Task<int> AddDriverAsync(Browser owner, string first, string surname, string cell, string? email = null)
    {
        var fields = new Dictionary<string, string> { ["Form.FirstName"] = first, ["Form.Surname"] = surname, ["Form.Cell"] = cell };
        if (email is not null)
            fields["Form.Email"] = email;
        Assert.Equal(HttpStatusCode.Redirect, (await owner.PostFormAsync("/company/drivers/create", fields)).StatusCode);
        return await InDbAsync(db => db.Drivers.IgnoreQueryFilters().Where(d => d.Surname == surname).Select(d => d.Id).SingleAsync());
    }

    private static Task<HttpResponseMessage> GiveLoginAsync(Browser owner, int driverId) =>
        Scenario.PostToAsync(owner, $"/company/drivers/edit/{driverId}",
            $"/company/drivers/edit/{driverId}?handler=GiveLogin", new() { ["password"] = "driver1" });

    [Fact]
    public async Task A_cell_number_belongs_to_one_user_on_the_whole_platform()
    {
        var admin = await _s.AdminAsync();
        var first = await admin.PostFormAsync("/sysadmin/companies/create", new()
        {
            ["Form.Name"] = "Cell Test One",
            ["Owner.FullName"] = "First Owner",
            ["Owner.Email"] = "owner@cell-one.test",
            ["Owner.Password"] = "owner1",
            ["Owner.Cell"] = "083 444 0001",
        });
        Assert.Equal(HttpStatusCode.Redirect, first.StatusCode);

        // Another company's Owner with the same number, typed differently: refused, and the company is not made.
        var second = await admin.PostFormAsync("/sysadmin/companies/create", new()
        {
            ["Form.Name"] = "Cell Test Two",
            ["Owner.FullName"] = "Second Owner",
            ["Owner.Email"] = "owner@cell-two.test",
            ["Owner.Password"] = "owner1",
            ["Owner.Cell"] = "+27 83 444 0001",
        });
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Contains("already used by another TransLog account", await second.Content.ReadAsStringAsync());
        Assert.False(await InDbAsync(db => db.Companies.AnyAsync(c => c.Name == "Cell Test Two")));

        // A company user with that number: refused too.
        var owner = await _s.LoginAsync("owner@cell-one.test", "owner1");
        var user = await owner.PostFormAsync("/company/users/create", new()
        {
            ["Form.FullName"] = "Same Cell",
            ["Form.Email"] = "same@cell-one.test",
            ["Form.Cell"] = "0834440001",
            ["Form.Password"] = "user11",
            ["Form.Role"] = "Viewer",
        });
        Assert.Equal(HttpStatusCode.OK, user.StatusCode);
        Assert.Contains("already used by another TransLog account", await user.Content.ReadAsStringAsync());

        // Every user needs a cell number.
        var none = await owner.PostFormAsync("/company/users/create", new()
        {
            ["Form.FullName"] = "No Cell",
            ["Form.Email"] = "nocell@cell-one.test",
            ["Form.Password"] = "user11",
            ["Form.Role"] = "Viewer",
        });
        Assert.Contains("Enter the cell number", await none.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Users_log_in_with_email_or_cell_number()
    {
        var admin = await _s.AdminAsync();
        await admin.PostFormAsync("/sysadmin/companies/create", new()
        {
            ["Form.Name"] = "Cell Test Three",
            ["Owner.FullName"] = "Third Owner",
            ["Owner.Email"] = "owner@cell-three.test",
            ["Owner.Password"] = "owner1",
            ["Owner.Cell"] = "0834440003",
        });

        Assert.Equal(HttpStatusCode.Redirect, (await app.NewBrowser().LoginAsync("owner@cell-three.test", "owner1")).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await app.NewBrowser().LoginAsync("083 444 0003", "owner1")).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await app.NewBrowser().LoginAsync("27834440003", "owner1")).StatusCode);

        var wrong = await app.NewBrowser().LoginAsync("0834440003", "wrong!");
        Assert.Contains("Login failed", await wrong.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_driver_login_gets_the_Driver_role_and_only_the_driver_pages()
    {
        var owner = await _s.CompanyWithOwnerAsync("Driver Test One", "owner@driver-one.test");
        var driver = await AddDriverAsync(owner, "Gift", "Driverone", "0824440001");
        Assert.Equal(HttpStatusCode.Redirect, (await GiveLoginAsync(owner, driver)).StatusCode);

        using (var scope = app.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var login = await users.Users.SingleAsync(u => u.PhoneNumber == "0824440001");
            Assert.Equal(new[] { "Driver" }, (await users.GetRolesAsync(login)).ToArray());
        }

        var phone = await _s.LoginAsync("0824440001", "driver1");
        Assert.Equal("/driver", (await phone.GetAsync("/go")).Headers.Location?.OriginalString);
        var home = await (await phone.GetAsync("/driver")).Content.ReadAsStringAsync();
        Assert.Contains("data-area=\"driver\"", home);
        Assert.Contains("Gift Driverone", home);
        Assert.Equal(HttpStatusCode.OK, (await phone.GetAsync("/driver/myvehicle")).StatusCode);

        // No company pages, no system admin pages.
        Assert.Equal(HttpStatusCode.NotFound, (await phone.GetAsync("/company")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await phone.GetAsync("/company/vehicles")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await phone.GetAsync("/sysadmin")).StatusCode);
    }

    [Fact]
    public async Task A_driver_cannot_get_a_login_with_a_cell_number_another_company_uses()
    {
        var ownerA = await _s.CompanyWithOwnerAsync("Driver Test Two A", "owner@driver-two-a.test");
        var ownerB = await _s.CompanyWithOwnerAsync("Driver Test Two B", "owner@driver-two-b.test");
        var inA = await AddDriverAsync(ownerA, "Moved", "Drivertwoa", "0824440002");
        Assert.Equal(HttpStatusCode.Redirect, (await GiveLoginAsync(ownerA, inA)).StatusCode);

        // B may keep a driver record with the same number, but cannot give it a login.
        var inB = await AddDriverAsync(ownerB, "Moved", "Drivertwob", "0824440002");
        await GiveLoginAsync(ownerB, inB);
        var page = await (await ownerB.GetAsync($"/company/drivers/edit/{inB}")).Content.ReadAsStringAsync();
        Assert.Contains("already used by another TransLog account", page);
        Assert.DoesNotContain("Driver Test Two A", page);
        Assert.Null(await InDbAsync(db => db.Drivers.IgnoreQueryFilters().Where(d => d.Id == inB).Select(d => d.UserId).SingleAsync()));
    }

    [Fact]
    public async Task Changing_a_drivers_cell_number_changes_the_login()
    {
        var owner = await _s.CompanyWithOwnerAsync("Driver Test Three", "owner@driver-three.test");
        var driver = await AddDriverAsync(owner, "Hope", "Driverthree", "0824440003");
        await GiveLoginAsync(owner, driver);

        var save = await owner.PostFormAsync($"/company/drivers/edit/{driver}", new()
        {
            ["Form.FirstName"] = "Hope",
            ["Form.Surname"] = "Driverthree",
            ["Form.Cell"] = "0824440033",
            ["Form.IsActive"] = "true",
        });
        Assert.Equal(HttpStatusCode.Redirect, save.StatusCode);

        Assert.NotEqual(HttpStatusCode.Redirect, (await app.NewBrowser().LoginAsync("0824440003", "driver1")).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await app.NewBrowser().LoginAsync("0824440033", "driver1")).StatusCode);
    }

    [Fact]
    public async Task Each_area_shows_its_own_bar()
    {
        var admin = await _s.AdminAsync();
        Assert.Contains("data-area=\"sysadmin\"", await (await admin.GetAsync("/sysadmin")).Content.ReadAsStringAsync());

        var owner = await _s.CompanyWithOwnerAsync("Area Test One", "owner@area-one.test");
        var home = await (await owner.GetAsync("/company")).Content.ReadAsStringAsync();
        Assert.Contains("data-area=\"company\"", home);
        Assert.Contains("Area Test One", home);
        Assert.Contains("#buildings", home);

        // Visitors see no bar.
        Assert.DoesNotContain("data-area=", await app.NewBrowser().Client.GetStringAsync("/"));
    }

    [Fact]
    public async Task Vehicles_pick_a_model_from_the_list_or_type_one_that_TransLog_adds()
    {
        var owner = await _s.CompanyWithOwnerAsync("Model Test One", "owner@model-one.test");
        var fh = await InDbAsync(db => db.VehicleModels.Include(m => m.Make).SingleAsync(m => m.Make!.Name == "Volvo" && m.Name == "FH"));

        // From the list: type and fuel come from the model when left empty.
        var picked = await owner.PostFormAsync("/company/vehicles/create", new()
        {
            ["Form.RegistrationNo"] = "MOD 1 GP",
            ["Form.MakeChoice"] = fh.MakeId.ToString(),
            ["Form.VehicleModelId"] = fh.Id.ToString(),
        });
        Assert.Equal(HttpStatusCode.Redirect, picked.StatusCode);
        var stored = await InDbAsync(db => db.Vehicles.IgnoreQueryFilters().SingleAsync(v => v.RegistrationNo == "MOD1GP"));
        Assert.Equal(fh.Id, stored.VehicleModelId);
        Assert.Equal("Truck tractor", stored.VehicleType);
        Assert.Equal("Diesel", stored.FuelType);
        Assert.Contains("Volvo FH", await (await owner.GetAsync("/company/vehicles")).Content.ReadAsStringAsync());

        // A model of another make is refused.
        var scania = await InDbAsync(db => db.VehicleMakes.SingleAsync(m => m.Name == "Scania"));
        var mixed = await owner.PostFormAsync("/company/vehicles/create", new()
        {
            ["Form.RegistrationNo"] = "MOD 2 GP",
            ["Form.MakeChoice"] = scania.Id.ToString(),
            ["Form.VehicleModelId"] = fh.Id.ToString(),
        });
        Assert.Equal(HttpStatusCode.OK, mixed.StatusCode);
        Assert.Contains("Choose a model of that make", await mixed.Content.ReadAsStringAsync());

        // Other: typed, then a system admin adds it to the list and the vehicle moves onto it.
        var typed = await owner.PostFormAsync("/company/vehicles/create", new()
        {
            ["Form.RegistrationNo"] = "MOD 3 GP",
            ["Form.MakeChoice"] = "other",
            ["Form.OtherMake"] = "Shacman",
            ["Form.OtherModel"] = "X3000",
        });
        Assert.Equal(HttpStatusCode.Redirect, typed.StatusCode);

        var admin = await _s.AdminAsync();
        var models = await (await admin.GetAsync("/sysadmin/models")).Content.ReadAsStringAsync();
        Assert.Contains("Shacman X3000", models);

        var adopt = await Scenario.PostToAsync(admin, "/sysadmin/models", "/sysadmin/models?handler=Adopt", new()
        {
            ["otherMake"] = "Shacman",
            ["otherModel"] = "X3000",
            ["newMake"] = "Shacman",
            ["modelName"] = "X3000",
            ["type"] = "Truck tractor",
            ["fuel"] = "Diesel",
        });
        Assert.Equal(HttpStatusCode.Redirect, adopt.StatusCode);

        var moved = await InDbAsync(db => db.Vehicles.IgnoreQueryFilters().Include(v => v.VehicleModel!).ThenInclude(m => m.Make)
            .SingleAsync(v => v.RegistrationNo == "MOD3GP"));
        Assert.Equal("Shacman", moved.VehicleModel?.Make?.Name);
        Assert.Null(moved.OtherMake);
    }

    [Fact]
    public async Task Only_system_admins_open_the_model_list()
    {
        var owner = await _s.CompanyWithOwnerAsync("Model Test Two", "owner@model-two.test");
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync("/sysadmin/models")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await (await _s.AdminAsync()).GetAsync("/sysadmin/models")).StatusCode);
    }
}
