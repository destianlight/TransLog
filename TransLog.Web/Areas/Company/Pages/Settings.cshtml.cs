using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using TransLog.Web.Data;
using TransLog.Web.Security;

namespace TransLog.Web.Areas.CompanyArea.Pages;

/// <summary>The Owner edits the company's contact details and who may link drivers to vehicles.</summary>
public class SettingsModel(AppDbContext db, AuditLogger audit) : PageModel
{
    [BindProperty]
    public CompanySettingsForm Form { get; set; } = new();

    public string CompanyName { get; private set; } = "";

    public async Task<IActionResult> OnGetAsync()
    {
        if (await LoadAsync() is not { } company)
            return NotFound();

        Form = CompanySettingsForm.From(company);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (await LoadAsync() is not { } company)
            return NotFound();
        if (!ModelState.IsValid)
            return Page();

        Form.CopyTo(company);
        audit.Add("CompanySettingsUpdated", $"driver linking: {company.DriverLinking}, drivers correct values: {(company.DriverEditsLocked ? "no" : "yes")}", company.Id);
        await db.SaveChangesAsync();

        TempData["Message"] = "Settings saved.";
        return RedirectToPage();
    }

    private async Task<Company?> LoadAsync()
    {
        if (User.GetCompanyId() is not int companyId || await db.Companies.FindAsync(companyId) is not { } company)
            return null;
        CompanyName = company.Name;
        return company;
    }
}
