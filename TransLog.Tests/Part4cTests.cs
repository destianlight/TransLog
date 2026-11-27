using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TransLog.Web.Data;
using TransLog.Web.Fleet;

namespace TransLog.Tests;

/// <summary>Part 4c: driver profile photos from the phone camera or gallery; the office adds, replaces or removes them.</summary>
public class Part4cTests(TestApp app) : IClassFixture<TestApp>
{
    private readonly Scenario _s = new(app);
    private static readonly Regex Token = new("name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");

    private async Task<T> InDbAsync<T>(Func<AppDbContext, Task<T>> query)
    {
        using var scope = app.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
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

    /// <summary>Posts a photo the way page-driver-photo.js does: multipart, token in the RequestVerificationToken header.</summary>
    private static async Task<HttpResponseMessage> UploadAsync(Browser b, string pageUrl, string postUrl, byte[] photo)
    {
        var page = await b.GetAsync(pageUrl);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var token = Token.Match(await page.Content.ReadAsStringAsync());
        Assert.True(token.Success, $"No anti-forgery token on {pageUrl}");

        using var form = new MultipartFormDataContent();
        var p = new ByteArrayContent(photo);
        p.Headers.ContentType = new("image/jpeg");
        form.Add(p, "photo", "photo.jpg");
        var t = new ByteArrayContent(TestJpeg.Make(160, 160));
        t.Headers.ContentType = new("image/jpeg");
        form.Add(t, "thumb", "thumb.jpg");

        var request = new HttpRequestMessage(HttpMethod.Post, postUrl) { Content = form };
        request.Headers.Add("RequestVerificationToken", token.Groups[1].Value);
        return await b.Client.SendAsync(request);
    }

    private Task<string?> PathAsync(int driverId) =>
        InDbAsync(db => db.Drivers.IgnoreQueryFilters().Where(d => d.Id == driverId).Select(d => d.PhotoPath).SingleAsync());

    private bool OnDisk(string path) => File.Exists(Path.Combine(app.FilesRoot, path));

    [Fact]
    public async Task A_driver_adds_his_own_photo_and_only_the_right_people_see_it()
    {
        var ownerA = await _s.CompanyWithOwnerAsync("Photo Test One", "owner@photo-one.test");
        var ownerB = await _s.CompanyWithOwnerAsync("Photo Test Two", "owner@photo-two.test");
        var viewer = await _s.AddUserAndLoginAsync(ownerA, "Vusi Viewer", "viewer@photo-one.test", "Viewer");
        var driver = await AddDriverWithLoginAsync(ownerA, "Andile", "Photoone", "0827770001");
        await AddDriverWithLoginAsync(ownerA, "Buhle", "Phototwo", "0827770002");
        var phone = await _s.LoginAsync("0827770001", "driver1");
        var other = await _s.LoginAsync("0827770002", "driver1");

        // Before: initials and "Add your photo" on the Driver home.
        var before = await (await phone.GetAsync("/driver")).Content.ReadAsStringAsync();
        Assert.Contains("Add your photo", before);
        Assert.Contains("href=\"/driver/photo\"", before);

        var r = await UploadAsync(phone, "/driver/photo", "/driver/photo?handler=Upload", TestJpeg.Make(600, 600));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);

        var path = await PathAsync(driver);
        Assert.NotNull(path);
        Assert.StartsWith("drivers/", path);
        Assert.Contains($"/{driver}/", path);
        Assert.True(OnDisk(path!));
        Assert.True(OnDisk(DriverPhotos.ThumbOf(path!)));

        var home = await (await phone.GetAsync("/driver")).Content.ReadAsStringAsync();
        Assert.Contains($"/files/driver/{driver}/thumb?v=", home);
        Assert.DoesNotContain("Add your photo", home);

        var url = $"/files/driver/{driver}";
        var own = await phone.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
        Assert.Equal("image/jpeg", own.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.OK, (await phone.GetAsync(url + "/thumb")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ownerA.GetAsync(url)).StatusCode);    // the company
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync(url)).StatusCode);    // every office role
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync(url)).StatusCode);  // another driver
        Assert.Equal(HttpStatusCode.NotFound, (await ownerB.GetAsync(url)).StatusCode); // another company
        Assert.Equal(HttpStatusCode.NotFound, (await (await _s.AdminAsync()).GetAsync(url)).StatusCode); // TransLog staff
        var anonymous = await app.NewBrowser().GetAsync(url);
        Assert.Equal(HttpStatusCode.Redirect, anonymous.StatusCode);
        Assert.Contains("/login", anonymous.Headers.Location?.OriginalString);

        // The driver list shows it to the Owner.
        Assert.Contains($"/files/driver/{driver}/thumb?v=", await (await ownerA.GetAsync("/company/drivers")).Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task The_owner_replaces_and_removes_a_photo_and_the_old_files_go()
    {
        var owner = await _s.CompanyWithOwnerAsync("Photo Test Three", "owner@photo-three.test");
        var driver = await AddDriverWithLoginAsync(owner, "Chris", "Photothree", "0827770003");
        var page = $"/company/drivers/edit/{driver}";

        Assert.Equal(HttpStatusCode.OK, (await UploadAsync(owner, page, page + "?handler=Photo", TestJpeg.Make(600, 600))).StatusCode);
        var first = await PathAsync(driver);
        Assert.NotNull(first);

        Assert.Equal(HttpStatusCode.OK, (await UploadAsync(owner, page, page + "?handler=Photo", TestJpeg.Make(600, 600))).StatusCode);
        var second = await PathAsync(driver);
        Assert.NotNull(second);
        Assert.NotEqual(first, second);
        Assert.False(OnDisk(first!));
        Assert.False(OnDisk(DriverPhotos.ThumbOf(first!)));
        Assert.True(OnDisk(second!));

        var html = await (await owner.GetAsync(page)).Content.ReadAsStringAsync();
        Assert.Contains("Remove photo", html);
        Assert.Contains($"/files/driver/{driver}?v=", html);

        var remove = await Scenario.PostToAsync(owner, page, page + "?handler=RemovePhoto", new());
        Assert.Equal(HttpStatusCode.Redirect, remove.StatusCode);
        Assert.Null(await PathAsync(driver));
        Assert.False(OnDisk(second!));
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"/files/driver/{driver}")).StatusCode);

        var actions = await InDbAsync(db => db.AuditLogs.IgnoreQueryFilters().Where(a => a.Details != null && a.Details.Contains("Chris Photothree"))
            .Select(a => a.Action).ToListAsync());
        Assert.Contains("DriverPhotoAdded", actions);
        Assert.Contains("DriverPhotoChanged", actions);
        Assert.Contains("DriverPhotoRemoved", actions);
    }

    [Fact]
    public async Task A_file_that_is_not_a_JPEG_is_refused_and_a_capturer_cannot_change_photos()
    {
        var owner = await _s.CompanyWithOwnerAsync("Photo Test Four", "owner@photo-four.test");
        var driver = await AddDriverWithLoginAsync(owner, "Dumi", "Photofour", "0827770004");
        var phone = await _s.LoginAsync("0827770004", "driver1");

        var bad = await UploadAsync(phone, "/driver/photo", "/driver/photo?handler=Upload", "not a photo"u8.ToArray());
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Contains("JPEG", await bad.Content.ReadAsStringAsync());
        var tooBig = await UploadAsync(phone, "/driver/photo", "/driver/photo?handler=Upload", TestJpeg.Make(3000, 3000));
        Assert.Equal(HttpStatusCode.BadRequest, tooBig.StatusCode);
        Assert.Null(await PathAsync(driver));

        // Driver pages belong to the Owner and Admins.
        var capturer = await _s.AddUserAndLoginAsync(owner, "Cleo Capturer", "capturer@photo-four.test", "Capturer");
        Assert.Equal(HttpStatusCode.NotFound, (await capturer.GetAsync($"/company/drivers/edit/{driver}")).StatusCode);
    }
}

/// <summary>Initials shown when a driver has no photo (Part 4c).</summary>
public class DriverPhotoRuleTests
{
    [Theory]
    [InlineData("Sipho van Wyk", "SW")]
    [InlineData("Sipho", "S")]
    [InlineData("  lerato   mokoena ", "LM")]
    [InlineData("", "?")]
    public void Initials(string name, string expected) => Assert.Equal(expected, DriverPhotos.Initials(name));

    [Fact]
    public void Thumb_and_address()
    {
        Assert.Equal("drivers/1/2/abc-t.jpg", DriverPhotos.ThumbOf("drivers/1/2/abc.jpg"));
        Assert.Equal("/files/driver/2/thumb?v=abc", DriverPhotos.Url(2, "drivers/1/2/abc.jpg"));
        Assert.Equal("/files/driver/2?v=abc", DriverPhotos.Url(2, "drivers/1/2/abc.jpg", thumb: false));
    }
}
