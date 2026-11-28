using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TransLog.Web.Ai;
using TransLog.Web.Data;

namespace TransLog.Web.Areas.SysAdmin.Pages.Prompts;

/// <summary>Part 5a1: the prompts one company gets after layering (the same PromptSet the reading engine uses from 5a2).</summary>
public class EffectiveModel(AppDbContext db) : PageModel
{
    public List<(int Id, string Name)> Companies { get; private set; } = [];

    public int? CompanyId { get; private set; }

    public string CompanyName { get; private set; } = "";

    public PromptSet? Set { get; private set; }

    public async Task<IActionResult> OnGetAsync(int? companyId)
    {
        Companies = (await db.Companies.AsNoTracking().OrderBy(c => c.Name).Select(c => new { c.Id, c.Name }).ToListAsync())
            .Select(c => (c.Id, c.Name)).ToList();
        if (companyId is not int id)
            return Page();

        var company = await db.Companies.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id);
        if (company is null)
            return NotFound();

        CompanyId = id;
        CompanyName = company.Name;
        Set = await PromptSet.ForCompanyAsync(db, id);
        return Page();
    }

    public static string ScopeLabel(CaptureType e) => e.Scope == PromptScope.Global ? "Global" : "For selected companies";
}
