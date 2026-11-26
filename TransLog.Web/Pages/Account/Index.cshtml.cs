using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TransLog.Web.Data;

namespace TransLog.Web.Pages.Account;

public class IndexModel(UserManager<AppUser> users, AppDbContext db) : PageModel
{
    public AppUser Me { get; private set; } = null!;

    public string? CompanyName { get; private set; }

    public IList<string> MyRoles { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await users.GetUserAsync(User);
        if (user is null)
            return Challenge();

        Me = user;
        MyRoles = await users.GetRolesAsync(user);
        if (user.CompanyId is int companyId)
            CompanyName = await db.Companies.Where(c => c.Id == companyId).Select(c => c.Name).FirstOrDefaultAsync();
        return Page();
    }
}
