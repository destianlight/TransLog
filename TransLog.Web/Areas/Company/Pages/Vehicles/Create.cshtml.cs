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

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (User.GetCompanyId() is not int companyId)
            return NotFound();

        var plate = Plates.Normalize(Form.RegistrationNo);
        if (ModelState.IsValid && await FleetChecks.PlateProblemAsync(db, plate, exceptVehicleId: 0) is { } problem)
            ModelState.AddModelError("Form.RegistrationNo", problem);
        if (!ModelState.IsValid)
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
