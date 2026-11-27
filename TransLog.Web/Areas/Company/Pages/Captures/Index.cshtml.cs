using System.Globalization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using TransLog.Web.Captures;
using TransLog.Web.Data;
using TransLog.Web.Security;

namespace TransLog.Web.Areas.CompanyArea.Pages.Captures;

/// <summary>
/// The company's captures (Part 4b), for every office role including Viewers. Newest first, 25 per page; filters for
/// dates, driver, vehicle and All / Busy / Done. The dates are the photo date for now; Part 5 adds the slip date.
/// The tenant filter keeps it to this company.
/// </summary>
public class IndexModel(AppDbContext db) : PageModel
{
    public const int PageSize = 25;

    public record Row(int CaptureId, int? ImageId, DateTime CapturedAt, string? Driver, string? Vehicle, CaptureStatus Status,
        ImageSource? Source, bool HasLocation, string By);

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
        Show = CaptureProgress.ParseShow(show);
        From = AppCulture.ParseDay(from);
        To = AppCulture.ParseDay(to);
        DriverId = driverId is > 0 ? driverId : null;
        VehicleId = vehicleId is > 0 ? vehicleId : null;
        PageNo = Math.Clamp(p, 1, 10_000);

        var query = CaptureProgress.Filter(db.Captures.AsNoTracking(), Show);
        if (From is DateOnly f)
        {
            var start = AppCulture.SastDayStartUtc(f);
            query = query.Where(c => c.CapturedAt >= start);
        }
        if (To is DateOnly t)
        {
            var end = AppCulture.SastDayStartUtc(t.AddDays(1));
            query = query.Where(c => c.CapturedAt < end);
        }
        if (DriverId is int d)
            query = query.Where(c => c.DriverId == d);
        if (VehicleId is int v)
            query = query.Where(c => c.VehicleId == v);

        var rows = await query
            .OrderByDescending(c => c.CapturedAt).ThenByDescending(c => c.Id)
            .Skip((PageNo - 1) * PageSize)
            .Take(PageSize + 1)
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
            })
            .ToListAsync();

        HasNext = rows.Count > PageSize;
        if (HasNext)
            rows.RemoveAt(rows.Count - 1);

        // Users have no tenant filter, so only names of this company's users are looked up.
        var companyId = User.GetCompanyId();
        var ids = rows.Select(r => r.CapturedByUserId).Distinct().ToList();
        var names = await db.Users.AsNoTracking()
            .Where(u => ids.Contains(u.Id) && u.CompanyId == companyId)
            .ToDictionaryAsync(u => u.Id, u => u.FullName);

        Rows = rows.Select(r => new Row(r.Id, r.ImageId, r.CapturedAt, r.Driver, r.Vehicle, r.Status, r.Source, r.HasLocation,
            names.TryGetValue(r.CapturedByUserId, out var n) ? n : "")).ToList();

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

    public static string Day(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
