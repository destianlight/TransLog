using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TransLog.Web.Data;
using TransLog.Web.Security;

// The folder is Areas/Driver, but the namespace is DriverArea so it does not clash with the Driver class.
namespace TransLog.Web.Areas.DriverArea.Pages;

/// <summary>
/// My slips (Part 4a): photos still waiting on the phone (drawn by page-capture-queue.js) and the driver's last 50 sent captures.
/// </summary>
public class SlipsModel(AppDbContext db) : PageModel
{
    public record SlipRow(int CaptureId, int? ImageId, DateTime CapturedAt, string? Vehicle, CaptureStatus Status, ImageSource? Source, bool HasLocation);

    public List<SlipRow> Slips { get; private set; } = [];

    public async Task OnGetAsync()
    {
        var userId = User.GetUserId();
        // The tenant filter keeps this inside the driver's own company.
        Slips = await db.Captures.AsNoTracking()
            .Where(c => c.CapturedByUserId == userId || c.Driver!.UserId == userId)
            .OrderByDescending(c => c.CapturedAt)
            .Take(50)
            .Select(c => new SlipRow(
                c.Id,
                c.Images.OrderBy(i => i.SortOrder).Select(i => (int?)i.Id).FirstOrDefault(),
                c.CapturedAt,
                c.Vehicle != null ? c.Vehicle.RegistrationNo : null,
                c.Status,
                c.Images.OrderBy(i => i.SortOrder).Select(i => (ImageSource?)i.Source).FirstOrDefault(),
                c.Images.Any(i => i.Latitude != null)))
            .ToListAsync();
    }

    public static (string Text, string Css) StatusLabel(CaptureStatus s) => s switch
    {
        CaptureStatus.Processing => ("Waiting to be read", "text-bg-secondary"),
        CaptureStatus.NeedsReview => ("Being checked", "text-bg-warning"),
        CaptureStatus.Approved => ("Approved", "text-bg-success"),
        CaptureStatus.Rejected => ("Rejected", "text-bg-danger"),
        CaptureStatus.Failed => ("Could not be read", "text-bg-danger"),
        _ => ("Uploading", "text-bg-secondary"),
    };
}
