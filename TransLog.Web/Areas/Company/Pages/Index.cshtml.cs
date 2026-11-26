using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using TransLog.Web.Data;
using TransLog.Web.Security;

// The folder is Areas/Company, but the namespace is CompanyArea so it does not clash with the Company class.
namespace TransLog.Web.Areas.CompanyArea.Pages;

public class IndexModel(AppDbContext db) : PageModel
{
    public string CompanyName { get; private set; } = "";

    public async Task<IActionResult> OnGetAsync()
    {
        if (User.GetCompanyId() is not int companyId || await db.Companies.FindAsync(companyId) is not { } company)
            return NotFound();

        CompanyName = company.Name;
        return Page();
    }
}
