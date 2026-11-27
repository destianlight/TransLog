using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using TransLog.Web.Captures;
using TransLog.Web.Data;
using TransLog.Web.Security;

// The folder is Areas/Company, but the namespace is CompanyArea so it does not clash with the Company class.
namespace TransLog.Web.Areas.CompanyArea.Pages;

/// <summary>
/// Office capture (Part 4b) for the Owner, Admins and Capturers: pick a driver (optional) and a vehicle (defaults to that
/// driver's link), choose several photos from a PC or phone; page-office-capture.js shrinks and turns each one in the
/// browser and posts it to the Upload handler as its own capture (source Office, no location).
/// </summary>
[RequestSizeLimit(12 * 1024 * 1024)]
[RequestFormLimits(MultipartBodyLengthLimit = 12 * 1024 * 1024)]
public class CaptureModel(AppDbContext db, CaptureService captures) : PageModel
{
    public List<SelectListItem> DriverChoices { get; private set; } = [];

    public List<SelectListItem> VehicleChoices { get; private set; } = [];

    /// <summary>JSON { driverId: vehicleId } of open links, so the vehicle follows the chosen driver.</summary>
    public string LinksJson { get; private set; } = "{}";

    public async Task OnGetAsync()
    {
        // The tenant filter keeps every list inside this company.
        DriverChoices = await db.Drivers.AsNoTracking()
            .Where(d => d.IsActive)
            .OrderBy(d => d.FirstName).ThenBy(d => d.Surname)
            .Select(d => new SelectListItem(d.FirstName + " " + d.Surname, d.Id.ToString()))
            .ToListAsync();

        var vehicles = await db.Vehicles.AsNoTracking()
            .Where(v => v.IsActive)
            .OrderBy(v => v.RegistrationNo)
            .Select(v => new { v.Id, v.RegistrationNo, v.FleetNo })
            .ToListAsync();
        VehicleChoices = vehicles
            .Select(v => new SelectListItem(string.IsNullOrEmpty(v.FleetNo) ? v.RegistrationNo : $"{v.RegistrationNo} ({v.FleetNo})",
                v.Id.ToString(CultureInfo.InvariantCulture)))
            .ToList();

        var links = await db.DriverVehicleLinks.AsNoTracking()
            .Where(l => l.UnlinkedAt == null)
            .Select(l => new { l.DriverId, l.VehicleId })
            .ToListAsync();
        LinksJson = JsonSerializer.Serialize(links.ToDictionary(l => l.DriverId.ToString(CultureInfo.InvariantCulture), l => l.VehicleId));
    }

    /// <summary>
    /// Receives one photo with the form fields of page-capture-queue.js plus <c>driverId</c> and <c>vehicleId</c> (both optional).
    /// Answers JSON { ok, id, duplicate } or, with status 400, { error }.
    /// </summary>
    public async Task<IActionResult> OnPostUploadAsync()
    {
        if (User.GetUserId() is not int userId || User.GetCompanyId() is not int companyId)
            return BadRequest(new { error = "Log in again." });

        var (upload, error) = await CaptureUpload.ReadAsync(Request, allowOffice: true);
        if (upload is null)
            return BadRequest(new { error });

        var form = await Request.ReadFormAsync();
        var result = await captures.SaveForOfficeAsync(companyId, userId, Id(form["driverId"]), Id(form["vehicleId"]), upload);
        return result.Ok
            ? new JsonResult(new { ok = true, id = result.CaptureId, duplicate = result.Duplicate })
            : BadRequest(new { error = result.Error });
    }

    private static int? Id(string? text) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) && id > 0 ? id : null;
}
