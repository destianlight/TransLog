using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TransLog.Web.Data;
using TransLog.Web.Fleet;

namespace TransLog.Tests;

/// <summary>Part 3a: vehicles with names used on slips, drivers, expiry dates; companies never see each other's fleet.</summary>
public class Part3aTests(TestApp app) : IClassFixture<TestApp>
{
    private readonly Scenario _s = new(app);

    private static string Day(int fromToday) =>
        Expiry.Today().AddDays(fromToday).ToString("yyyy-MM-dd");

    private static Task<HttpResponseMessage> AddVehicleAsync(Browser owner, string registration, Dictionary<string, string>? more = null)
    {
        var fields = new Dictionary<string, string> { ["Form.RegistrationNo"] = registration };
        if (more is not null)
            foreach (var (key, value) in more)
                fields[key] = value;
        return owner.PostFormAsync("/company/vehicles/create", fields);
    }

    private static Task<HttpResponseMessage> AddDriverAsync(Browser owner, string first, string surname, string cell, string? idNumber = null)
    {
        var fields = new Dictionary<string, string>
        {
            ["Form.FirstName"] = first,
            ["Form.Surname"] = surname,
            ["Form.Cell"] = cell,
        };
        if (idNumber is not null)
            fields["Form.IdNumber"] = idNumber;
        return owner.PostFormAsync("/company/drivers/create", fields);
    }

    private static Task<HttpResponseMessage> AddAliasAsync(Browser owner, int vehicleId, string alias) =>
        Scenario.PostToAsync(owner, $"/company/vehicles/edit/{vehicleId}",
            $"/company/vehicles/edit/{vehicleId}?handler=AddAlias", new() { ["newAlias"] = alias });

