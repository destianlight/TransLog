using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TransLog.Web.Ai;
using TransLog.Web.Data;

namespace TransLog.Web.Areas.SysAdmin.Pages.Providers;

/// <summary>Part 5a2a: AI providers with their key status and the price that counts now.</summary>
public class IndexModel(AppDbContext db, AiKeys keys) : PageModel
{
    public record Row(AiProvider Provider, string KeyStatus, AiPrice? Price, int Companies);

    public List<Row> Rows { get; private set; } = [];

    public string GlobalKey { get; private set; } = "";

    public async Task OnGetAsync()
    {
        GlobalKey = await PlatformSettings.GetAsync(db, PlatformSettings.ProviderKey) ?? PlatformSettings.DefaultProvider;
        var now = DateTime.UtcNow;
        var providers = await db.AiProviders.AsNoTracking().OrderBy(p => p.Name).ToListAsync();
        var usedBy = await db.Companies.AsNoTracking().Where(c => c.AiProviderId != null)
            .GroupBy(c => c.AiProviderId!.Value).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count);
        foreach (var p in providers)
            Rows.Add(new Row(p, keys.Describe(p), await AiPricing.FindAsync(db, p.Id, p.Model, now), usedBy.GetValueOrDefault(p.Id)));
    }
}
