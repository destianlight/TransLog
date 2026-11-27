using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TransLog.Web.Data;
using TransLog.Web.Security;

namespace TransLog.Web.Pages;

[AllowAnonymous]
public class LoginModel(SignInManager<AppUser> signIn, UserManager<AppUser> users, AppDbContext db) : PageModel
{
    // One message for every failure, so nobody can find out which emails or cell numbers exist.
    private const string Failed = "Login failed. Check your email or cell number and your password.";

    /// <summary>An email or a cell number (Part 3c).</summary>
    [BindProperty, Required(ErrorMessage = "Enter your email or cell number."), StringLength(256)]
    public string Login { get; set; } = "";

    [BindProperty, Required(ErrorMessage = "Enter your password."), DataType(DataType.Password)]
    public string Password { get; set; } = "";

    [BindProperty]
    public bool RememberMe { get; set; } = true;

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public string? Error { get; private set; }

    public IActionResult OnGet() =>
        User.Identity?.IsAuthenticated == true ? RedirectToPage("/Go") : Page();

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
            return Page();

        var user = await Logins.FindAsync(users, Login);
        if (user is null || !user.IsActive || !await CompanyIsActiveAsync(user))
        {
            Error = Failed;
            return Page();
        }

        var result = await signIn.CheckPasswordSignInAsync(user, Password, lockoutOnFailure: true);
        if (result.IsLockedOut)
        {
            Error = "Too many attempts. Try again in 5 minutes.";
            return Page();
        }
        if (!result.Succeeded)
        {
            Error = Failed;
            return Page();
        }

        // One account = one device: a new security stamp makes every older login cookie invalid at its
        // next check (within a minute, see Program.cs). Written straight to the table so a user check
        // can never block a login. A future system admin "log in as" must NOT do this.
        await NewSecurityStampAsync(user);
        await signIn.SignInAsync(user, isPersistent: RememberMe);

        if (!string.IsNullOrEmpty(ReturnUrl) && Url.IsLocalUrl(ReturnUrl))
            return LocalRedirect(ReturnUrl);
        return RedirectToPage("/Go");
    }

    private async Task NewSecurityStampAsync(AppUser user)
    {
        var stamp = Guid.NewGuid().ToString("N").ToUpperInvariant();
        await db.Users.IgnoreQueryFilters()
            .Where(u => u.Id == user.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.SecurityStamp, stamp));

        // Keep the loaded user in step, without marking it as changed.
        var entry = db.Entry(user).Property(u => u.SecurityStamp);
        entry.CurrentValue = stamp;
        entry.OriginalValue = stamp;
    }

    private async Task<bool> CompanyIsActiveAsync(AppUser user) =>
        user.CompanyId is null || await db.Companies.AnyAsync(c => c.Id == user.CompanyId && c.IsActive);
}
