using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using TransLog.Web.Areas.SysAdmin;
using TransLog.Web.Data;
using TransLog.Web.Security;

namespace TransLog.Web.Areas.CompanyArea.Pages.Users;

/// <summary>
/// The Owner edits a user of their own company. Users of other companies and Owners
/// (managed by TransLog) answer "not found", even when their id is typed into the address.
/// </summary>
public class EditModel(AppDbContext db, UserManager<AppUser> users, AuditLogger audit) : PageModel
{
    [BindProperty]
    public EditUserForm Form { get; set; } = new();

    public int Id { get; private set; }

    public bool LockedOut { get; private set; }

    /// <summary>Set when this login belongs to a driver: then only the password and unlock are changed here.</summary>
    public int? DriverId { get; private set; }

    public IEnumerable<SelectListItem> RoleOptions =>
        Roles.OwnerCanGive.Select(r => new SelectListItem(r, r));

    public async Task<IActionResult> OnGetAsync(int id)
    {
        if (await FindAsync(id) is not { } found)
            return NotFound();

        var (user, role) = found;
        await LoadAsync(user);
        Form = new EditUserForm
        {
            FullName = user.FullName,
            Email = user.Email ?? "",
            Cell = user.PhoneNumber ?? "",
            IsActive = user.IsActive,
            Role = role,
        };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        if (await FindAsync(id) is not { } found)
            return NotFound();

        var (user, currentRole) = found;
        await LoadAsync(user);

        // A driver's login keeps its details from the driver's page: only the password and unlock count here.
        var isDriver = DriverId is not null;
        if (isDriver)
        {
            foreach (var key in new[] { "Form.FullName", "Form.Email", "Form.Cell", "Form.Role", "Form.IsActive" })
                ModelState.Remove(key);
            Form.FullName = user.FullName;
            Form.Email = user.Email ?? "";
            Form.Cell = user.PhoneNumber ?? "";
            Form.Role = currentRole;
            Form.IsActive = user.IsActive;
        }
        else if (Form.Role is null || !Roles.OwnerCanGive.Contains(Form.Role))
            ModelState.AddModelError("Form.Role", "Choose a role.");
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

        if (!AddErrors(await users.UpdateAsync(user)))
            return Page();

        if (!isDriver && currentRole != Form.Role)
        {
            if (!AddErrors(await users.RemoveFromRolesAsync(user, await users.GetRolesAsync(user))) ||
                !AddErrors(await users.AddToRoleAsync(user, Form.Role!)))
                return Page();
            changes.Add($"role -> {Form.Role}");
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
        {
            var companyName = (await db.Companies.FindAsync(user.CompanyId))?.Name;
            LoginDetails.Keep(this, user.FullName, companyName, Logins.Describe(user), Form.NewPassword);
        }
        return RedirectToPage("Index");
    }

    /// <summary>The user and their role, only when they belong to this company and are not an Owner.</summary>
    private async Task<(AppUser User, string? Role)?> FindAsync(int id)
    {
        var companyId = User.GetCompanyId();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id && u.CompanyId == companyId);
        if (user is null)
            return null;

        var role = (await users.GetRolesAsync(user)).FirstOrDefault();
        return role == Roles.Owner ? null : (user, role);
    }

    private async Task LoadAsync(AppUser user)
    {
        Id = user.Id;
        LockedOut = user.LockoutEnd > DateTimeOffset.UtcNow;
        // The tenant filter keeps this inside the Owner's company.
        DriverId = await db.Drivers.Where(d => d.UserId == user.Id).Select(d => (int?)d.Id).FirstOrDefaultAsync();
    }

    private bool AddErrors(IdentityResult result)
    {
        foreach (var error in result.Errors)
            ModelState.AddModelError(string.Empty, error.Description);
        return result.Succeeded;
    }
}
