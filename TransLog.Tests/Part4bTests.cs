using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TransLog.Web.Captures;
using TransLog.Web.Data;

namespace TransLog.Tests;

/// <summary>Part 4b: office capture, the company Captures list, the progress bar and the My slips filters.</summary>
public class Part4bTests(TestApp app) : IClassFixture<TestApp>
{
    private readonly Scenario _s = new(app);
    private static readonly Regex Token = new("name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");

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

    /// <summary>The Owner adds a driver and gives them a login (password "driver1"); returns the driver id.</summary>
    private async Task<int> AddDriverWithLoginAsync(Browser owner, string first, string surname, string cell)
    {
        var fields = new Dictionary<string, string> { ["Form.FirstName"] = first, ["Form.Surname"] = surname, ["Form.Cell"] = cell };
        Assert.Equal(HttpStatusCode.Redirect, (await owner.PostFormAsync("/company/drivers/create", fields)).StatusCode);
        var id = await InDbAsync(db => db.Drivers.IgnoreQueryFilters().Where(d => d.Surname == surname).Select(d => d.Id).SingleAsync());
        var give = await Scenario.PostToAsync(owner, $"/company/drivers/edit/{id}", $"/company/drivers/edit/{id}?handler=GiveLogin",
            new() { ["password"] = "driver1" });
        Assert.Equal(HttpStatusCode.Redirect, give.StatusCode);
        return id;
    }

    /// <summary>Posts one photo the way the browser scripts do: multipart form, token in the RequestVerificationToken header.</summary>
    private static async Task<HttpResponseMessage> UploadAsync(Browser b, string pageUrl, Guid key, Dictionary<string, string>? fields = null)
    {
        var page = await b.GetAsync(pageUrl);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var token = Token.Match(await page.Content.ReadAsStringAsync());
        Assert.True(token.Success, $"No anti-forgery token on {pageUrl}");

        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(key.ToString()), "clientKey");
        var p = new ByteArrayContent(TestJpeg.Make(1200, 1600));
        p.Headers.ContentType = new("image/jpeg");
        form.Add(p, "photo", "photo.jpg");
        var t = new ByteArrayContent(TestJpeg.Make(240, 320));
        t.Headers.ContentType = new("image/jpeg");
        form.Add(t, "thumb", "thumb.jpg");
        foreach (var (name, value) in fields ?? new Dictionary<string, string>())
            form.Add(new StringContent(value), name);

