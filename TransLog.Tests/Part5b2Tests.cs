using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TransLog.Web.Ai;
using TransLog.Web.Captures;
using TransLog.Web.Data;

namespace TransLog.Tests;

/// <summary>Part 5b2: the driver's slip page (correct values, remove), office remove and restore, new rows for the lists.</summary>
public class Part5b2Tests(TestApp app) : IClassFixture<TestApp>
{
    private readonly Scenario _s = new(app);

    private static async Task<string> HtmlAsync(Browser b, string url)
    {
        var r = await b.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return await r.Content.ReadAsStringAsync();
    }

    private Task<Capture> LoadAsync(int id) => AiFlow.LoadAsync(app.Services, id);

    /// <summary>Makes an uploaded slip look read by the AI as a fuel slip (as the worker stores it).</summary>
    private async Task MakeReadAsync(int id, CaptureStatus status = CaptureStatus.NeedsReview)
    {
        var fuel = await Part5bFlow.InDbAsync(app, db => db.CaptureTypes.Where(t => t.Key == "fuel" && t.Scope == PromptScope.Global).Select(t => t.Id).SingleAsync());
        int? typeId = fuel;
        DateOnly? slipDate = new DateOnly(2026, 11, 27);
        decimal? litres = 412.5m;
        DateTime? readAt = DateTime.UtcNow;
        const string data = "{\"registration\":\"LLT587NW\",\"litres\":\"412.5\",\"amount\":\"8250.75\",\"slip_date\":\"2026-11-27\",\"vendor\":\"Engen Vryburg\"}";
        await Part5bFlow.InDbAsync(app, db => db.Captures.IgnoreQueryFilters().Where(c => c.Id == id).ExecuteUpdateAsync(s => s
            .SetProperty(c => c.Status, status)
            .SetProperty(c => c.CaptureTypeId, typeId)
            .SetProperty(c => c.EventDate, slipDate)
            .SetProperty(c => c.Quantity, litres)
            .SetProperty(c => c.DataJson, data)
            .SetProperty(c => c.AiReadAt, readAt)));
    }

    [Fact]
    public async Task The_driver_corrects_values_on_his_slip_page_and_each_change_is_kept()
    {
        var owner = await _s.CompanyWithOwnerAsync("Correct Co", "owner@correct.test");
        var driverId = await Part5bFlow.AddDriverWithLoginAsync(app, owner, "Musa", "Fixone", "0829990001");
        await Part5bFlow.AddDriverWithLoginAsync(app, owner, "Lindi", "Fixtwo", "0829990002");
        var id = await Part5bFlow.OfficeUploadAsync(owner, driverId);
        await MakeReadAsync(id);

        var phone = await _s.LoginAsync("0829990001", "driver1");
        var list = await HtmlAsync(phone, "/driver/slips");
        Assert.Contains($"href=\"/driver/slips/{id}\"", list);

        var page = await HtmlAsync(phone, $"/driver/slips/{id}");
        Assert.Contains("What the AI read", page);
        Assert.Contains("name=\"v.litres\"", page);
        Assert.Contains("value=\"412.5\"", page);
        Assert.Contains("Remove this slip", page);
        Assert.Contains($"/files/capture/", page);

        // A bad number: nothing is saved.
        var bad = await Scenario.PostToAsync(phone, $"/driver/slips/{id}", $"/driver/slips/{id}?handler=Save", new() { ["v.litres"] = "abc" });
        Assert.Equal(HttpStatusCode.OK, bad.StatusCode);
        Assert.Contains("type a number", await bad.Content.ReadAsStringAsync());
        Assert.Equal(412.5m, (await LoadAsync(id)).Quantity);

        // Two real changes and one value left as it was.
        var r = await Scenario.PostToAsync(phone, $"/driver/slips/{id}", $"/driver/slips/{id}?handler=Save", new()
        {
            ["v.litres"] = "30,5",
            ["v.slip_date"] = "2026-11-26",
            ["v.vendor"] = "Engen Vryburg",
        });
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        var c = await LoadAsync(id);
        Assert.Equal(30.5m, c.Quantity);
        Assert.Equal(new DateOnly(2026, 11, 26), c.EventDate);
        var data = CaptureReader.ReadDataJson(c.DataJson);
        Assert.Equal("30.5", data["litres"]);
        Assert.Equal("2026-11-26", data["slip_date"]);
        Assert.Equal("LLT587NW", data["registration"]);

        var edits = await Part5bFlow.InDbAsync(app, db => db.CaptureEdits.IgnoreQueryFilters().Where(e => e.CaptureId == id).OrderBy(e => e.FieldKey).ToListAsync());
        Assert.Equal(2, edits.Count);
        Assert.All(edits, e => Assert.True(e.ByDriver));
        Assert.Equal(("litres", "412.5", "30.5"), (edits[0].FieldKey, edits[0].OldValue, edits[0].NewValue));
        Assert.Equal(("slip_date", "2026-11-27", "2026-11-26"), (edits[1].FieldKey, edits[1].OldValue, edits[1].NewValue));

        var after = await HtmlAsync(phone, $"/driver/slips/{id}");
        Assert.Contains("changed by you", after);
        Assert.Contains("AI read: 412.5", after);
        Assert.Contains("values saved", after);

        // The office sees the corrected value; another driver of the company cannot open the slip.
        Assert.Contains("30.5", await HtmlAsync(owner, "/company/captures"));
        var other = await _s.LoginAsync("0829990002", "driver1");
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/driver/slips/{id}")).StatusCode);
        Assert.True(await Part5bFlow.InDbAsync(app, db => db.AuditLogs.AnyAsync(a => a.Action == "CaptureEdited" && a.Details!.Contains("#" + id + ":"))));
    }

