using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TransLog.Web.Data;
using TransLog.Web.Security;

namespace TransLog.Web.Areas.CompanyArea.Pages.Drivers;

/// <summary>
/// The Owner or an Admin edits a driver of their own company. A driver of another company
/// answers "not found", even when the id is typed into the address.
/// </summary>
public class EditModel(AppDbContext db, AuditLogger audit) : PageModel
{
    [BindProperty]
    public DriverForm Form { get; set; } = new();

    public int Id { get; private set; }

    public string DriverName { get; private set; } = "";

    public async Task<IActionResult> OnGetAsync(int id)
    {
        if (await LoadAsync(id) is not { } driver)
            return NotFound();

        Form = DriverForm.From(driver);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        if (await LoadAsync(id) is not { } driver)
            return NotFound();

        var (cell, idNumber) = await FleetChecks.CheckDriverAsync(db, ModelState, Form, driver.Id);
        if (!ModelState.IsValid)
            return Page();

        var changes = new List<string>();
        if (driver.Cell != cell)
            changes.Add($"cell {driver.Cell} -> {cell}");
        if (driver.IsActive != Form.IsActive)
            changes.Add(Form.IsActive ? "switched on" : "switched off");

        driver.Cell = cell;
        driver.IdNumber = idNumber;
        Form.CopyTo(driver);

        // The ID number is never written to the activity log.
        audit.Add("DriverUpdated", $"{driver.FullName}: {(changes.Count == 0 ? "details" : string.Join(", ", changes))}", driver.CompanyId);
        await db.SaveChangesAsync();

        TempData["Message"] = $"{driver.FullName} saved.";
        return RedirectToPage("Index");
    }

    /// <summary>The driver, only when they belong to this company (the tenant filter decides).</summary>
    private async Task<Driver?> LoadAsync(int id)
    {
        var driver = await db.Drivers.FirstOrDefaultAsync(d => d.Id == id);
        if (driver is null)
            return null;

        Id = driver.Id;
        DriverName = driver.FullName;
        return driver;
    }
}
