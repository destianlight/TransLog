using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.RazorPages;
using TransLog.Web.Data;
using TransLog.Web.Security;

namespace TransLog.Web.Areas.SysAdmin.Pages;

public class AdminsModel(UserManager<AppUser> users) : PageModel
{
    public List<UserRow> Admins { get; private set; } = [];

    public async Task OnGetAsync()
    {
        var now = DateTimeOffset.UtcNow;
        Admins = (await users.GetUsersInRoleAsync(Roles.SysAdmin))
            .OrderBy(u => u.FullName)
            .Select(u => new UserRow(u.Id, u.FullName, u.Email ?? "", Roles.SysAdmin, u.IsActive, u.LockoutEnd > now))
            .ToList();
    }
}
