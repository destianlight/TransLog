using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TransLog.Web.Data;
using TransLog.Web.Fleet;
using TransLog.Web.Security;

namespace TransLog.Web.Areas.CompanyArea.Pages.Vehicles;

/// <summary>
/// The Owner or an Admin edits a vehicle of their own company and the names slips use for it.
/// A vehicle of another company answers "not found", even when its id is typed into the address.
/// </summary>
public class EditModel(AppDbContext db, AuditLogger audit) : PageModel
{
    [BindProperty]
    public VehicleForm Form { get; set; } = new();

    public int Id { get; private set; }

    public string Registration { get; private set; } = "";

    public List<VehicleAlias> Aliases { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(int id)
    {
        if (await LoadAsync(id) is not { } vehicle)
            return NotFound();

        Form = VehicleForm.From(vehicle);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        if (await LoadAsync(id) is not { } vehicle)
            return NotFound();

        var plate = Plates.Normalize(Form.RegistrationNo);
        if (ModelState.IsValid && await FleetChecks.PlateProblemAsync(db, plate, vehicle.Id) is { } problem)
            ModelState.AddModelError("Form.RegistrationNo", problem);
        if (!ModelState.IsValid)
            return Page();

        var changes = new List<string>();
        if (vehicle.RegistrationNo != plate)
            changes.Add($"registration {vehicle.RegistrationNo} -> {plate}");
        if (vehicle.IsActive != Form.IsActive)
            changes.Add(Form.IsActive ? "switched on" : "switched off");

        vehicle.RegistrationNo = plate;
        Form.CopyTo(vehicle);

        audit.Add("VehicleUpdated", $"{plate}: {(changes.Count == 0 ? "details" : string.Join(", ", changes))}", vehicle.CompanyId);
        await db.SaveChangesAsync();

        TempData["Message"] = $"{plate} saved.";
        return RedirectToPage("Index");
    }

    public async Task<IActionResult> OnPostAddAliasAsync(int id, string? newAlias)
    {
        if (await LoadAsync(id) is not { } vehicle)
            return NotFound();

        var alias = Plates.Normalize(newAlias);
        if (await FleetChecks.AliasProblemAsync(db, alias, vehicle) is { } problem)
        {
            TempData["Error"] = problem;
            return RedirectToPage(new { id });
        }

        db.VehicleAliases.Add(new VehicleAlias { CompanyId = vehicle.CompanyId, VehicleId = vehicle.Id, Alias = alias });
        audit.Add("VehicleAliasAdded", $"{vehicle.RegistrationNo}: {alias}", vehicle.CompanyId);
        await db.SaveChangesAsync();

        TempData["Message"] = $"{alias} added.";
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostRemoveAliasAsync(int id, int aliasId)
    {
        if (await LoadAsync(id) is not { } vehicle)
            return NotFound();
        if (vehicle.Aliases.FirstOrDefault(a => a.Id == aliasId) is not { } alias)
            return NotFound();

        db.VehicleAliases.Remove(alias);
        audit.Add("VehicleAliasRemoved", $"{vehicle.RegistrationNo}: {alias.Alias}", vehicle.CompanyId);
        await db.SaveChangesAsync();

        TempData["Message"] = $"{alias.Alias} removed.";
        return RedirectToPage(new { id });
    }

    /// <summary>The vehicle with its names, only when it belongs to this company (the tenant filter decides).</summary>
    private async Task<Vehicle?> LoadAsync(int id)
    {
        var vehicle = await db.Vehicles.Include(v => v.Aliases).FirstOrDefaultAsync(v => v.Id == id);
        if (vehicle is null)
            return null;

        Id = vehicle.Id;
        Registration = vehicle.RegistrationNo;
        Aliases = vehicle.Aliases.OrderBy(a => a.Alias).ToList();
        return vehicle;
    }
}
