using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TransLog.Web.Areas.SysAdmin;
using TransLog.Web.Data;
using TransLog.Web.Security;

namespace TransLog.Web.Areas.CompanyArea.Pages.Users;

/// <summary>The Owner's list of the company's users.</summary>
public class IndexModel(AppDbContext db, UserManager<AppUser> users) : PageModel
{
    public List<UserRow> Users { get; private set; } = [];

    public async Task OnGetAsync()
    {
        // AppUser has no tenant filter (login must find any user), so always filter by company here.
        var companyId = User.GetCompanyId();
        var now = DateTimeOffset.UtcNow;
        foreach (var u in await db.Users.Where(u => u.CompanyId == companyId).OrderBy(u => u.FullName).ToListAsync())
        {
            var role = (await users.GetRolesAsync(u)).FirstOrDefault() ?? "";
            Users.Add(new UserRow(u.Id, u.FullName, u.Email ?? "", role, u.IsActive, u.LockoutEnd > now));
        }
    }
}
