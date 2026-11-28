using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TransLog.Web.Data;

namespace TransLog.Web.Areas.SysAdmin.Pages.Prompts;

/// <summary>Part 5a1: every prompt entry in three groups, plus the "Effective for company" picker.</summary>
public class IndexModel(AppDbContext db) : PageModel
{
    public record Row(int Id, PromptKind Kind, string Key, string Name, string Scope, int Version, bool IsActive, DateTime UpdatedAt);

    public List<Row> Rows { get; private set; } = [];

    public List<(int Id, string Name)> Companies { get; private set; } = [];

    public async Task OnGetAsync()
    {
        var companies = await db.Companies.AsNoTracking().OrderBy(c => c.Name).Select(c => new { c.Id, c.Name }).ToListAsync();
        Companies = companies.Select(c => (c.Id, c.Name)).ToList();
        var names = companies.ToDictionary(c => c.Id, c => c.Name);

        var entries = await db.CaptureTypes.AsNoTracking().Include(t => t.Companies).ToListAsync();
        Rows = entries
            .OrderBy(e => e.Kind).ThenBy(e => e.Key).ThenBy(e => e.Scope).ThenBy(e => e.Name)
            .Select(e => new Row(e.Id, e.Kind, e.Key, e.Name,
                e.Scope == PromptScope.Global
                    ? "Global"
                    : string.Join(", ", e.Companies.Select(c => names.GetValueOrDefault(c.CompanyId, "?")).Order()),
                e.CurrentVersion, e.IsActive, e.UpdatedAt))
            .ToList();
    }
}
