using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TransLog.Web.Ai;
using TransLog.Web.Data;

namespace TransLog.Web.Areas.SysAdmin.Pages.Prompts;

/// <summary>Part 5a1: every version of one entry, newest first; Restore saves an old one as a new version.</summary>
public class HistoryModel(AppDbContext db, PromptService prompts) : PageModel
{
    public record VersionRow(CaptureTypeVersion Version, string By, string Companies, List<PromptField> Fields);

    public CaptureType Entry { get; private set; } = new();

    public List<VersionRow> Versions { get; private set; } = [];

    public List<string> Errors { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var entry = await prompts.FindAsync(id);
        if (entry is null)
            return NotFound();
        await LoadAsync(entry);
        return Page();
    }

    public async Task<IActionResult> OnPostRestoreAsync(int id, int version)
    {
        var entry = await prompts.FindAsync(id);
        if (entry is null)
            return NotFound();

        var result = await prompts.RestoreAsync(id, version);
        if (!result.Ok)
        {
            Errors = result.Errors.Select(e => e.Message).ToList();
            await LoadAsync(entry);
            return Page();
        }

        TempData["Message"] = $"{PromptSet.VersionLabel(version)} restored as {PromptSet.VersionLabel(result.Entry!.CurrentVersion)}.";
        return RedirectToPage(new { id });
    }

    private async Task LoadAsync(CaptureType entry)
    {
        Entry = entry;
        var versions = await db.CaptureTypeVersions.AsNoTracking()
            .Where(v => v.CaptureTypeId == entry.Id)
            .OrderByDescending(v => v.Version)
            .ToListAsync();
        var userIds = versions.Where(v => v.CreatedByUserId != null).Select(v => v.CreatedByUserId!.Value).Distinct().ToList();
        var users = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName);
        var companies = await db.Companies.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Name);

        Versions = versions.Select(v => new VersionRow(
                v,
                v.CreatedByUserId is int u ? users.GetValueOrDefault(u, "?") : "TransLog (built in)",
                v.Scope == PromptScope.Global
                    ? "Global"
                    : string.Join(", ", PromptService.ParseIds(v.CompanyIds).Select(c => companies.GetValueOrDefault(c, "?")).Order()),
                PromptRules.FieldsFromJson(v.FieldsJson)))
            .ToList();
    }
}
