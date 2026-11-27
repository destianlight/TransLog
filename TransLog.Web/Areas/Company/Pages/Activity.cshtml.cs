using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TransLog.Web.Data;

namespace TransLog.Web.Areas.CompanyArea.Pages;

/// <summary>The company's own audit log.</summary>
public class ActivityModel(AppDbContext db) : PageModel
{
    public List<AuditLog> Entries { get; private set; } = [];

    public async Task OnGetAsync()
    {
        // No "where CompanyId = ..." on purpose: the tenant filter in AppDbContext limits this
        // to the logged-in company. A test checks that another company's entries never show.
        Entries = await db.AuditLogs
            .OrderByDescending(a => a.CreatedAt)
            .Take(100)
            .ToListAsync();
    }
}