        var request = new HttpRequestMessage(HttpMethod.Post, pageUrl + "?handler=Upload") { Content = form };
        request.Headers.Add("RequestVerificationToken", token.Groups[1].Value);
        return await b.Client.SendAsync(request);
    }

    private static Task<HttpResponseMessage> OfficeUploadAsync(Browser b, Guid key, Dictionary<string, string>? fields = null) =>
        UploadAsync(b, "/company/capture", key, fields);

    private static async Task<string> HtmlAsync(Browser b, string url)
    {
        var r = await b.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return await r.Content.ReadAsStringAsync();
    }

    [Fact]
    public async Task A_capturer_adds_several_photos_for_a_driver_and_vehicle()
    {
        var owner = await _s.CompanyWithOwnerAsync("Office Capture One", "owner@office-one.test");
        var vehicle = await AddVehicleAsync(owner, "OC 100 GP");
        var driver = await AddDriverWithLoginAsync(owner, "Thabo", "Officeone", "0826660001");
        var capturer = await _s.AddUserAndLoginAsync(owner, "Cara Capturer", "capturer@office-one.test", "Capturer");

        var old = DateTimeOffset.UtcNow.AddDays(-30).ToUnixTimeMilliseconds().ToString();
        var keys = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        foreach (var key in keys)
        {
            // The form tries to say camera, an old date and a location: office captures ignore all three.
            var r = await OfficeUploadAsync(capturer, key, new()
            {
                ["driverId"] = driver.ToString(),
                ["vehicleId"] = vehicle.ToString(),
                ["source"] = "camera",
                ["capturedAt"] = old,
                ["rotation"] = "270",
                ["lat"] = "-26.1",
                ["lng"] = "28.1",
            });
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        }

        var captures = await InDbAsync(db => db.Captures.IgnoreQueryFilters().Include(c => c.Images)
            .Where(c => keys.Contains(c.ClientKey)).ToListAsync());
        Assert.Equal(3, captures.Count);
        var capturerId = await InDbAsync(db => db.Users.Where(u => u.Email == "capturer@office-one.test").Select(u => u.Id).SingleAsync());
        foreach (var c in captures)
        {
            Assert.Equal(driver, c.DriverId);
            Assert.Equal(vehicle, c.VehicleId);
            Assert.Equal(capturerId, c.CapturedByUserId);
            Assert.True(c.CapturedAt > DateTime.UtcNow.AddMinutes(-5), "Office photos take the moment they arrive");
            var image = Assert.Single(c.Images);
            Assert.Equal(ImageSource.Office, image.Source);
            Assert.Null(image.Latitude);
            Assert.Equal(270, image.Rotation);
            Assert.Equal(RotatedBy.Office, image.RotatedBy);
            Assert.True(File.Exists(Path.Combine(app.FilesRoot, image.FilePath)));
        }

        // No driver and no vehicle is allowed too.
        var loose = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.OK, (await OfficeUploadAsync(capturer, loose)).StatusCode);
        var c2 = await InDbAsync(db => db.Captures.IgnoreQueryFilters().SingleAsync(c => c.ClientKey == loose));
        Assert.Null(c2.DriverId);
        Assert.Null(c2.VehicleId);
    }

    [Fact]
    public async Task The_capture_page_lists_drivers_and_vehicles_with_the_drivers_link()
    {
        var owner = await _s.CompanyWithOwnerAsync("Office Capture Two", "owner@office-two.test");
        var vehicle = await AddVehicleAsync(owner, "OC 200 GP");
        var driver = await AddDriverWithLoginAsync(owner, "Naledi", "Officetwo", "0826660002");
        var link = await Scenario.PostToAsync(owner, $"/company/drivers/edit/{driver}", $"/company/drivers/edit/{driver}?handler=Link",
            new() { ["vehicleId"] = vehicle.ToString() });
        Assert.Equal(HttpStatusCode.Redirect, link.StatusCode);

        var html = await HtmlAsync(owner, "/company/capture");
        Assert.Contains("Naledi Officetwo", html);
        Assert.Contains("OC200GP", html);
        Assert.Contains($"\"{driver}\":{vehicle}", html);
        // The script URL may be fingerprinted by MapStaticAssets (page-office-capture.<hash>.js).
        Assert.Contains("/resources/page-office-capture", html);
    }

    [Fact]
    public async Task Only_owners_admins_and_capturers_open_capture_and_every_office_role_sees_the_list()
    {
        var owner = await _s.CompanyWithOwnerAsync("Office Capture Three", "owner@office-three.test");
        var admin = await _s.AddUserAndLoginAsync(owner, "Ada Admin", "admin@office-three.test", "Admin");
        var viewer = await _s.AddUserAndLoginAsync(owner, "Vic Viewer", "viewer@office-three.test", "Viewer");
        await AddDriverWithLoginAsync(owner, "Pule", "Officethree", "0826660003");
        var phone = await _s.LoginAsync("0826660003", "driver1");

        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync("/company/capture")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/company/capture")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await viewer.GetAsync("/company/capture")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await phone.GetAsync("/company/capture")).StatusCode);

        var list = await HtmlAsync(viewer, "/company/captures");
        Assert.DoesNotContain("href=\"/company/capture\"", list); // no Capture button for a Viewer
        Assert.Contains("href=\"/company/capture\"", await HtmlAsync(owner, "/company/captures"));
        Assert.Equal(HttpStatusCode.NotFound, (await phone.GetAsync("/company/captures")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await (await _s.AdminAsync()).GetAsync("/company/captures")).StatusCode);
    }

    [Fact]
    public async Task Another_companys_driver_or_vehicle_is_refused()
    {
        var ownerA = await _s.CompanyWithOwnerAsync("Office Capture Four", "owner@office-four.test");
        var ownerB = await _s.CompanyWithOwnerAsync("Office Capture Five", "owner@office-five.test");
        var vehicleB = await AddVehicleAsync(ownerB, "OC 500 GP");
        var driverB = await AddDriverWithLoginAsync(ownerB, "Lindiwe", "Officefive", "0826660005");

        var k1 = Guid.NewGuid();
        var r1 = await OfficeUploadAsync(ownerA, k1, new() { ["driverId"] = driverB.ToString() });
        Assert.Equal(HttpStatusCode.BadRequest, r1.StatusCode);
        Assert.Contains("driver", JsonDocument.Parse(await r1.Content.ReadAsStringAsync()).RootElement.GetProperty("error").GetString());

        var k2 = Guid.NewGuid();
        var r2 = await OfficeUploadAsync(ownerA, k2, new() { ["vehicleId"] = vehicleB.ToString() });
        Assert.Equal(HttpStatusCode.BadRequest, r2.StatusCode);

        Assert.False(await InDbAsync(db => db.Captures.IgnoreQueryFilters().AnyAsync(c => c.ClientKey == k1 || c.ClientKey == k2)));
    }

    [Fact]
    public async Task The_captures_list_shows_this_companys_captures_with_filters_and_progress()
    {
        var ownerA = await _s.CompanyWithOwnerAsync("Office Capture Six", "owner@office-six.test");
        var ownerB = await _s.CompanyWithOwnerAsync("Office Capture Seven", "owner@office-seven.test");
        var driver1 = await AddDriverWithLoginAsync(ownerA, "Mpho", "Officesix", "0826660006");
        var driver2 = await AddDriverWithLoginAsync(ownerA, "Karabo", "Officesixb", "0826660016");

        Guid k1 = Guid.NewGuid(), k2 = Guid.NewGuid(), kb = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.OK, (await OfficeUploadAsync(ownerA, k1, new() { ["driverId"] = driver1.ToString() })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await OfficeUploadAsync(ownerA, k2, new() { ["driverId"] = driver2.ToString() })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await OfficeUploadAsync(ownerB, kb)).StatusCode);

        Task<int> Img(Guid key) => InDbAsync(db => db.CaptureImages.IgnoreQueryFilters().Where(i => i.Capture!.ClientKey == key).Select(i => i.Id).SingleAsync());
        int i1 = await Img(k1), i2 = await Img(k2), ib = await Img(kb);

        var all = await HtmlAsync(ownerA, "/company/captures");
        Assert.Contains($"/files/capture/{i1}/thumb", all);
        Assert.Contains($"/files/capture/{i2}/thumb", all);
        Assert.DoesNotContain($"/files/capture/{ib}/thumb", all); // another company
        Assert.Contains("Waiting to be read", all);                // progress bar label
        Assert.Contains("tl-progress", all);
        Assert.Contains("Office upload by Owner of Office Capture Six", all);

        var one = await HtmlAsync(ownerA, $"/company/captures?driverId={driver1}");
        Assert.Contains($"/files/capture/{i1}/thumb", one);
        Assert.DoesNotContain($"/files/capture/{i2}/thumb", one);

        Assert.Contains($"/files/capture/{i1}/thumb", await HtmlAsync(ownerA, "/company/captures?show=busy"));
        Assert.DoesNotContain($"/files/capture/{i1}/thumb", await HtmlAsync(ownerA, "/company/captures?show=done"));

        var today = TransLog.Web.AppCulture.ToSast(DateTime.UtcNow).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        Assert.Contains($"/files/capture/{i1}/thumb", await HtmlAsync(ownerA, $"/company/captures?from={today}&to={today}"));
        Assert.DoesNotContain($"/files/capture/{i1}/thumb", await HtmlAsync(ownerA, "/company/captures?to=2020-01-01"));
    }

    [Fact]
    public async Task The_menu_groups_fleet_and_company_pages_by_role_and_marks_the_current_page()
    {
        var owner = await _s.CompanyWithOwnerAsync("Office Capture Nine", "owner@office-nine.test");
        var viewer = await _s.AddUserAndLoginAsync(owner, "Val Viewer", "viewer@office-nine.test", "Viewer");

        var home = await HtmlAsync(owner, "/company");
        Assert.Contains("navbar-expand-lg", home);
        Assert.Contains("href=\"/company/drivers\"", home);
        Assert.Contains("href=\"/company/users\"", home);
        Assert.Contains("Log out", home);
        Assert.Contains("My account", home);

        var list = await HtmlAsync(viewer, "/company/captures");
        Assert.DoesNotContain("href=\"/company/drivers\"", list); // no Fleet menu for a Viewer
        Assert.DoesNotContain("href=\"/company/users\"", list);   // no Company menu for a Viewer
        Assert.Matches("class=\"nav-link tl-navlink active\"[^>]*href=\"/company/captures\"", list);
    }

    [Fact]
    public async Task My_slips_shows_15_at_a_time_and_filters_busy_done_and_day()
    {
        var owner = await _s.CompanyWithOwnerAsync("Office Capture Eight", "owner@office-eight.test");
        await AddDriverWithLoginAsync(owner, "Sizwe", "Officeeight", "0826660008");
        var phone = await _s.LoginAsync("0826660008", "driver1");

        for (var i = 0; i < 17; i++)
            Assert.Equal(HttpStatusCode.OK, (await UploadAsync(phone, "/driver/capture", Guid.NewGuid())).StatusCode);

        var first = await HtmlAsync(phone, "/driver/slips");
        Assert.Equal(15, Regex.Matches(first, "/thumb\"").Count);
        Assert.Contains("Show 15 more", first);
        Assert.Contains("take=30", first);

        var more = await HtmlAsync(phone, "/driver/slips?take=30");
        Assert.Equal(17, Regex.Matches(more, "/thumb\"").Count);
        Assert.DoesNotContain("Show 15 more", more);

        Assert.Equal(15, Regex.Matches(await HtmlAsync(phone, "/driver/slips?show=busy"), "/thumb\"").Count);
        var done = await HtmlAsync(phone, "/driver/slips?show=done");
        Assert.Empty(Regex.Matches(done, "/thumb\""));
        Assert.Contains("No slips for this choice.", done);
        Assert.Empty(Regex.Matches(await HtmlAsync(phone, "/driver/slips?day=2020-01-01"), "/thumb\""));
    }
}

