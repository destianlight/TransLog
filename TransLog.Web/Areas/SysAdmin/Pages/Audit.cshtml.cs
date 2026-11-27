using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TransLog.Web.Data;

namespace TransLog.Web.Areas.SysAdmin.Pages;

public class AuditModel(AppDbContext db) : PageModel
{
    public record Row(DateTime CreatedAt, string? UserName, string? Company, string Action, string? Details);

    public List<Row> Entries { get; private set; } = [];

    public async Task OnGetAsync()
    {
        Entries = await (
            from a in db.AuditLogs.IgnoreQueryFilters() // system admins see every company
            join c in db.Companies on a.CompanyId equals c.Id into cs
            from c in cs.DefaultIfEmpty()
            orderby a.CreatedAt descending
            select new Row(a.CreatedAt, a.UserName, c == null ? null : c.Name, a.Action, a.Details))
            .Take(200)
            .ToListAsync();
    }
}
