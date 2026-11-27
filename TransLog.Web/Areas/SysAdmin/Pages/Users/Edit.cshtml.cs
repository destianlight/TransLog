using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using TransLog.Web.Data;
using TransLog.Web.Security;

namespace TransLog.Web.Areas.SysAdmin.Pages.Users;

public class EditModel(AppDbContext db, UserManager<AppUser> users, AuditLogger audit) : PageModel
{
    [BindProperty]
    public EditUserForm Form { get; set; } = new();

    public int Id { get; private set; }

    public int? CompanyId { get; private set; }

    public string? CompanyName { get; private set; }

    public bool LockedOut { get; private set; }

    /// <summary>True when this login belongs to a driver: the role stays Driver and the details come from the driver's page.</summary>
    public bool IsDriverLogin { get; private set; }

    public IEnumerable<SelectListItem> RoleOptions =>
        Roles.Company.Select(r => new SelectListItem(r, r));

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var user = await users.FindByIdAsync(id.ToString());
        if (user is null)
            return NotFound();

        await LoadAsync(user);
        Form = new EditUserForm
        {
            FullName = user.FullName,
            Email = user.Email ?? "",
            Cell = user.PhoneNumber ?? "",
            IsActive = user.IsActive,
            Role = (await users.GetRolesAsync(user)).FirstOrDefault(),
        };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        var user = await users.FindByIdAsync(id.ToString());
        if (user is null)
            return NotFound();
        await LoadAsync(user);

        if (IsDriverLogin)
        {
            // A driver's name, email, cell and on/off come from the driver's page; the role stays Driver.
            foreach (var key in new[] { "Form.FullName", "Form.Email", "Form.Cell", "Form.Role", "Form.IsActive" })
                ModelState.Remove(key);
            Form.FullName = user.FullName;
            Form.Email = user.Email ?? "";
            Form.Cell = user.PhoneNumber ?? "";
            Form.Role = Roles.Driver;
            Form.IsActive = user.IsActive;
        }
        else if (user.CompanyId is not null && (Form.Role is null || !Roles.Company.Contains(Form.Role)))
            ModelState.AddModelError("Form.Role", "Choose a role.");
        if (user.Id == User.GetUserId() && !Form.IsActive)
            ModelState.AddModelError("Form.IsActive", "You cannot switch yourself off.");
        if (!ModelState.IsValid)
            return Page();

        var changes = new List<string>();
        var email = Form.Email.Trim();
        var cell = TransLog.Web.Fleet.CellNumbers.Normalize(Form.Cell);
        var switchedOff = user.IsActive && !Form.IsActive;

        if (!string.Equals(user.Email ?? "", email, StringComparison.OrdinalIgnoreCase))
            changes.Add($"email {user.Email} -> {email}");
        if (user.PhoneNumber != cell)
            changes.Add($"cell {user.PhoneNumber} -> {cell}");
        if (user.IsActive != Form.IsActive)
            changes.Add(Form.IsActive ? "switched on" : "switched off");

        user.FullName = Form.FullName.Trim();
        Logins.Apply(user, email, cell);
        user.IsActive = Form.IsActive;

        var result = await users.UpdateAsync(user);
        if (!AddErrors(result))
            return Page();

        if (user.CompanyId is not null && !IsDriverLogin)
        {
            var current = await users.GetRolesAsync(user);
            if (!current.Contains(Form.Role!))
            {
                if (!AddErrors(await users.RemoveFromRolesAsync(user, current)) ||
                    !AddErrors(await users.AddToRoleAsync(user, Form.Role!)))
                    return Page();
                changes.Add($"role -> {Form.Role}");
            }
        }

        if (!string.IsNullOrEmpty(Form.NewPassword))
        {
            var token = await users.GeneratePasswordResetTokenAsync(user);
            if (!AddErrors(await users.ResetPasswordAsync(user, token, Form.NewPassword)))
                return Page();
            changes.Add("password set");
        }

        if (Form.Unlock)
        {
            await users.SetLockoutEndDateAsync(user, null);
            await users.ResetAccessFailedCountAsync(user);
            changes.Add("unlocked");
        }

        if (switchedOff)
            await users.UpdateSecurityStampAsync(user); // logged out within 5 minutes

        audit.Add("UserUpdated", $"{email}: {(changes.Count == 0 ? "details" : string.Join(", ", changes))}", user.CompanyId);
        await db.SaveChangesAsync();

        TempData["Message"] = $"{user.FullName} saved.";
        if (!string.IsNullOrEmpty(Form.NewPassword))
            LoginDetails.Keep(this, user.FullName, CompanyName, Logins.Describe(user), Form.NewPassword);
        return user.CompanyId is int companyId
            ? RedirectToPage("/Companies/Edit", new { id = companyId })
            : RedirectToPage("/Admins");
    }

    private async Task LoadAsync(AppUser user)
    {
        Id = user.Id;
        CompanyId = user.CompanyId;
        LockedOut = user.LockoutEnd > DateTimeOffset.UtcNow;
        IsDriverLogin = await users.IsInRoleAsync(user, Roles.Driver);
        if (user.CompanyId is int companyId)
            CompanyName = (await db.Companies.FindAsync(companyId))?.Name;
    }

    private bool AddErrors(IdentityResult result)
    {
        foreach (var error in result.Errors)
            ModelState.AddModelError(string.Empty, error.Description);
        return result.Succeeded;
    }
}