    [Fact]
    public async Task The_company_can_stop_drivers_correcting_values()
    {
        var owner = await _s.CompanyWithOwnerAsync("Locked Co", "owner@locked.test");
        var driverId = await Part5bFlow.AddDriverWithLoginAsync(app, owner, "Bheki", "Lockone", "0829990003");
        var id = await Part5bFlow.OfficeUploadAsync(owner, driverId);
        await MakeReadAsync(id);

        Assert.Contains("Drivers may correct values", await HtmlAsync(owner, "/company/settings"));
        var saved = await owner.PostFormAsync("/company/settings", new()
        {
            ["Form.DriverLinking"] = "DriversAndCompany",
            ["Form.DriversMayEditValues"] = "false",
        });
        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);

        var phone = await _s.LoginAsync("0829990003", "driver1");
        var page = await HtmlAsync(phone, $"/driver/slips/{id}");
        Assert.DoesNotContain("name=\"v.litres\"", page);
        Assert.Contains("Your company checks the values", page);
        Assert.Contains("412.5", page);

        var r = await Scenario.PostToAsync(phone, $"/driver/slips/{id}", $"/driver/slips/{id}?handler=Save", new() { ["v.litres"] = "1" });
        Assert.Contains("does not let drivers", await r.Content.ReadAsStringAsync());
        Assert.Equal(412.5m, (await LoadAsync(id)).Quantity);
    }

    [Fact]
    public async Task The_driver_removes_a_waiting_slip_and_the_office_restores_it()
    {
        var owner = await _s.CompanyWithOwnerAsync("Remove Co", "owner@remove.test");
        var driverId = await Part5bFlow.AddDriverWithLoginAsync(app, owner, "Andile", "Removeone", "0829990004");
        var id = await Part5bFlow.OfficeUploadAsync(owner, driverId);
        var phone = await _s.LoginAsync("0829990004", "driver1");

        Assert.Contains("Being read", await HtmlAsync(phone, $"/driver/slips/{id}"));
        var r = await Scenario.PostToAsync(phone, $"/driver/slips/{id}", $"/driver/slips/{id}?handler=Remove", new());
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        var c = await LoadAsync(id);
        Assert.Equal(CaptureStatus.Removed, c.Status);
        Assert.Equal(CaptureStatus.Processing, c.StatusBeforeRemove);
        Assert.NotNull(c.RemovedAt);
        Assert.NotNull(c.RemovedByUserId);

        // Gone from My slips and never read.
        Assert.DoesNotContain($"data-row=\"{id}\"", await HtmlAsync(phone, "/driver/slips"));
        Assert.Contains("This slip was removed", await HtmlAsync(phone, $"/driver/slips/{id}"));
        using (var scope = app.Services.CreateScope())
        {
            var job = scope.ServiceProvider.GetRequiredService<CaptureReader>();
            for (var i = 0; i < 20; i++)
                Assert.NotEqual(id, await job.ClaimAsync());
            Assert.Null(await job.ProcessAsync(id));
        }

        // The office: not under All, under Removed with who and Restore.
        Assert.DoesNotContain($"data-row=\"{id}\"", await HtmlAsync(owner, "/company/captures"));
        var removed = await HtmlAsync(owner, "/company/captures?show=removed");
        Assert.Contains($"data-row=\"{id}\"", removed);
        Assert.Contains("Removed by ", removed);
        Assert.Contains("Restore", removed);

        var back = await Scenario.PostToAsync(owner, "/company/captures?show=removed", $"/company/captures?handler=Restore&id={id}",
            new() { ["back"] = "?show=removed" });
        Assert.Equal(HttpStatusCode.Redirect, back.StatusCode);
        Assert.Equal("/company/captures?show=removed", back.Headers.Location?.OriginalString);
        c = await LoadAsync(id);
        Assert.Equal(CaptureStatus.Processing, c.Status);
        Assert.Null(c.RemovedAt);
        Assert.Null(c.StatusBeforeRemove);

        var logs = await Part5bFlow.InDbAsync(app, db => db.AuditLogs.Where(a => a.Details!.StartsWith("#" + id)).Select(a => a.Action + " " + a.Details).ToListAsync());
        Assert.Contains($"CaptureRemoved #{id} by the driver", logs);
        Assert.Contains($"CaptureRestored #{id}", logs);
    }

    [Fact]
    public async Task The_office_removes_an_approved_slip_and_restore_brings_it_back_approved()
    {
        var owner = await _s.CompanyWithOwnerAsync("Office Remove Co", "owner@office-remove.test");
        var driverId = await Part5bFlow.AddDriverWithLoginAsync(app, owner, "Vusi", "Removetwo", "0829990005");
        var viewer = await _s.AddUserAndLoginAsync(owner, "Vera Viewer", "viewer@office-remove.test", "Viewer");
        var id = await Part5bFlow.OfficeUploadAsync(owner, driverId);
        await MakeReadAsync(id, CaptureStatus.Approved);

        // The driver can no longer remove it.
        var phone = await _s.LoginAsync("0829990005", "driver1");
        Assert.DoesNotContain("Remove this slip", await HtmlAsync(phone, $"/driver/slips/{id}"));
        var refused = await Scenario.PostToAsync(phone, $"/driver/slips/{id}", $"/driver/slips/{id}?handler=Remove", new());
        Assert.Contains("can no longer be removed", await refused.Content.ReadAsStringAsync());

        // A Viewer sees no buttons and cannot post.
        var asViewer = await HtmlAsync(viewer, "/company/captures");
        Assert.Contains($"data-row=\"{id}\"", asViewer);
        Assert.DoesNotContain("handler=Remove", asViewer);
        Assert.Equal(HttpStatusCode.NotFound,
            (await Scenario.PostToAsync(viewer, "/company/captures", $"/company/captures?handler=Remove&id={id}", new())).StatusCode);

        var list = await HtmlAsync(owner, "/company/captures");
        Assert.Contains("handler=Remove", list);
        var r = await Scenario.PostToAsync(owner, "/company/captures", $"/company/captures?handler=Remove&id={id}", new() { ["back"] = "" });
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.Equal("/company/captures", r.Headers.Location?.OriginalString);
        Assert.Equal(CaptureStatus.Removed, (await LoadAsync(id)).Status);

        await Scenario.PostToAsync(owner, "/company/captures?show=removed", $"/company/captures?handler=Restore&id={id}", new());
        Assert.Equal(CaptureStatus.Approved, (await LoadAsync(id)).Status);
    }

    [Fact]
    public async Task A_new_row_is_only_given_when_it_fits_the_page_filters()
    {
        var owner = await _s.CompanyWithOwnerAsync("Insert Co", "owner@insert.test");
        var driverId = await Part5bFlow.AddDriverWithLoginAsync(app, owner, "Zola", "Insertone", "0829990006");
        var id = await Part5bFlow.OfficeUploadAsync(owner, driverId);
        var phone = await _s.LoginAsync("0829990006", "driver1");

        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync($"/company/captures?handler=Row&id={id}&match=true")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync($"/company/captures?handler=Row&id={id}&match=true&show=busy")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"/company/captures?handler=Row&id={id}&match=true&show=done")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"/company/captures?handler=Row&id={id}&match=true&from=2020-01-01&to=2020-01-31")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await phone.GetAsync($"/driver/slips?handler=Row&id={id}&match=true")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await phone.GetAsync($"/driver/slips?handler=Row&id={id}&match=true&show=done")).StatusCode);

        // The row keeps the page's filters for Remove.
        var row = await HtmlAsync(owner, $"/company/captures?handler=Row&id={id}&show=busy");
        Assert.Contains("value=\"?show=busy\"", row);

        // Removed: only on the Removed chip; the plain refresh still finds it (the list then drops it).
        await Scenario.PostToAsync(owner, "/company/captures", $"/company/captures?handler=Remove&id={id}", new());
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"/company/captures?handler=Row&id={id}&match=true")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync($"/company/captures?handler=Row&id={id}&match=true&show=removed")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync($"/company/captures?handler=Row&id={id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await phone.GetAsync($"/driver/slips?handler=Row&id={id}&match=true")).StatusCode);

        var page = await HtmlAsync(owner, "/company/captures");
        Assert.Contains("id=\"liveList\"", page);
        Assert.Contains("data-insert=\"1\"", page);
        Assert.Contains("data-drop-removed=\"1\"", page);
        Assert.Contains("data-insert=\"0\"", await HtmlAsync(owner, "/company/captures?show=removed"));
    }
}

