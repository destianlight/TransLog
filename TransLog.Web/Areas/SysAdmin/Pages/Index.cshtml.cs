using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TransLog.Web.Data;

namespace TransLog.Web.Areas.SysAdmin.Pages;

public class IndexModel(AppDbContext db) : PageModel
{
    public record Row(int Id, string Name, bool IsActive, int Users, DateTime CreatedAt);

    public List<Row> Companies { get; private set; } = [];

    public async Task OnGetAsync()
    {
        Companies = await db.Companies
            .OrderBy(c => c.Name)
            .Select(c => new Row(c.Id, c.Name, c.IsActive, c.Users.Count, c.CreatedAt))
            .ToListAsync();
    }
}
