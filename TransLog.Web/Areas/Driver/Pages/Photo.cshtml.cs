using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TransLog.Web.Data;
using TransLog.Web.Fleet;
using TransLog.Web.Security;

// The folder is Areas/Driver, but the namespace is DriverArea so it does not clash with the Driver class.
namespace TransLog.Web.Areas.DriverArea.Pages;

/// <summary>
/// The driver's own profile photo (Part 4c), taken with the phone camera or picked from the gallery
/// (page-driver-photo.js crops it square). Opened by tapping the picture on the Driver home.
/// </summary>
[RequestSizeLimit(3 * 1024 * 1024)]
[RequestFormLimits(MultipartBodyLengthLimit = 3 * 1024 * 1024)]
public class PhotoModel(AppDbContext db, DriverPhotos photos) : PageModel
{
    public DriverPhotoBox? Box { get; private set; }

    public async Task OnGetAsync()
    {
        if (await MyDriverAsync() is { } d)
            Box = new DriverPhotoBox(d.Id, d.PhotoPath, d.FullName, "/driver/photo?handler=Upload", "/driver", "user");
    }

    /// <summary>Receives the cropped photo. Answers JSON { ok } or, with status 400, { error }.</summary>
    public async Task<IActionResult> OnPostUploadAsync()
    {
        if (await MyDriverAsync() is not { } driver)
            return BadRequest(new { error = "Your login is not linked to a driver. Ask your company to check your driver details." });

        var (photo, thumb, error) = await DriverPhotos.ReadAsync(Request);
        if (photo is null)
            return BadRequest(new { error });
        error = await photos.SaveAsync(driver, photo, thumb, byDriver: true);
        return error is null ? new JsonResult(new { ok = true }) : BadRequest(new { error });
    }

    /// <summary>The logged-in driver's own record, when it is switched on (the tenant filter keeps it in the company).</summary>
    private Task<Driver?> MyDriverAsync()
    {
        var userId = User.GetUserId();
        return db.Drivers.FirstOrDefaultAsync(d => d.UserId == userId && d.IsActive);
    }
}
