using System.Globalization;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using TransLog.Web.Data;
using TransLog.Web.Security;

// The folder is Areas/Company, but the namespace is CompanyArea so it does not clash with the Company class.
namespace TransLog.Web.Areas.CompanyArea;

public enum LinkResult
{
    Linked,
    AlreadyLinked,

    /// <summary>The vehicle is linked to another driver and the caller has not confirmed the take-over.</summary>
    TakenByOther,

    NotFound,

    /// <summary>The driver or the vehicle is switched off.</summary>
    Inactive,
}

public record LinkOutcome(LinkResult Result, string? OtherDriver = null, DateTime? Since = null);

/// <summary>A driver's open link, for the pages.</summary>
public record CurrentLinkInfo(string Vehicle, DateTime Since);

/// <summary>What the "take it over?" question shows.</summary>
public record TakeOverInfo(int VehicleId, string Vehicle, string OtherDriver, DateTime Since);

/// <summary>
/// Links drivers to vehicles. Every change is a history row: a link is closed (UnlinkedAt) and never deleted.
/// The tenant filter keeps every query inside the logged-in company, so ids from another company are "not found".
/// </summary>
public class LinkService(AppDbContext db, AuditLogger audit, IHttpContextAccessor http)
{
    private int? UserId => http.HttpContext?.User.GetUserId();

    /// <summary>
    /// Links the driver to the vehicle. The driver's old link is closed. A vehicle that another driver has is only
    /// taken over when <paramref name="takeOver"/> is true; the other driver's link is then closed too.
    /// </summary>
    public async Task<LinkOutcome> LinkAsync(int driverId, int vehicleId, LinkSource by, bool takeOver)
    {
        var driver = await db.Drivers.FirstOrDefaultAsync(d => d.Id == driverId);
        var vehicle = await db.Vehicles.FirstOrDefaultAsync(v => v.Id == vehicleId);
        if (driver is null || vehicle is null)
            return new LinkOutcome(LinkResult.NotFound);
        if (!driver.IsActive || !vehicle.IsActive)
            return new LinkOutcome(LinkResult.Inactive);

        var mine = await db.DriverVehicleLinks.FirstOrDefaultAsync(l => l.DriverId == driverId && l.UnlinkedAt == null);
        if (mine is not null && mine.VehicleId == vehicleId)
            return new LinkOutcome(LinkResult.AlreadyLinked);

        var theirs = await db.DriverVehicleLinks
            .Include(l => l.Driver)
            .FirstOrDefaultAsync(l => l.VehicleId == vehicleId && l.UnlinkedAt == null);
        if (theirs is not null && !takeOver)
            return new LinkOutcome(LinkResult.TakenByOther, theirs.Driver?.FullName, theirs.LinkedAt);

        // Close first, then open: two saves in one transaction, because the database allows only one open
        // link per driver and per vehicle.
        await using var tx = await db.Database.BeginTransactionAsync();
        var now = DateTime.UtcNow;
        var userId = UserId;

        if (mine is not null)
        {
            mine.UnlinkedAt = now;
            mine.UnlinkedByUserId = userId;
        }
        if (theirs is not null)
        {
            theirs.UnlinkedAt = now;
            theirs.UnlinkedByUserId = userId;
        }
        await db.SaveChangesAsync();

        db.DriverVehicleLinks.Add(new DriverVehicleLink
        {
            CompanyId = driver.CompanyId,
            DriverId = driver.Id,
            VehicleId = vehicle.Id,
            LinkedAt = now,
            LinkedBy = by,
            LinkedByUserId = userId,
        });
        var took = theirs?.Driver is { } other ? $" (took over from {other.FullName})" : "";
        audit.Add("DriverLinked", $"{driver.FullName} -> {vehicle.RegistrationNo} by {by}{took}", driver.CompanyId);
        await db.SaveChangesAsync();
        await tx.CommitAsync();

        return new LinkOutcome(LinkResult.Linked);
    }

    /// <summary>Closes the driver's open link. False when there was none.</summary>
    public async Task<bool> UnlinkDriverAsync(int driverId)
    {
        var link = await db.DriverVehicleLinks
            .Include(l => l.Driver).Include(l => l.Vehicle)
            .FirstOrDefaultAsync(l => l.DriverId == driverId && l.UnlinkedAt == null);
        return await CloseAsync(link);
    }

    /// <summary>Closes the vehicle's open link. False when there was none.</summary>
    public async Task<bool> UnlinkVehicleAsync(int vehicleId)
    {
        var link = await db.DriverVehicleLinks
            .Include(l => l.Driver).Include(l => l.Vehicle)
            .FirstOrDefaultAsync(l => l.VehicleId == vehicleId && l.UnlinkedAt == null);
        return await CloseAsync(link);
    }

    private async Task<bool> CloseAsync(DriverVehicleLink? link)
    {
        if (link is null)
            return false;

        link.UnlinkedAt = DateTime.UtcNow;
        link.UnlinkedByUserId = UserId;
        audit.Add("DriverUnlinked", $"{link.Driver?.FullName} from {link.Vehicle?.RegistrationNo}", link.CompanyId);
        await db.SaveChangesAsync();
        return true;
    }

    /// <summary>The driver's open link, or null.</summary>
    public async Task<CurrentLinkInfo?> CurrentAsync(int driverId)
    {
        var open = await db.DriverVehicleLinks.AsNoTracking()
            .Where(l => l.DriverId == driverId && l.UnlinkedAt == null)
            .Select(l => new { l.Vehicle!.RegistrationNo, l.LinkedAt })
            .FirstOrDefaultAsync();
        return open is null ? null : new CurrentLinkInfo(open.RegistrationNo, open.LinkedAt);
    }

    /// <summary>Active vehicles for a pick list; a vehicle another driver has says so.</summary>
    public async Task<List<SelectListItem>> VehicleChoicesAsync()
    {
        var taken = await db.DriverVehicleLinks.AsNoTracking()
            .Where(l => l.UnlinkedAt == null)
            .Select(l => new { l.VehicleId, l.Driver!.FirstName, l.Driver!.Surname })
            .ToListAsync();
        var vehicles = await db.Vehicles.AsNoTracking()
            .Where(v => v.IsActive)
            .OrderBy(v => v.RegistrationNo)
            .ToListAsync();

        return vehicles.Select(v =>
        {
            var label = v.RegistrationNo;
            if (!string.IsNullOrEmpty(v.FleetNo))
                label += $" ({v.FleetNo})";
            var holder = taken.FirstOrDefault(t => t.VehicleId == v.Id);
            if (holder is not null)
                label += $" - with {holder.FirstName} {holder.Surname}".TrimEnd();
            return new SelectListItem(label, v.Id.ToString(CultureInfo.InvariantCulture));
        }).ToList();
    }

    /// <summary>The details for the "take it over?" question.</summary>
    public async Task<TakeOverInfo> TakeOverAsync(int vehicleId, LinkOutcome outcome)
    {
        var reg = await db.Vehicles.Where(v => v.Id == vehicleId).Select(v => v.RegistrationNo).FirstAsync();
        return new TakeOverInfo(vehicleId, reg, outcome.OtherDriver ?? "another driver", outcome.Since ?? DateTime.UtcNow);
    }
}
