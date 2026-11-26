using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using TransLog.Web.Data;
using TransLog.Web.Security;

namespace TransLog.Web.Pages.Account;

public class ChangePasswordModel(
    UserManager<AppUser> users, SignInManager<AppUser> signIn, AuditLogger audit, AppDbContext db) : PageModel
{
    [BindProperty, Required(ErrorMessage = "Enter your current password."), DataType(DataType.Password)]
    public string CurrentPassword { get; set; } = "";

    [BindProperty, Required(ErrorMessage = "Enter a new password."), DataType(DataType.Password)]
    [StringLength(100, MinimumLength = 6, ErrorMessage = "Use at least 6 characters.")]
    public string NewPassword { get; set; } = "";

    [BindProperty, DataType(DataType.Password)]
    [Compare(nameof(NewPassword), ErrorMessage = "The new passwords do not match.")]
    public string ConfirmPassword { get; set; } = "";

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
            return Page();

        var user = await users.GetUserAsync(User);
        if (user is null)
            return Challenge();

        var result = await users.ChangePasswordAsync(user, CurrentPassword, NewPassword);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
                ModelState.AddModelError(string.Empty, error.Description);
            return Page();
        }

        await signIn.RefreshSignInAsync(user);
        audit.Add("PasswordChanged", null, user.CompanyId);
        await db.SaveChangesAsync();

        TempData["Message"] = "Your password has been changed.";
        return RedirectToPage("/Account/Index");
    }
}