public class CaptureActionRuleTests
{
    private static PromptField F(string type) => new() { Key = "x", Label = "X", Type = type };

    [Fact]
    public void Typed_values_are_cleaned_by_their_type()
    {
        Assert.Equal(("1234.5", (string?)null), CaptureActions.Clean(F("number"), " 1 234,5 "));
        Assert.Equal(("8250.75", (string?)null), CaptureActions.Clean(F("money"), "R8 250.75"));
        Assert.NotNull(CaptureActions.Clean(F("number"), "abc").Error);
        Assert.Equal(("2026-11-26", (string?)null), CaptureActions.Clean(F("date"), "26/11/2026"));
        Assert.NotNull(CaptureActions.Clean(F("date"), "soon").Error);
        Assert.Equal(("yes", (string?)null), CaptureActions.Clean(F("yesno"), "Yes"));
        Assert.NotNull(CaptureActions.Clean(F("yesno"), "maybe").Error);
        Assert.Equal(((string?)null, (string?)null), CaptureActions.Clean(F("text"), "   "));
        Assert.NotNull(CaptureActions.Clean(F("text"), new string('a', 501)).Error);
    }

    [Fact]
    public void The_same_value_written_differently_is_not_a_change()
    {
        Assert.True(CaptureActions.SameValue(F("number"), "412.50", "412.5"));
        Assert.True(CaptureActions.SameValue(F("date"), "2026-11-27", "2026-11-27"));
        Assert.True(CaptureActions.SameValue(F("text"), null, null));
        Assert.True(CaptureActions.SameValue(F("text"), "", null));
        Assert.False(CaptureActions.SameValue(F("text"), "A", "a"));
        Assert.False(CaptureActions.SameValue(F("number"), null, "1"));
    }

    [Fact]
    public void Who_may_remove_and_edit()
    {
        Assert.True(CaptureActions.DriverMayRemove(CaptureStatus.Processing));
        Assert.True(CaptureActions.DriverMayRemove(CaptureStatus.NeedsReview));
        Assert.False(CaptureActions.DriverMayRemove(CaptureStatus.Approved));
        Assert.False(CaptureActions.DriverMayRemove(CaptureStatus.Removed));
        Assert.False(CaptureActions.ValuesEditable(new Capture { Status = CaptureStatus.NeedsReview }));
        Assert.True(CaptureActions.ValuesEditable(new Capture { Status = CaptureStatus.NeedsReview, CaptureTypeId = 1 }));
        Assert.Equal("Removed", new CaptureProgress(1, CaptureStatus.Removed).Label);
        Assert.Equal(CaptureShow.Removed, CaptureProgress.ParseShow("removed"));
    }
}