/// <summary>The progress bar steps for each status (Part 4b).</summary>
public class CaptureProgressTests
{
    [Theory]
    [InlineData(CaptureStatus.Processing, 1, "", "Waiting to be read")]
    [InlineData(CaptureStatus.NeedsReview, 2, "", "Being checked")]
    [InlineData(CaptureStatus.Approved, 4, "ok", "Approved")]
    [InlineData(CaptureStatus.Rejected, 4, "bad", "Rejected")]
    [InlineData(CaptureStatus.Failed, 4, "bad", "Could not be read")]
    public void Steps_and_labels(CaptureStatus status, int finished, string tone, string label)
    {
        var p = new CaptureProgress(1, status);
        Assert.Equal(finished, p.Finished);
        Assert.Equal(tone, p.Tone);
        Assert.Equal(label, p.Label);
    }

    [Fact]
    public void Busy_and_done_cover_every_status_once()
    {
        var all = Enum.GetValues<CaptureStatus>();
        Assert.Equal(all.Length, CaptureProgress.Busy.Length + CaptureProgress.Done.Length);
        Assert.Empty(CaptureProgress.Busy.Intersect(CaptureProgress.Done));
        Assert.Equal(CaptureShow.Busy, CaptureProgress.ParseShow("BUSY"));
        Assert.Equal(CaptureShow.All, CaptureProgress.ParseShow("nonsense"));
    }
}