    private async Task<T> InDbAsync<T>(Func<AppDbContext, Task<T>> query)
    {
        using var scope = app.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    [Fact]
    public async Task Owner_adds_a_vehicle_and_the_plate_is_stored_in_one_form()
    {
        var owner = await _s.CompanyWithOwnerAsync("Fleet Test One", "owner@fleet-one.test");

        var add = await AddVehicleAsync(owner, "llt 587-nw", new()
        {
            ["Form.Make"] = "Volvo",
            ["Form.VehicleType"] = "Truck tractor",
            ["Form.LicenceDiscExpiry"] = Day(-1),
            ["Form.RoadworthyExpiry"] = Day(10),
        });
        Assert.Equal(HttpStatusCode.Redirect, add.StatusCode);
        var id = Scenario.IdFromRedirect(add);

        var list = await (await owner.GetAsync("/company/vehicles")).Content.ReadAsStringAsync();
        Assert.Contains("LLT587NW", list);
        Assert.Contains("Volvo", list);
        Assert.Contains("Expired", list);
        Assert.Contains("Due in 10 days", list);

        var edit = await (await owner.GetAsync($"/company/vehicles/edit/{id}")).Content.ReadAsStringAsync();
        Assert.Contains("LLT587NW", edit);
        Assert.Contains("Truck tractor", edit);
    }

    [Fact]
    public async Task Duplicate_registrations_and_names_are_refused()
    {
        var owner = await _s.CompanyWithOwnerAsync("Fleet Test Two", "owner@fleet-two.test");
        var a = Scenario.IdFromRedirect(await AddVehicleAsync(owner, "CA 123 456"));
        var b = Scenario.IdFromRedirect(await AddVehicleAsync(owner, "ND 555 GP"));

        // Same plate in other letters and spacing.
        var twin = await AddVehicleAsync(owner, "ca123456");
        Assert.Equal(HttpStatusCode.OK, twin.StatusCode);
        Assert.Contains("already", await twin.Content.ReadAsStringAsync());

        // A name on slips belongs to one vehicle only.
        Assert.Equal(HttpStatusCode.Redirect, (await AddAliasAsync(owner, a, "TFN pump 1")).StatusCode);
        var again = await AddAliasAsync(owner, b, "tfnpump1");
        Assert.Equal(HttpStatusCode.Redirect, again.StatusCode);
        Assert.Contains("already used", await (await owner.GetAsync($"/company/vehicles/edit/{b}")).Content.ReadAsStringAsync());

        // A name cannot be another vehicle's plate, and a plate cannot be an existing name.
        await AddAliasAsync(owner, a, "ND555GP");
        Assert.Contains("already used", await (await owner.GetAsync($"/company/vehicles/edit/{a}")).Content.ReadAsStringAsync());
        var plateIsName = await AddVehicleAsync(owner, "TFNPUMP1");
        Assert.Equal(HttpStatusCode.OK, plateIsName.StatusCode);
        Assert.Contains("already", await plateIsName.Content.ReadAsStringAsync());

        // Editing a vehicle into another's plate is refused too.
        var edit = await owner.PostFormAsync($"/company/vehicles/edit/{b}", new()
        {
            ["Form.RegistrationNo"] = "CA123456",
            ["Form.IsActive"] = "true",
        });
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        Assert.Contains("already", await edit.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Names_used_on_slips_can_be_added_and_removed()
    {
        var owner = await _s.CompanyWithOwnerAsync("Fleet Test Three", "owner@fleet-three.test");
        var id = Scenario.IdFromRedirect(await AddVehicleAsync(owner, "LLT587NW"));

        Assert.Equal(HttpStatusCode.Redirect, (await AddAliasAsync(owner, id, "LLT587NM")).StatusCode);
        var page = await (await owner.GetAsync($"/company/vehicles/edit/{id}")).Content.ReadAsStringAsync();
        Assert.Contains("LLT587NM", page);
        Assert.Contains("LLT587NM", await (await owner.GetAsync("/company/vehicles")).Content.ReadAsStringAsync());

        var aliasId = await InDbAsync(db => db.VehicleAliases.IgnoreQueryFilters().Where(a => a.Alias == "LLT587NM").Select(a => a.Id).SingleAsync());
        var remove = await Scenario.PostToAsync(owner, $"/company/vehicles/edit/{id}",
            $"/company/vehicles/edit/{id}?handler=RemoveAlias&aliasId={aliasId}", new());
        Assert.Equal(HttpStatusCode.Redirect, remove.StatusCode);

        // The "removed" message is shown once, on the edit page; read it away before checking the list.
        var after = await (await owner.GetAsync($"/company/vehicles/edit/{id}")).Content.ReadAsStringAsync();
        Assert.Contains("LLT587NM removed", after);
        Assert.DoesNotContain("LLT587NM", await (await owner.GetAsync("/company/vehicles")).Content.ReadAsStringAsync());
        Assert.Equal(0, await InDbAsync(db => db.VehicleAliases.IgnoreQueryFilters().CountAsync(a => a.Alias == "LLT587NM")));
    }

    [Fact]
    public async Task Companies_never_see_each_others_vehicles_or_drivers()
    {
        var ownerA = await _s.CompanyWithOwnerAsync("Fleet Test Four A", "owner@fleet-four-a.test");
        var ownerB = await _s.CompanyWithOwnerAsync("Fleet Test Four B", "owner@fleet-four-b.test");

        var vehicleA = Scenario.IdFromRedirect(await AddVehicleAsync(ownerA, "AAA111"));
        Assert.Equal(HttpStatusCode.Redirect, (await AddDriverAsync(ownerA, "Alice", "Aardvark", "0831110001")).StatusCode);
        var driverA = await InDbAsync(db => db.Drivers.IgnoreQueryFilters().Where(d => d.Surname == "Aardvark").Select(d => d.Id).SingleAsync());

        // B sees nothing of A, and typing A's ids into the address gives "not found".
        Assert.DoesNotContain("AAA111", await (await ownerB.GetAsync("/company/vehicles")).Content.ReadAsStringAsync());
        Assert.DoesNotContain("Aardvark", await (await ownerB.GetAsync("/company/drivers")).Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await ownerB.GetAsync($"/company/vehicles/edit/{vehicleA}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ownerB.GetAsync($"/company/drivers/edit/{driverA}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ownerA.GetAsync($"/company/vehicles/edit/{vehicleA}")).StatusCode);

        // B cannot name A's vehicle either.
        var steal = await Scenario.PostToAsync(ownerB, "/company/vehicles/create",
            $"/company/vehicles/edit/{vehicleA}?handler=AddAlias", new() { ["newAlias"] = "BBB222" });
        Assert.Equal(HttpStatusCode.NotFound, steal.StatusCode);

        // The same plate and the same cell number can exist in two companies.
        Assert.Equal(HttpStatusCode.Redirect, (await AddVehicleAsync(ownerB, "AAA111")).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await AddDriverAsync(ownerB, "Bob", "Buffalo", "0831110001")).StatusCode);
    }

    [Fact]
    public async Task Only_the_Owner_and_Admins_open_the_fleet_pages()
    {
        var owner = await _s.CompanyWithOwnerAsync("Fleet Test Five", "owner@fleet-five.test");
        var admin = await _s.AddUserAndLoginAsync(owner, "Anna Admin", "anna@fleet-five.test", "Admin");
        var capturer = await _s.AddUserAndLoginAsync(owner, "Cara Capturer", "cara@fleet-five.test", "Capturer");
        var viewer = await _s.AddUserAndLoginAsync(owner, "Vic Viewer", "vic@fleet-five.test", "Viewer");

        string[] pages = ["/company/vehicles", "/company/vehicles/create", "/company/drivers", "/company/drivers/create"];
        foreach (var url in pages)
            Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync(url)).StatusCode);

        foreach (var user in new[] { capturer, viewer })
            foreach (var url in pages)
                Assert.Equal(HttpStatusCode.NotFound, (await user.GetAsync(url)).StatusCode);

        // An Admin can add a vehicle.
        Assert.Equal(HttpStatusCode.Redirect, (await AddVehicleAsync(admin, "ADM 1")).StatusCode);
    }

