using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TransLog.Web.Data;

namespace TransLog.Web.Areas.CompanyArea.Pages.Links;

public record LinkRow(DateTime LinkedAt, DateTime? UnlinkedAt, string Driver, string Vehicle, string By);

/// <summary>Who drove which vehicle and when. Newest first, latest 200. The tenant filter keeps it to this company.</summary>
public class IndexModel(AppDbContext db) : PageModel
{
    public List<LinkRow> Rows { get; private set; } = [];

    public int? DriverId { get; private set; }

    public int? VehicleId { get; private set; }

    public string? Filter { get; private set; }

    public async Task OnGetAsync(int? driverId, int? vehicleId)
    {
        DriverId = driverId;
        VehicleId = vehicleId;

        var query = db.DriverVehicleLinks.AsNoTracking().AsQueryable();
        if (driverId is int d)
        {
            query = query.Where(l => l.DriverId == d);
            Filter = await db.Drivers.Where(x => x.Id == d).Select(x => x.FirstName + " " + x.Surname).FirstOrDefaultAsync();
        }
        if (vehicleId is int v)
        {
            query = query.Where(l => l.VehicleId == v);
            Filter = await db.Vehicles.Where(x => x.Id == v).Select(x => x.RegistrationNo).FirstOrDefaultAsync();
        }

        var rows = await query
            .OrderByDescending(l => l.LinkedAt)
            .Take(200)
            .Select(l => new
            {
                l.LinkedAt,
                l.UnlinkedAt,
                l.Driver!.FirstName,
                l.Driver!.Surname,
                l.Vehicle!.RegistrationNo,
                l.LinkedBy,
                l.LinkedByUserId,
            })
            .ToListAsync();

        var ids = rows.Where(r => r.LinkedByUserId != null).Select(r => r.LinkedByUserId!.Value).Distinct().ToList();
        var names = await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName);

        Rows = rows.Select(r => new LinkRow(
            r.LinkedAt,
            r.UnlinkedAt,
            $"{r.FirstName} {r.Surname}",
            r.RegistrationNo,
            (r.LinkedBy == LinkSource.Driver ? "Driver" : "Company")
                + (r.LinkedByUserId is int u && names.TryGetValue(u, out var n) ? $" ({n})" : ""))).ToList();
    }
}
