using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using TransLog.Web.Captures;
using TransLog.Web.Data;
using TransLog.Web.Security;

namespace TransLog.Web.Areas.CompanyArea.Pages.Captures;

/// <summary>
/// The company's captures (Part 4b), for every office role including Viewers. Newest first, 25 per page; filters for
/// dates, driver, vehicle and All / Busy / Done. Part 5b: the dates are slip dates (the photo date until a slip is read),
/// and rows are fetched again one by one when a live message arrives.
/// The tenant filter keeps it to this company.
/// </summary>
public class IndexModel(AppDbContext db, CaptureActions actions) : PageModel
{
    public const int PageSize = 25;

    public record Row(int CaptureId, int? ImageId, DateTime CapturedAt, string? Driver, string? Vehicle, CaptureStatus Status,
        ImageSource? Source, bool HasLocation, string By)
    {
        // Part 5a2b2: what the AI read.
        public string? TypeName { get; init; }

        public DateOnly? EventDate { get; init; }

        /// <summary>Why it needs a look ("Type not sure", "Could not be read: …"); only shown while being checked.</summary>
        public string? Note { get; init; }

        /// <summary>The values read, as (label, value), only those with a value, in the type's field order.</summary>
        public List<(string Label, string Value)> Values { get; init; } = [];

        /// <summary>Part 5b: the AI is reading it right now.</summary>
        public bool Reading { get; init; }

        // Part 5b2: removed slips.
        public DateTime? RemovedAt { get; init; }

        public string? RemovedBy { get; init; }
    }

    /// <summary>Part 5b2: Owner, Admin and Capturer may remove and restore slips (not Viewers).</summary>
    public bool CanRemove => User.IsInRole(Roles.Owner) || User.IsInRole(Roles.Admin) || User.IsInRole(Roles.Capturer);

    /// <summary>Part 5b2: the current filters as a query string ("?show=busy&amp;p=2"), to come back to after Remove or Restore.</summary>
    public string Back { get; private set; } = "";

    public List<Row> Rows { get; private set; } = [];

    public List<SelectListItem> DriverChoices { get; private set; } = [];

    public List<SelectListItem> VehicleChoices { get; private set; } = [];

    public CaptureShow Show { get; private set; }

    public DateOnly? From { get; private set; }

    public DateOnly? To { get; private set; }

    public int? DriverId { get; private set; }

    public int? VehicleId { get; private set; }

    public int PageNo { get; private set; } = 1;

    public bool HasNext { get; private set; }

    public bool Filtered => Show != CaptureShow.All || From is not null || To is not null || DriverId is not null || VehicleId is not null;

    public async Task OnGetAsync(string? show, string? from, string? to, int? driverId, int? vehicleId, int p = 1)
    {
        ReadFilters(show, from, to, driverId, vehicleId, p);
        var query = ApplyFilters(db.Captures.AsNoTracking());

        // Newest photo first, so an old slip sent today is still on top.
        Rows = await RowsAsync(query
            .OrderByDescending(c => c.CapturedAt).ThenByDescending(c => c.Id)
            .Skip((PageNo - 1) * PageSize)
            .Take(PageSize + 1));

        HasNext = Rows.Count > PageSize;
        if (HasNext)
            Rows.RemoveAt(Rows.Count - 1);

        await LoadChoicesAsync();
    }

    private void ReadFilters(string? show, string? from, string? to, int? driverId, int? vehicleId, int p)
    {
        Show = CaptureProgress.ParseShow(show);
        From = AppCulture.ParseDay(from);
        To = AppCulture.ParseDay(to);
        DriverId = driverId is > 0 ? driverId : null;
        VehicleId = vehicleId is > 0 ? vehicleId : null;
        PageNo = Math.Clamp(p, 1, 10_000);
        var values = With(page: PageNo);
        Back = values.Count == 0 ? "" : "?" + string.Join("&", values.Select(kv => Uri.EscapeDataString(kv.Key) + "=" + Uri.EscapeDataString(kv.Value)));
        ViewData["CanRemove"] = CanRemove;
        ViewData["Back"] = Back;
    }