    [Fact]
    public async Task Driver_cell_number_and_ID_number_are_checked()
    {
        var owner = await _s.CompanyWithOwnerAsync("Fleet Test Six", "owner@fleet-six.test");

        var add = await AddDriverAsync(owner, "Sipho", "Dlamini", "+27 82 123 4567", "8001015009087");
        Assert.Equal(HttpStatusCode.Redirect, add.StatusCode);

        // The list shows the cell number in one form and never the ID number.
        var list = await (await owner.GetAsync("/company/drivers")).Content.ReadAsStringAsync();
        Assert.Contains("0821234567", list);
        Assert.Contains("Sipho Dlamini", list);
        Assert.DoesNotContain("8001015009087", list);

        // The edit page shows it, and the activity log never does.
        var id = await InDbAsync(db => db.Drivers.IgnoreQueryFilters().Where(d => d.Surname == "Dlamini").Select(d => d.Id).SingleAsync());
        Assert.Contains("8001015009087", await (await owner.GetAsync($"/company/drivers/edit/{id}")).Content.ReadAsStringAsync());
        Assert.DoesNotContain("8001015009087", await (await owner.GetAsync("/company/activity")).Content.ReadAsStringAsync());

        // Same cell number (typed differently), same ID number, wrong check digit, impossible date, short cell.
        var sameCell = await AddDriverAsync(owner, "Other", "Person", "082 123 4567");
        Assert.Contains("already exists", await sameCell.Content.ReadAsStringAsync());
        var sameId = await AddDriverAsync(owner, "Other", "Person", "0829990001", "800101 5009 087");
        Assert.Contains("already exists", await sameId.Content.ReadAsStringAsync());
        var badCheck = await AddDriverAsync(owner, "Other", "Person", "0829990002", "8001015009088");
        Assert.Contains("not a valid", await badCheck.Content.ReadAsStringAsync());
        var badDate = await AddDriverAsync(owner, "Other", "Person", "0829990003", "9002295124082");
        Assert.Contains("not a valid", await badDate.Content.ReadAsStringAsync());
        var shortCell = await AddDriverAsync(owner, "Other", "Person", "12345");
        Assert.Contains("South African cell number", await shortCell.Content.ReadAsStringAsync());

        // Drivers need no ID number.
        Assert.Equal(HttpStatusCode.Redirect, (await AddDriverAsync(owner, "No", "Idnumber", "0829990004")).StatusCode);
    }

    [Fact]
    public async Task Licence_and_permit_dates_show_warnings_in_the_driver_list()
    {
        var owner = await _s.CompanyWithOwnerAsync("Fleet Test Seven", "owner@fleet-seven.test");
        var add = await owner.PostFormAsync("/company/drivers/create", new()
        {
            ["Form.FirstName"] = "Thabo",
            ["Form.Surname"] = "Nkosi",
            ["Form.Cell"] = "0825550001",
            ["Form.LicenceCode"] = "EC",
            ["Form.LicenceExpiry"] = Day(-5),
            ["Form.PrdpExpiry"] = Day(20),
        });
        Assert.Equal(HttpStatusCode.Redirect, add.StatusCode);

        var list = await (await owner.GetAsync("/company/drivers")).Content.ReadAsStringAsync();
        Assert.Contains("Expired", list);
        Assert.Contains("Due in 20 days", list);
        Assert.Contains("EC", list);
    }

    [Fact]
    public async Task Switching_off_keeps_vehicles_and_drivers_in_the_list()
    {
        var owner = await _s.CompanyWithOwnerAsync("Fleet Test Eight", "owner@fleet-eight.test");
        var vehicleId = Scenario.IdFromRedirect(await AddVehicleAsync(owner, "OFF 100"));
        await AddDriverAsync(owner, "Lerato", "Mokoena", "0825550002");
        var driverId = await InDbAsync(db => db.Drivers.IgnoreQueryFilters().Where(d => d.Surname == "Mokoena").Select(d => d.Id).SingleAsync());

        var vehicleOff = await owner.PostFormAsync($"/company/vehicles/edit/{vehicleId}", new()
        {
            ["Form.RegistrationNo"] = "OFF100",
            ["Form.IsActive"] = "false",
        });
        Assert.Equal(HttpStatusCode.Redirect, vehicleOff.StatusCode);

        var driverOff = await owner.PostFormAsync($"/company/drivers/edit/{driverId}", new()
        {
            ["Form.FirstName"] = "Lerato",
            ["Form.Surname"] = "Mokoena",
            ["Form.Cell"] = "0825550002",
            ["Form.IsActive"] = "false",
        });
        Assert.Equal(HttpStatusCode.Redirect, driverOff.StatusCode);

        var vehicles = await (await owner.GetAsync("/company/vehicles")).Content.ReadAsStringAsync();
        Assert.Contains("OFF100", vehicles);
        Assert.Contains("Off", vehicles);
        var drivers = await (await owner.GetAsync("/company/drivers")).Content.ReadAsStringAsync();
        Assert.Contains("Lerato Mokoena", drivers);
        Assert.Contains("Off", drivers);
    }
}
