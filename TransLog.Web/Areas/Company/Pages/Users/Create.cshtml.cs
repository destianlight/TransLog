using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using TransLog.Web.Areas.SysAdmin;
using TransLog.Web.Data;
using TransLog.Web.Security;

namespace TransLog.Web.Areas.CompanyArea.Pages.Users;

/// <summary>The Owner adds a user to their own company: Admin, Capturer or Viewer.</summary>
public class CreateModel(AppDbContext db, UserManager<AppUser> users, AuditLogger audit) : PageModel
{
    [BindProperty]
    public NewUserForm Form { get; set; } = new() { Role = Roles.Capturer };

    public IEnumerable<SelectListItem> RoleOptions =>
        Roles.OwnerCanGive.Select(r => new SelectListItem(r, r));

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (User.GetCompanyId() is not int companyId || await db.Companies.FindAsync(companyId) is not { } company)
            return NotFound();

        if (Form.Role is null || !Roles.OwnerCanGive.Contains(Form.Role))
            ModelState.AddModelError("Form.Role", "Choose a role.");
        if (!ModelState.IsValid)
            return Page();

        var email = Form.Email.Trim();
        var user = new AppUser
        {
            FullName = Form.FullName.Trim(),
            CompanyId = companyId,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
        };
        Logins.Apply(user, email, Form.Cell);

        var result = await users.CreateAsync(user, Form.Password);
        if (result.Succeeded)
            result = await users.AddToRoleAsync(user, Form.Role!);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
                ModelState.AddModelError(string.Empty, error.Description);
            return Page();
        }

        audit.Add("UserCreated", $"{email} as {Form.Role}", companyId);
        await db.SaveChangesAsync();

        TempData["Message"] = $"{user.FullName} added.";
        LoginDetails.Keep(this, user.FullName, company.Name, Logins.Describe(user), Form.Password);
        return RedirectToPage("Index");
    }
}
