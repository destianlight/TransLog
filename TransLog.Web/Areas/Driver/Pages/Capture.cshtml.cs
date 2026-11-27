using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TransLog.Web.Captures;
using TransLog.Web.Data;
using TransLog.Web.Security;

// The folder is Areas/Driver, but the namespace is DriverArea so it does not clash with the Driver class.
namespace TransLog.Web.Areas.DriverArea.Pages;

/// <summary>
/// The camera page (Part 4a): fills the browser window, no menu. The photo is taken, turned and shrunk on the phone
/// (page-capture.js), kept on the phone until sent (page-capture-queue.js) and posted to the Upload handler.
/// </summary>
[RequestSizeLimit(12 * 1024 * 1024)]
[RequestFormLimits(MultipartBodyLengthLimit = 12 * 1024 * 1024)]
public class CaptureModel(AppDbContext db, CaptureService captures) : PageModel
{
    public bool HasDriver { get; private set; }

    /// <summary>The driver's linked vehicle, shown at the top; null when none (the photo can still be sent).</summary>
    public string? Vehicle { get; private set; }

    public async Task OnGetAsync()
    {
        var userId = User.GetUserId();
        // The tenant filter keeps this inside the driver's own company.
        var driver = await db.Drivers.AsNoTracking().FirstOrDefaultAsync(d => d.UserId == userId && d.IsActive);
        if (driver is null)
            return;
        HasDriver = true;
        Vehicle = await db.DriverVehicleLinks
            .Where(l => l.DriverId == driver.Id && l.UnlinkedAt == null)
            .Select(l => l.Vehicle!.RegistrationNo)
            .FirstOrDefaultAsync();
    }

    /// <summary>
    /// Receives one photo. Answers JSON: { ok, id, duplicate } or, with status 400, { error } (the phone shows it and
    /// stops retrying that photo). The anti-forgery token comes in the RequestVerificationToken header.
    /// </summary>
    public async Task<IActionResult> OnPostUploadAsync()
    {
        if (User.GetUserId() is not int userId)
            return BadRequest(new { error = "Log in again." });

        var (upload, error) = await CaptureUpload.ReadAsync(Request);
        if (upload is null)
            return BadRequest(new { error });

        var result = await captures.SaveForDriverAsync(userId, upload);
        return result.Ok
            ? new JsonResult(new { ok = true, id = result.CaptureId, duplicate = result.Duplicate })
            : BadRequest(new { error = result.Error });
    }
}
