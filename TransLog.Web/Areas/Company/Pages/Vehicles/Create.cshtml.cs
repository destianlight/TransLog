using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using TransLog.Web.Data;
using TransLog.Web.Fleet;
using TransLog.Web.Security;

namespace TransLog.Web.Areas.CompanyArea.Pages.Vehicles;

/// <summary>The Owner or an Admin adds a vehicle to their own company.</summary>
public class CreateModel(AppDbContext db, AuditLogger audit) : PageModel
{
    [BindProperty]
    public VehicleForm Form { get; set; } = new();

    /// <summary>Known makes, models and variants for the suggestion lists.</summary>
    public List<VehicleName> Names { get; private set; } = [];

    /// <summary>"Did you mean …?" questions after a save attempt.</summary>
    public List<NameSuggestion> Questions { get; private set; } = [];

    public async Task OnGetAsync() => Names = await VehicleNames.AllAsync(db);

    public async Task<IActionResult> OnPostAsync()
    {
        if (User.GetCompanyId() is not int companyId)
            return NotFound();

        Names = await VehicleNames.AllAsync(db);
        var plate = Plates.Normalize(Form.RegistrationNo);
        if (ModelState.IsValid && await FleetChecks.PlateProblemAsync(db, plate, exceptVehicleId: 0) is { } problem)
            ModelState.AddModelError("Form.RegistrationNo", problem);
        Questions = await FleetChecks.CheckNamesAsync(db, Form);
        if (!ModelState.IsValid || Questions.Count > 0)
            return Page();

        var vehicle = new Vehicle { CompanyId = companyId, RegistrationNo = plate, CreatedAt = DateTime.UtcNow };
        Form.CopyTo(vehicle);
        vehicle.IsActive = true;
        db.Vehicles.Add(vehicle);
        audit.Add("VehicleCreated", plate, companyId);
        await db.SaveChangesAsync();

        TempData["Message"] = $"{plate} added. Add the names that slips print for it below.";
        return RedirectToPage("Edit", new { id = vehicle.Id });
    }
}
