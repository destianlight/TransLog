using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TransLog.Web.Data;
using TransLog.Web.Fleet;

namespace TransLog.Web.Areas.CompanyArea.Pages.Vehicles;

/// <summary>The company's vehicles. No "where CompanyId": the tenant filter limits the list to this company.</summary>
public class IndexModel(AppDbContext db) : PageModel
{
    public List<Vehicle> Vehicles { get; private set; } = [];

    public DateOnly Today { get; private set; }

    /// <summary>Vehicle id to the name of the driver who has it now.</summary>
    public Dictionary<int, string> DriverNames { get; private set; } = new();

    public async Task OnGetAsync()
    {
        Today = Expiry.Today();
        Vehicles = await db.Vehicles
            .AsNoTracking()
            .Include(v => v.Aliases)
            .Include(v => v.VehicleModel!).ThenInclude(m => m.Make)
            .OrderByDescending(v => v.IsActive)
            .ThenBy(v => v.RegistrationNo)
            .ToListAsync();

        var open = await db.DriverVehicleLinks
            .AsNoTracking()
            .Where(l => l.UnlinkedAt == null)
            .Select(l => new { l.VehicleId, l.Driver!.FirstName, l.Driver!.Surname })
            .ToListAsync();
        DriverNames = open.ToDictionary(x => x.VehicleId, x => $"{x.FirstName} {x.Surname}");
    }
}
