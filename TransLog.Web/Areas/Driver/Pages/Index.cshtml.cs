using Microsoft.AspNetCore.Mvc.RazorPages;
using TransLog.Web.Areas.CompanyArea;
using TransLog.Web.Data;
using TransLog.Web.Security;
using Microsoft.EntityFrameworkCore;

// The folder is Areas/Driver, but the namespace is DriverArea so it does not clash with the Driver class.
namespace TransLog.Web.Areas.DriverArea.Pages;

/// <summary>The driver's home page on the phone: big buttons for the few things a driver does.</summary>
public class IndexModel(AppDbContext db, LinkService links) : PageModel
{
    public string CompanyName { get; private set; } = "";

    public CurrentLinkInfo? Current { get; private set; }

    public bool HasDriver { get; private set; }

    public int DriverId { get; private set; }

    /// <summary>Profile photo (Part 4c); null shows the initials and "Add your photo".</summary>
    public string? PhotoPath { get; private set; }

    public async Task OnGetAsync()
    {
        CompanyName = User.GetCompanyName();
        var userId = User.GetUserId();
        // The tenant filter keeps this inside the driver's own company.
        var driver = await db.Drivers.AsNoTracking().Where(d => d.UserId == userId)
            .Select(d => new { d.Id, d.PhotoPath }).FirstOrDefaultAsync();
        if (driver is not null)
        {
            HasDriver = true;
            DriverId = driver.Id;
            PhotoPath = driver.PhotoPath;
            Current = await links.CurrentAsync(driver.Id);
        }
    }
}
