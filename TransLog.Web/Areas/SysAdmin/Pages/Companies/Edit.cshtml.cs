using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TransLog.Web.Data;
using TransLog.Web.Security;

namespace TransLog.Web.Areas.SysAdmin.Pages.Companies;

public class EditModel(AppDbContext db, UserManager<AppUser> users, AuditLogger audit) : PageModel
{
    [BindProperty]
    public CompanyForm Form { get; set; } = new();

    [BindProperty]
    public bool IsActive { get; set; }

    public int Id { get; private set; }

    public string Name { get; private set; } = "";

    public List<UserRow> Users { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var company = await db.Companies.FindAsync(id);
        if (company is null)
            return NotFound();

        Form = CompanyForm.From(company);
        IsActive = company.IsActive;
        await LoadAsync(company);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        var company = await db.Companies.FindAsync(id);
        if (company is null)
            return NotFound();

        if (!ModelState.IsValid)
        {
            await LoadAsync(company);
            return Page();
        }

        var wasActive = company.IsActive;
        Form.CopyTo(company);
        company.IsActive = IsActive;

        if (wasActive && !IsActive)
        {
            // Log out everyone in the company within 5 minutes.
            foreach (var user in await db.Users.Where(u => u.CompanyId == id).ToListAsync())
                await users.UpdateSecurityStampAsync(user);
        }

        var action = wasActive == IsActive ? "CompanyUpdated" : IsActive ? "CompanySwitchedOn" : "CompanySwitchedOff";
        audit.Add(action, company.Name, company.Id);
        await db.SaveChangesAsync();

        TempData["Message"] = "Saved.";
        return RedirectToPage(new { id });
    }

    private async Task LoadAsync(Company company)
    {
        Id = company.Id;
        Name = company.Name;
        var now = DateTimeOffset.UtcNow;
        Users = [];
        foreach (var u in await db.Users.Where(u => u.CompanyId == company.Id).OrderBy(u => u.FullName).ToListAsync())
        {
            var role = (await users.GetRolesAsync(u)).FirstOrDefault() ?? "";
            Users.Add(new UserRow(u.Id, u.FullName, u.Email ?? "", role, u.IsActive, u.LockoutEnd > now));
        }
    }
}
