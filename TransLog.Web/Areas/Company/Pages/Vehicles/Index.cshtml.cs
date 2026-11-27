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

    public async Task OnGetAsync()
    {
        Today = Expiry.Today();
        Vehicles = await db.Vehicles
            .AsNoTracking()
            .Include(v => v.Aliases)
            .OrderByDescending(v => v.IsActive)
            .ThenBy(v => v.RegistrationNo)
            .ToListAsync();
    }
}
