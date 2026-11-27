using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TransLog.Web.Data;
using TransLog.Web.Fleet;

namespace TransLog.Web.Areas.CompanyArea.Pages.Drivers;

/// <summary>The company's drivers. No "where CompanyId": the tenant filter limits the list to this company.</summary>
public class IndexModel(AppDbContext db) : PageModel
{
    public List<Driver> Drivers { get; private set; } = [];

    public DateOnly Today { get; private set; }

    public async Task OnGetAsync()
    {
        Today = Expiry.Today();
        Drivers = await db.Drivers
            .AsNoTracking()
            .OrderByDescending(d => d.IsActive)
            .ThenBy(d => d.Surname)
            .ThenBy(d => d.FirstName)
            .ToListAsync();
    }
}
