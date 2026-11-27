using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TransLog.Web.Data;
using TransLog.Web.Storage;

namespace TransLog.Tests;

/// <summary>Part 4a: drivers send photos from the camera page; files on disk; photos only for the right people; My slips.</summary>
public class Part4aTests(TestApp app) : IClassFixture<TestApp>
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

    private static async Task LinkAsync(Browser owner, int driverId, int vehicleId)
    {
        var r = await Scenario.PostToAsync(owner, $"/company/drivers/edit/{driverId}", $"/company/drivers/edit/{driverId}?handler=Link",
            new() { ["vehicleId"] = vehicleId.ToString() });
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
    }

    /// <summary>Posts a photo the way page-capture-queue.js does: multipart form, token in the RequestVerificationToken header.</summary>
    private static async Task<HttpResponseMessage> UploadAsync(Browser phone, Guid key, byte[] photo, Dictionary<string, string>? fields = null, bool thumb = true)
    {
        var page = await phone.GetAsync("/driver/capture");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var token = Token.Match(await page.Content.ReadAsStringAsync());
        Assert.True(token.Success, "No anti-forgery token on the camera page");

        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(key.ToString()), "clientKey");
        var p = new ByteArrayContent(photo);
        p.Headers.ContentType = new("image/jpeg");
        form.Add(p, "photo", "photo.jpg");
        if (thumb)
        {
            var t = new ByteArrayContent(TestJpeg.Make(240, 320));
            t.Headers.ContentType = new("image/jpeg");
            form.Add(t, "thumb", "thumb.jpg");
        }
        foreach (var (name, value) in fields ?? new Dictionary<string, string>())
            form.Add(new StringContent(value), name);

        var request = new HttpRequestMessage(HttpMethod.Post, "/driver/capture?handler=Upload") { Content = form };
        request.Headers.Add("RequestVerificationToken", token.Groups[1].Value);
        return await phone.Client.SendAsync(request);
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage r) =>
        JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement;

    [Fact]
    public async Task A_driver_sends_a_photo_and_it_is_stored_with_vehicle_location_and_files()
    {
        var owner = await _s.CompanyWithOwnerAsync("Capture Test One", "owner@capture-one.test");
        var vehicle = await AddVehicleAsync(owner, "CP 100 GP");
        var driver = await AddDriverWithLoginAsync(owner, "Sipho", "Captureone", "0825550001");
        await LinkAsync(owner, driver, vehicle);
        var phone = await _s.LoginAsync("0825550001", "driver1");

        var taken = DateTimeOffset.UtcNow.AddMinutes(-5);
        var key = Guid.NewGuid();
        var r = await UploadAsync(phone, key, TestJpeg.Make(1200, 1600), new()
        {
            ["capturedAt"] = taken.ToUnixTimeMilliseconds().ToString(),
            ["source"] = "camera",
            ["rotation"] = "90",
            ["lat"] = "-26.2041",
            ["lng"] = "28.0473",
            ["acc"] = "12",
            ["locAt"] = taken.ToUnixTimeMilliseconds().ToString(),
        });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var json = await JsonAsync(r);
        Assert.True(json.GetProperty("ok").GetBoolean());
        Assert.False(json.GetProperty("duplicate").GetBoolean());

        var capture = await InDbAsync(db => db.Captures.IgnoreQueryFilters().Include(c => c.Images).SingleAsync(c => c.ClientKey == key));
        Assert.Equal(vehicle, capture.VehicleId);
        Assert.Equal(driver, capture.DriverId);
        Assert.Equal(CaptureStatus.Processing, capture.Status);
        Assert.InRange(capture.CapturedAt, taken.UtcDateTime.AddSeconds(-1), taken.UtcDateTime.AddSeconds(1));

        var image = Assert.Single(capture.Images);
        Assert.Equal(capture.CompanyId, image.CompanyId);
        Assert.Equal((1200, 1600), (image.Width, image.Height));
        Assert.Equal(ImageSource.Camera, image.Source);
        Assert.Equal(90, image.Rotation);
        Assert.Equal(RotatedBy.Driver, image.RotatedBy);
        Assert.Equal(-26.2041m, image.Latitude);
        Assert.Equal(28.0473m, image.Longitude);
        Assert.Equal(12, image.Accuracy);
        Assert.Equal(64, image.Sha256.Length);
        Assert.StartsWith($"captures/{capture.CompanyId}/", image.FilePath);
        Assert.True(File.Exists(Path.Combine(app.FilesRoot, image.FilePath)));
        Assert.True(File.Exists(Path.Combine(app.FilesRoot, image.ThumbPath!)));
    }

    [Fact]
    public async Task The_same_photo_sent_twice_is_stored_once()
    {
        var owner = await _s.CompanyWithOwnerAsync("Capture Test Two", "owner@capture-two.test");
        await AddDriverWithLoginAsync(owner, "Lerato", "Capturetwo", "0825550002");
        var phone = await _s.LoginAsync("0825550002", "driver1");

        var key = Guid.NewGuid();
        var first = await JsonAsync(await UploadAsync(phone, key, TestJpeg.Make(800, 600)));
        var again = await UploadAsync(phone, key, TestJpeg.Make(800, 600));
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        var second = await JsonAsync(again);

        Assert.True(second.GetProperty("duplicate").GetBoolean());
        Assert.Equal(first.GetProperty("id").GetInt32(), second.GetProperty("id").GetInt32());
        Assert.Equal(1, await InDbAsync(db => db.Captures.IgnoreQueryFilters().CountAsync(c => c.ClientKey == key)));
    }

    [Fact]
    public async Task A_file_that_is_not_a_JPEG_is_refused_and_a_driver_without_a_vehicle_can_still_send()
    {
        var owner = await _s.CompanyWithOwnerAsync("Capture Test Three", "owner@capture-three.test");
        await AddDriverWithLoginAsync(owner, "Ayanda", "Capturethree", "0825550003");
        var phone = await _s.LoginAsync("0825550003", "driver1");

        var badKey = Guid.NewGuid();
        var bad = await UploadAsync(phone, badKey, "not a photo"u8.ToArray());
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Contains("JPEG", (await JsonAsync(bad)).GetProperty("error").GetString());
        Assert.False(await InDbAsync(db => db.Captures.IgnoreQueryFilters().AnyAsync(c => c.ClientKey == badKey)));

        // No vehicle linked: the photo is still taken, without a vehicle (the reviewer sets it later).
        var key = Guid.NewGuid();
        var ok = await UploadAsync(phone, key, TestJpeg.Make(1600, 1200), new() { ["source"] = "gallery" }, thumb: false);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var capture = await InDbAsync(db => db.Captures.IgnoreQueryFilters().Include(c => c.Images).SingleAsync(c => c.ClientKey == key));
        Assert.Null(capture.VehicleId);
        var image = Assert.Single(capture.Images);
        Assert.Equal(ImageSource.Gallery, image.Source);
        Assert.Null(image.ThumbPath);
        Assert.Null(image.Latitude);
    }

    [Fact]
    public async Task Photos_are_only_shown_to_the_right_people()
    {
        var ownerA = await _s.CompanyWithOwnerAsync("Capture Test Four", "owner@capture-four.test");
        var ownerB = await _s.CompanyWithOwnerAsync("Capture Test Five", "owner@capture-five.test");
        await AddDriverWithLoginAsync(ownerA, "Bongani", "Capturefour", "0825550004");
        await AddDriverWithLoginAsync(ownerA, "Zanele", "Capturefive", "0825550005");
        var viewer = await _s.AddUserAndLoginAsync(ownerA, "Vera Viewer", "viewer@capture-four.test", "Viewer");
        var phone1 = await _s.LoginAsync("0825550004", "driver1");
        var phone2 = await _s.LoginAsync("0825550005", "driver1");

        var key = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.OK, (await UploadAsync(phone1, key, TestJpeg.Make(1200, 900))).StatusCode);
        var imageId = await InDbAsync(db => db.CaptureImages.IgnoreQueryFilters().Where(i => i.Capture!.ClientKey == key).Select(i => i.Id).SingleAsync());
        var url = $"/files/capture/{imageId}";

        var own = await phone1.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
        Assert.Equal("image/jpeg", own.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.OK, (await phone1.GetAsync(url + "/thumb")).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await phone2.GetAsync(url)).StatusCode);   // another driver
        Assert.Equal(HttpStatusCode.OK, (await ownerA.GetAsync(url)).StatusCode);         // the company
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync(url)).StatusCode);         // any office user of the company
        Assert.Equal(HttpStatusCode.NotFound, (await ownerB.GetAsync(url)).StatusCode);   // another company
        Assert.Equal(HttpStatusCode.NotFound, (await (await _s.AdminAsync()).GetAsync(url)).StatusCode); // TransLog staff: no company

        var anonymous = await app.NewBrowser().GetAsync(url);
        Assert.Equal(HttpStatusCode.Redirect, anonymous.StatusCode);
        Assert.Contains("/login", anonymous.Headers.Location?.OriginalString);

        // My slips: the driver sees their own photo; the other driver does not.
        Assert.Contains(url + "/thumb", await (await phone1.GetAsync("/driver/slips")).Content.ReadAsStringAsync());
        Assert.DoesNotContain(url + "/thumb", await (await phone2.GetAsync("/driver/slips")).Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Office_users_cannot_open_the_driver_camera()
    {
        var owner = await _s.CompanyWithOwnerAsync("Capture Test Six", "owner@capture-six.test");
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync("/driver/capture")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync("/driver/slips")).StatusCode);
    }

    [Fact]
    public async Task The_driver_home_links_to_the_camera_and_my_slips()
    {
        var owner = await _s.CompanyWithOwnerAsync("Capture Test Seven", "owner@capture-seven.test");
        await AddDriverWithLoginAsync(owner, "Kagiso", "Captureseven", "0825550007");
        var phone = await _s.LoginAsync("0825550007", "driver1");
        var home = await (await phone.GetAsync("/driver")).Content.ReadAsStringAsync();
        Assert.Contains("href=\"/driver/capture\"", home);
        Assert.Contains("href=\"/driver/slips\"", home);
    }
}

