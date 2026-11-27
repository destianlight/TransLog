using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TransLog.Web.Data;
using TransLog.Web.Fleet;

namespace TransLog.Tests;

/// <summary>Part 3d: make, model and variant as free text with suggestions, known spellings and "Did you mean".</summary>
public class Part3dTests(TestApp app) : IClassFixture<TestApp>
{
    private readonly Scenario _s = new(app);

    private async Task<Vehicle> VehicleAsync(string plate)
    {
        using var scope = app.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Vehicles.IgnoreQueryFilters()
            .SingleAsync(v => v.RegistrationNo == plate);
    }

    private static Task<HttpResponseMessage> AddAsync(Browser owner, string plate, string? make, string? model, string? variant = null,
        string? button = null)
    {
        var fields = new Dictionary<string, string> { ["Form.RegistrationNo"] = plate };
        if (make is not null) fields["Form.Make"] = make;
        if (model is not null) fields["Form.Model"] = model;
        if (variant is not null) fields["Form.Variant"] = variant;
        if (button is not null) fields[button] = "true";
        return owner.PostFormAsync("/company/vehicles/create", fields);
    }

    [Fact]
    public async Task Typed_names_take_the_known_spelling()
    {
        var owner = await _s.CompanyWithOwnerAsync("Names Test One", "owner@names-one.test");

        var r = await AddAsync(owner, "NAM 1 GP", "mercedes benz", "ACTROS", "2645ls");
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        var v = await VehicleAsync("NAM1GP");
        Assert.Equal("Mercedes-Benz", v.Make);
        Assert.Equal("Actros", v.Model);
        Assert.Equal("2645ls", v.Variant); // a new variant is kept as typed

        // The next vehicle with "2645 LS" gets the spelling already stored.
        Assert.Equal(HttpStatusCode.Redirect, (await AddAsync(owner, "NAM 2 GP", "Mercedes-Benz", "Actros", "2645 LS")).StatusCode);
        Assert.Equal("2645ls", (await VehicleAsync("NAM2GP")).Variant);

        Assert.Contains("Mercedes-Benz Actros 2645ls", await (await owner.GetAsync("/company/vehicles")).Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_near_miss_asks_did_you_mean_and_both_answers_work()
    {
        var owner = await _s.CompanyWithOwnerAsync("Names Test Two", "owner@names-two.test");

        // Nothing is saved until the question is answered.
        var ask = await AddAsync(owner, "NAM 3 GP", "Mercedez-Benz", "Actros");
        Assert.Equal(HttpStatusCode.OK, ask.StatusCode);
        var html = await ask.Content.ReadAsStringAsync();
        Assert.Contains("did you mean <strong>Mercedes-Benz</strong>", html);
        Assert.Equal(0, await CountAsync("NAM3GP"));

        // "Use that".
        Assert.Equal(HttpStatusCode.Redirect, (await AddAsync(owner, "NAM 3 GP", "Mercedez-Benz", "Actros", button: "Form.AcceptSuggestions")).StatusCode);
        Assert.Equal("Mercedes-Benz", (await VehicleAsync("NAM3GP")).Make);

        // "Keep what I typed".
        Assert.Equal(HttpStatusCode.Redirect, (await AddAsync(owner, "NAM 4 GP", "Scanai", "R-series", button: "Form.KeepTyped")).StatusCode);
        Assert.Equal("Scanai", (await VehicleAsync("NAM4GP")).Make);

        // A brand-new make that is close to nothing is saved straight away.
        Assert.Equal(HttpStatusCode.Redirect, (await AddAsync(owner, "NAM 5 GP", "Zebrawagon", "Z1")).StatusCode);
    }

    [Fact]
    public async Task Names_typed_by_one_company_are_suggested_to_others()
    {
        var ownerA = await _s.CompanyWithOwnerAsync("Names Test Three A", "owner@names-three-a.test");
        var ownerB = await _s.CompanyWithOwnerAsync("Names Test Three B", "owner@names-three-b.test");
        Assert.Equal(HttpStatusCode.Redirect, (await AddAsync(ownerA, "NAM 6 GP", "Kalahari Bodies", "Tipper 30")).StatusCode);

        var form = await (await ownerB.GetAsync("/company/vehicles/create")).Content.ReadAsStringAsync();
        Assert.Contains("Kalahari Bodies", form);
        Assert.Contains("Tipper 30", form);
        Assert.DoesNotContain("NAM6GP", form); // names only, never the other company's vehicles
    }

    private async Task<int> CountAsync(string plate)
    {
        using var scope = app.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Vehicles.IgnoreQueryFilters()
            .CountAsync(v => v.RegistrationNo == plate);
    }
}

/// <summary>The matching rules on their own.</summary>
public class VehicleNameRuleTests
{
    private static readonly string[] Makes = ["Mercedes-Benz", "MAN", "Scania", "Volvo", "Isuzu", "Iveco"];

    [Theory]
    [InlineData("mercedes benz", "Mercedes-Benz")]
    [InlineData("MERCEDES-BENZ", "Mercedes-Benz")]
    [InlineData("man", "MAN")]
    public void Same_letters_are_the_same_name(string typed, string expected) =>
        Assert.Equal(expected, VehicleNames.Find(typed, Makes, nearMatch: true).Exact);

    [Theory]
    [InlineData("Mercedez-Benz", "Mercedes-Benz")]
    [InlineData("Merc", "Mercedes-Benz")]
    [InlineData("Scnaia", "Scania")]
    public void Near_misses_get_a_suggestion(string typed, string expected) =>
        Assert.Equal(expected, VehicleNames.Find(typed, Makes, nearMatch: true).Suggestion);

    [Theory]
    [InlineData("MAM")]        // too short to guess
    [InlineData("Shacman")]    // a real make that is close to nothing known
    [InlineData("Isuvo")]      // two letters off a short name: no guess
    public void No_guess_when_unsure(string typed)
    {
        var (exact, suggestion) = VehicleNames.Find(typed, Makes, nearMatch: true);
        Assert.Null(exact);
        Assert.Null(suggestion);
    }
}