    private IQueryable<Capture> ApplyFilters(IQueryable<Capture> captures)
    {
        var query = CaptureProgress.Filter(captures, Show);
        // Part 5b: From and To are slip dates (photo date while a slip has none yet).
        if (From is DateOnly f)
            query = CaptureProgress.FromDay(query, f);
        if (To is DateOnly t)
            query = CaptureProgress.ToDay(query, t);
        if (DriverId is int d)
            query = query.Where(c => c.DriverId == d);
        if (VehicleId is int v)
            query = query.Where(c => c.VehicleId == v);
        return query;
    }

    private async Task LoadChoicesAsync()
    {
        DriverChoices = (await db.Drivers.AsNoTracking()
                .OrderByDescending(x => x.IsActive).ThenBy(x => x.FirstName).ThenBy(x => x.Surname)
                .Select(x => new { x.Id, Name = x.FirstName + " " + x.Surname, x.IsActive })
                .ToListAsync())
            .Select(x => new SelectListItem(x.IsActive ? x.Name : x.Name + " (off)", x.Id.ToString(CultureInfo.InvariantCulture), x.Id == DriverId))
            .ToList();
        VehicleChoices = (await db.Vehicles.AsNoTracking()
                .OrderByDescending(x => x.IsActive).ThenBy(x => x.RegistrationNo)
                .Select(x => new { x.Id, x.RegistrationNo, x.IsActive })
                .ToListAsync())
            .Select(x => new SelectListItem(x.IsActive ? x.RegistrationNo : x.RegistrationNo + " (off)", x.Id.ToString(CultureInfo.InvariantCulture), x.Id == VehicleId))
            .ToList();
    }

    /// <summary>
    /// Part 5b: one row again, for the live update (page-capture-live.js). Other companies' slips are not found.
    /// The page's filters come along (for Back); with <paramref name="match"/> (5b2, a new slip to put on top) the row is
    /// only found when it fits those filters.
    /// </summary>
    public async Task<IActionResult> OnGetRowAsync(int id, bool match, string? show, string? from, string? to, int? driverId, int? vehicleId, int p = 1)
    {
        ReadFilters(show, from, to, driverId, vehicleId, p);
        var query = match ? ApplyFilters(db.Captures.AsNoTracking()) : db.Captures.AsNoTracking();
        var rows = await RowsAsync(query.Where(c => c.Id == id));
        return rows.Count == 0 ? NotFound() : Partial(RowView, rows[0]);
    }

    /// <summary>Part 5b2: soft delete (a wrong photo, a duplicate). Also for approved or rejected slips.</summary>
    public async Task<IActionResult> OnPostRemoveAsync(int id, string? back)
    {
        if (!CanRemove || await db.Captures.FirstOrDefaultAsync(c => c.Id == id) is not { } capture)
            return NotFound();
        await actions.RemoveAsync(capture, User.GetUserId()!.Value, byDriver: false);
        return BackTo(back);
    }

    public async Task<IActionResult> OnPostRestoreAsync(int id, string? back)
    {
        if (!CanRemove || await db.Captures.FirstOrDefaultAsync(c => c.Id == id) is not { } capture)
            return NotFound();
        await actions.RestoreAsync(capture);
        return BackTo(back);
    }

    private IActionResult BackTo(string? back) =>
        LocalRedirect("/company/captures" + (back is { Length: > 1 } b && b[0] == '?' && !b.Contains('\\') ? b : ""));

    public const string RowView = "/Areas/Company/Pages/Captures/_CaptureRow.cshtml";

