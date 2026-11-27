using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using TransLog.Web.Data;
using TransLog.Web.Areas.CompanyArea;
using TransLog.Web.Security;

// The folder is Areas/Driver, but the namespace is DriverArea so it does not clash with the Driver class.
namespace TransLog.Web.Areas.DriverArea.Pages;

/// <summary>
/// The driver's own page (Driver area, Part 3c): which vehicle they have, and (when the company allows it) picking another one.
/// The server checks the company setting, so a hidden form cannot get around it.
/// </summary>
public class MyVehicleModel(AppDbContext db, LinkService links) : PageModel
{
    public bool HasDriver { get; private set; }

    public string DriverName { get; private set; } = "";

    public bool CanChoose { get; private set; }

    public CurrentLinkInfo? Current { get; private set; }

    public List<SelectListItem> VehicleChoices { get; private set; } = [];

    public TakeOverInfo? TakeOver { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostLinkAsync(int vehicleId, bool confirm)
    {
        var driver = await LoadAsync();
        if (driver is null)
            return RedirectToPage();
        if (!CanChoose)
        {
            TempData["Error"] = "Your company links drivers to vehicles. Ask them to change it.";
            return RedirectToPage();
        }
        if (vehicleId == 0)
        {
            TempData["Error"] = "Choose a vehicle.";
            return RedirectToPage();
        }

        var outcome = await links.LinkAsync(driver.Id, vehicleId, LinkSource.Driver, confirm);
        switch (outcome.Result)
        {
            case LinkResult.TakenByOther:
                TakeOver = await links.TakeOverAsync(vehicleId, outcome);
                return Page();
            case LinkResult.NotFound:
                TempData["Error"] = "That vehicle was not found.";
                break;
            case LinkResult.Inactive:
                TempData["Error"] = "That vehicle is switched off.";
                break;
            case LinkResult.AlreadyLinked:
                TempData["Message"] = "You already have that vehicle.";
                break;
            default:
                TempData["Message"] = "Done. That is now your vehicle.";
                break;
        }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostUnlinkAsync()
    {
        var driver = await LoadAsync();
        if (driver is null)
            return RedirectToPage();
        if (!CanChoose)
        {
            TempData["Error"] = "Your company links drivers to vehicles. Ask them to change it.";
            return RedirectToPage();
        }

        TempData["Message"] = await links.UnlinkDriverAsync(driver.Id) ? "You no longer have a vehicle." : "You had no vehicle.";
        return RedirectToPage();
    }

    /// <summary>The driver record of the logged-in user, or null when the login is not linked to a driver.</summary>
    private async Task<Driver?> LoadAsync()
    {
        var userId = User.GetUserId();
        var driver = userId is null ? null : await db.Drivers.FirstOrDefaultAsync(d => d.UserId == userId);
        var company = User.GetCompanyId() is int companyId ? await db.Companies.FindAsync(companyId) : null;
        CanChoose = company?.DriverLinking == DriverLinking.DriversAndCompany;
        if (driver is null)
            return null;

        HasDriver = true;
        DriverName = driver.FullName;
        Current = await links.CurrentAsync(driver.Id);
        VehicleChoices = await links.VehicleChoicesAsync();
        return driver;
    }
}
