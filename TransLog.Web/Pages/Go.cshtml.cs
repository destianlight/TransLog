using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using TransLog.Web.Security;

namespace TransLog.Web.Pages;

/// <summary>After login: sends each user to the right area.</summary>
public class GoModel : PageModel
{
    public IActionResult OnGet()
    {
        if (User.IsInRole(Roles.SysAdmin))
            return RedirectToPage("/Index", new { area = "SysAdmin" });
        if (User.IsInRole(Roles.Driver) && User.GetCompanyId() is not null)
            return RedirectToPage("/Index", new { area = "Driver" });
        if (User.GetCompanyId() is not null)
            return RedirectToPage("/Index", new { area = "Company" });
        return RedirectToPage("/Account/Index", new { area = "" });
    }
}