    /// <summary>Rows for an ordered and paged query (the tenant filter keeps it to this company).</summary>
    private async Task<List<Row>> RowsAsync(IQueryable<Capture> query)
    {
        var now = DateTime.UtcNow;
        var rows = await query
            .Select(c => new
            {
                c.Id,
                ImageId = c.Images.OrderBy(i => i.SortOrder).Select(i => (int?)i.Id).FirstOrDefault(),
                c.CapturedAt,
                Driver = c.Driver != null ? c.Driver.FirstName + " " + c.Driver.Surname : null,
                Vehicle = c.Vehicle != null ? c.Vehicle.RegistrationNo : null,
                c.Status,
                Source = c.Images.OrderBy(i => i.SortOrder).Select(i => (ImageSource?)i.Source).FirstOrDefault(),
                HasLocation = c.Images.Any(i => i.Latitude != null),
                c.CapturedByUserId,
                TypeName = c.CaptureType != null ? c.CaptureType.Name : null,
                FieldsJson = c.CaptureType != null ? c.CaptureType.FieldsJson : null,
                c.EventDate,
                c.ErrorMessage,
                c.DataJson,
                Reading = c.Status == CaptureStatus.Processing && c.AiLockedUntil != null && c.AiLockedUntil > now,
                c.RemovedAt,
                c.RemovedByUserId,
            })
            .ToListAsync();

        // Users have no tenant filter, so only names of this company's users are looked up.
        var companyId = User.GetCompanyId();
        var ids = rows.Select(r => r.CapturedByUserId).Concat(rows.Where(r => r.RemovedByUserId != null).Select(r => r.RemovedByUserId!.Value)).Distinct().ToList();
        var names = await db.Users.AsNoTracking()
            .Where(u => ids.Contains(u.Id) && u.CompanyId == companyId)
            .ToDictionaryAsync(u => u.Id, u => u.FullName);

        return rows.Select(r => new Row(r.Id, r.ImageId, r.CapturedAt, r.Driver, r.Vehicle, r.Status, r.Source, r.HasLocation,
            names.TryGetValue(r.CapturedByUserId, out var n) ? n : "")
        {
            TypeName = r.TypeName,
            EventDate = r.EventDate,
            Note = r.Status == CaptureStatus.NeedsReview ? r.ErrorMessage : null,
            Values = ValuesOf(r.FieldsJson, r.DataJson),
            Reading = r.Reading,
            RemovedAt = r.RemovedAt,
            RemovedBy = r.RemovedByUserId is int by && names.TryGetValue(by, out var rn) ? rn : null,
        }).ToList();
    }

    /// <summary>Query values for a link that keeps the current filters, with the chip or page changed.</summary>
    public Dictionary<string, string> With(CaptureShow? show = null, int? page = null)
    {
        var values = new Dictionary<string, string>();
        var s = show ?? Show;
        if (s != CaptureShow.All)
            values["show"] = s.ToString().ToLowerInvariant();
        if (From is DateOnly f)
            values["from"] = Day(f);
        if (To is DateOnly t)
            values["to"] = Day(t);
        if (DriverId is int d)
            values["driverId"] = d.ToString(CultureInfo.InvariantCulture);
        if (VehicleId is int v)
            values["vehicleId"] = v.ToString(CultureInfo.InvariantCulture);
        if (page is > 1)
            values["p"] = page.Value.ToString(CultureInfo.InvariantCulture);
        return values;
    }

    /// <summary>Part 5a2b2: the read values with the type's labels (keys the type no longer has keep their key).</summary>
    public static List<(string Label, string Value)> ValuesOf(string? fieldsJson, string? dataJson)
    {
        var data = TransLog.Web.Ai.CaptureReader.ReadDataJson(dataJson);
        if (data.Count == 0)
            return [];
        var labels = TransLog.Web.Ai.PromptRules.FieldsFromJson(fieldsJson).ToDictionary(f => f.Key, f => f.Label);
        return data.Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
            .Select(kv => (labels.TryGetValue(kv.Key, out var label) ? label : kv.Key, kv.Value!))
            .ToList();
    }

    public static string Day(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
