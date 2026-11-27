using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using TransLog.Web.Data;
using TransLog.Web.Security;

// The folder is Areas/Company, but the namespace is CompanyArea so it does not clash with the Company class.
namespace TransLog.Web.Areas.CompanyArea.Pages;

/// <summary>Company home page for every company user. Owners also get users, settings and activity.</summary>
public class IndexModel(AppDbContext db) : PageModel
{
    public string CompanyName { get; private set; } = "";

    public bool IsOwner => User.IsInRole(Roles.Owner);

    /// <summary>The Owner and Admins manage drivers and vehicles.</summary>
    public bool IsFleetAdmin => User.IsInRole(Roles.Owner) || User.IsInRole(Roles.Admin);

    /// <summary>The Owner, Admins and Capturers add photos on the office Capture page (Part 4b).</summary>
    public bool CanCapture => User.IsInRole(Roles.Owner) || User.IsInRole(Roles.Admin) || User.IsInRole(Roles.Capturer);

    public async Task<IActionResult> OnGetAsync()
    {
        if (User.GetCompanyId() is not int companyId || await db.Companies.FindAsync(companyId) is not { } company)
            return NotFound();

        CompanyName = company.Name;
        return Page();
    }
}
