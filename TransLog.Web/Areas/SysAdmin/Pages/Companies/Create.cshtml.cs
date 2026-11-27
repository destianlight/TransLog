using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TransLog.Web.Data;
using TransLog.Web.Security;

namespace TransLog.Web.Areas.SysAdmin.Pages.Companies;

public class CreateModel(AppDbContext db, UserManager<AppUser> users, AuditLogger audit) : PageModel
{
    [BindProperty]
    public CompanyForm Form { get; set; } = new();

    [BindProperty]
    public NewUserForm Owner { get; set; } = new();

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
            return Page();

        var name = Form.Name.Trim();
        if (await db.Companies.AnyAsync(c => c.Name == name))
        {
            ModelState.AddModelError("Form.Name", "A company with this name already exists.");
            return Page();
        }

        var email = Owner.Email.Trim();
        if (await Logins.FindAsync(users, email) is not null)
        {
            ModelState.AddModelError("Owner.Email", "A user with this email already exists.");
            return Page();
        }

        await using var tx = await db.Database.BeginTransactionAsync();

        var company = new Company { IsActive = true, CreatedAt = DateTime.UtcNow };
        Form.CopyTo(company);
        db.Companies.Add(company);
        await db.SaveChangesAsync();

        var owner = new AppUser
        {
            FullName = Owner.FullName.Trim(),
            CompanyId = company.Id,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
        };
        Logins.Apply(owner, email, Owner.Cell);

        var result = await users.CreateAsync(owner, Owner.Password);
        if (result.Succeeded)
            result = await users.AddToRoleAsync(owner, Roles.Owner);
        if (!result.Succeeded)
        {
            await tx.RollbackAsync();
            foreach (var error in result.Errors)
                ModelState.AddModelError(string.Empty, error.Description);
            return Page();
        }

        audit.Add("CompanyCreated", $"{company.Name}; owner {email}", company.Id);
        await db.SaveChangesAsync();
        await tx.CommitAsync();

        TempData["Message"] = $"{company.Name} created. {owner.FullName} can now log in with {Logins.Describe(owner)}.";
        LoginDetails.Keep(this, owner.FullName, company.Name, Logins.Describe(owner), Owner.Password);
        return RedirectToPage("/Companies/Edit", new { id = company.Id });
    }
}