/// <summary>Reading the size of a JPEG without an image library (Part 4a).</summary>
public class JpegInfoTests
{
    [Fact]
    public void Reads_width_and_height()
    {
        Assert.Equal<(int, int)?>((1600, 1200), JpegInfo.Read(TestJpeg.Make(1600, 1200)));
        Assert.Equal<(int, int)?>((900, 1600), JpegInfo.Read(TestJpeg.Make(900, 1600)));
    }

    [Fact]
    public void Refuses_what_is_not_a_JPEG()
    {
        Assert.Null(JpegInfo.Read("hello"u8));
        Assert.Null(JpegInfo.Read([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A])); // PNG
        Assert.Null(JpegInfo.Read([0xFF, 0xD8, 0xFF, 0xD9]));                       // JPEG without a frame header
        Assert.Null(JpegInfo.Read([]));
    }
}

/// <summary>The smallest JPEG header the server accepts: start, a JFIF block, a frame header with the size, end.</summary>
public static class TestJpeg
{
    public static byte[] Make(int width, int height) =>
    [
        0xFF, 0xD8,
        0xFF, 0xE0, 0x00, 0x10, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0x00, 0x01, 0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00,
        0xFF, 0xC0, 0x00, 0x11, 0x08, (byte)(height >> 8), (byte)height, (byte)(width >> 8), (byte)width,
        0x03, 0x01, 0x22, 0x00, 0x02, 0x11, 0x01, 0x03, 0x11, 0x01,
        0xFF, 0xD9,
    ];
}
