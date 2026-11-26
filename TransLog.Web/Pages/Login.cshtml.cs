using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TransLog.Web.Data;

namespace TransLog.Web.Pages;

[AllowAnonymous]
public class LoginModel(SignInManager<AppUser> signIn, UserManager<AppUser> users, AppDbContext db) : PageModel
{
    // One message for every failure, so nobody can find out which emails exist.
    private const string Failed = "Login failed. Check your email and password.";

    [BindProperty, Required(ErrorMessage = "Enter your email."), EmailAddress(ErrorMessage = "Enter a valid email.")]
    public string Email { get; set; } = "";

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

        var user = await users.FindByEmailAsync(Email.Trim());
        if (user is null || !user.IsActive || !await CompanyIsActiveAsync(user))
        {
            Error = Failed;
            return Page();
        }

        var result = await signIn.PasswordSignInAsync(user, Password, RememberMe, lockoutOnFailure: true);
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

        if (!string.IsNullOrEmpty(ReturnUrl) && Url.IsLocalUrl(ReturnUrl))
            return LocalRedirect(ReturnUrl);
        return RedirectToPage("/Go");
    }

    private async Task<bool> CompanyIsActiveAsync(AppUser user) =>
        user.CompanyId is null || await db.Companies.AnyAsync(c => c.Id == user.CompanyId && c.IsActive);
}
