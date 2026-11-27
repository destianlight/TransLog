using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using TransLog.Web.Data;
using TransLog.Web.Security;

namespace TransLog.Web.Areas.CompanyArea.Pages.Drivers;

/// <summary>The Owner or an Admin adds a driver to their own company.</summary>
public class CreateModel(AppDbContext db, AuditLogger audit) : PageModel
{
    [BindProperty]
    public DriverForm Form { get; set; } = new();

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (User.GetCompanyId() is not int companyId)
            return NotFound();

        var (cell, idNumber) = await FleetChecks.CheckDriverAsync(db, ModelState, Form, exceptDriverId: 0);
        if (!ModelState.IsValid)
            return Page();

        var driver = new Driver { CompanyId = companyId, Cell = cell, IdNumber = idNumber, CreatedAt = DateTime.UtcNow };
        Form.CopyTo(driver);
        driver.IsActive = true;
        db.Drivers.Add(driver);
        audit.Add("DriverCreated", $"{driver.FullName} ({cell})", companyId);
        await db.SaveChangesAsync();

        TempData["Message"] = $"{driver.FullName} added.";
        return RedirectToPage("Index");
    }
}
