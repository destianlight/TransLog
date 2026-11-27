using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TransLog.Web.Captures;
using TransLog.Web.Data;
using TransLog.Web.Security;

// The folder is Areas/Driver, but the namespace is DriverArea so it does not clash with the Driver class.
namespace TransLog.Web.Areas.DriverArea.Pages;

/// <summary>
/// My slips: photos still waiting on the phone (drawn by page-capture-queue.js) and the driver's sent captures.
/// Part 4b: the 15 most recent first with "Show 15 more", chips All / Busy / Done, and one day to show.
/// </summary>
public class SlipsModel(AppDbContext db) : PageModel
{
    public const int PageSize = 15;

    public record SlipRow(int CaptureId, int? ImageId, DateTime CapturedAt, string? Vehicle, CaptureStatus Status, ImageSource? Source, bool HasLocation);

    public List<SlipRow> Slips { get; private set; } = [];

    public CaptureShow Show { get; private set; }

    /// <summary>The South African day to show; null shows every day.</summary>
    public DateOnly? Day { get; private set; }

    public int Take { get; private set; } = PageSize;

    public bool HasMore { get; private set; }

    public bool Filtered => Show != CaptureShow.All || Day is not null;

    public async Task OnGetAsync(string? show, string? day, int take = PageSize)
    {
        Show = CaptureProgress.ParseShow(show);
        Day = AppCulture.ParseDay(day);
        Take = Math.Clamp(take / PageSize * PageSize, PageSize, 20 * PageSize);

        var userId = User.GetUserId();
        // The tenant filter keeps this inside the driver's own company.
        var query = db.Captures.AsNoTracking().Where(c => c.CapturedByUserId == userId || c.Driver!.UserId == userId);
        query = CaptureProgress.Filter(query, Show);
        if (Day is DateOnly d)
        {
            DateTime start = AppCulture.SastDayStartUtc(d), end = AppCulture.SastDayStartUtc(d.AddDays(1));
            query = query.Where(c => c.CapturedAt >= start && c.CapturedAt < end);
        }

        Slips = await query
            .OrderByDescending(c => c.CapturedAt).ThenByDescending(c => c.Id)
            .Take(Take + 1)
            .Select(c => new SlipRow(
                c.Id,
                c.Images.OrderBy(i => i.SortOrder).Select(i => (int?)i.Id).FirstOrDefault(),
                c.CapturedAt,
                c.Vehicle != null ? c.Vehicle.RegistrationNo : null,
                c.Status,
                c.Images.OrderBy(i => i.SortOrder).Select(i => (ImageSource?)i.Source).FirstOrDefault(),
                c.Images.Any(i => i.Latitude != null)))
            .ToListAsync();

        HasMore = Slips.Count > Take;
        if (HasMore)
            Slips.RemoveAt(Slips.Count - 1);
    }

    /// <summary>Query values for a link that keeps the current filters, with some changed.</summary>
    public Dictionary<string, string> With(CaptureShow? show = null, int? take = null)
    {
        var s = show ?? Show;
        var values = new Dictionary<string, string>();
        if (s != CaptureShow.All)
            values["show"] = s.ToString().ToLowerInvariant();
        if (Day is DateOnly d)
            values["day"] = d.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        if (take is int t && t > PageSize)
            values["take"] = t.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return values;
    }
}
