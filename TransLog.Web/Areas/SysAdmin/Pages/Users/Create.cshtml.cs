using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using TransLog.Web.Data;
using TransLog.Web.Security;

namespace TransLog.Web.Areas.SysAdmin.Pages.Users;

/// <summary>Adds a user to a company (?companyId=5), or a TransLog admin when no company is given.</summary>
public class CreateModel(AppDbContext db, UserManager<AppUser> users, AuditLogger audit) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public int? CompanyId { get; set; }

    [BindProperty]
    public NewUserForm Form { get; set; } = new() { Role = Roles.Admin };

    public string? CompanyName { get; private set; }

    public IEnumerable<SelectListItem> RoleOptions =>
        Roles.Company.Select(r => new SelectListItem(r, r));

    public async Task<IActionResult> OnGetAsync() =>
        await LoadAsync() ? Page() : NotFound();

    public async Task<IActionResult> OnPostAsync()
    {
        if (!await LoadAsync())
            return NotFound();

        var role = CompanyId is null ? Roles.SysAdmin : Form.Role;
        if (CompanyId is not null && (role is null || !Roles.Company.Contains(role)))
            ModelState.AddModelError("Form.Role", "Choose a role.");
        if (!ModelState.IsValid)
            return Page();

        var email = Form.Email.Trim();
        var user = new AppUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            FullName = Form.FullName.Trim(),
            CompanyId = CompanyId,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
        };

        var result = await users.CreateAsync(user, Form.Password);
        if (result.Succeeded)
            result = await users.AddToRoleAsync(user, role!);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
                ModelState.AddModelError(string.Empty, error.Description);
            return Page();
        }

        audit.Add("UserCreated", $"{email} as {role}", CompanyId);
        await db.SaveChangesAsync();

        TempData["Message"] = $"{user.FullName} added. They can log in with {email}.";
        return CompanyId is int companyId
            ? RedirectToPage("/Companies/Edit", new { id = companyId })
            : RedirectToPage("/Admins");
    }

    private async Task<bool> LoadAsync()
    {
        if (CompanyId is not int companyId)
            return true;
        CompanyName = (await db.Companies.FindAsync(companyId))?.Name;
        return CompanyName is not null;
    }
}
