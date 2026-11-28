using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TransLog.Web.Ai;
using TransLog.Web.Data;

namespace TransLog.Web.Areas.SysAdmin.Pages.Prompts;

/// <summary>
/// Part 5a1: edit a prompt entry, or create one: a new capture type (no id), or a version of an existing entry
/// for selected companies (?from=id, same key and kind).
/// </summary>
public class EditModel(AppDbContext db, PromptService prompts) : PageModel
{
    private const string NewTypePrompt = """
        Step 2: read this document.

        Hints:
        - 

        {fields}
        """;

    [BindProperty]
    public PromptInput Form { get; set; } = new();

    /// <summary>Only typed for a new capture type; fixed afterwards.</summary>
    [BindProperty]
    public string? Key { get; set; }

    public int? Id { get; private set; }

    public CaptureType? Source { get; private set; }

    public PromptKind Kind { get; private set; } = PromptKind.Type;

    public string? FixedKey { get; private set; }

    public bool LockedGlobal { get; private set; }

    public int Version { get; private set; }

    public bool IsGlobalEntry { get; private set; }

    public string? Preview { get; private set; }

    public List<(int Id, string Name, bool IsActive)> Companies { get; private set; } = [];

    public string Title => Id is not null ? Form.Name
        : Source is not null ? $"{Source.Name}: version for selected companies"
        : "New capture type";

    public async Task<IActionResult> OnGetAsync(int? id, int? from)
    {
        if (id is int existingId)
        {
            var entry = await prompts.FindAsync(existingId);
            if (entry is null)
                return NotFound();
            Form = PromptService.InputFrom(entry);
            await LoadAsync(entry, null);
            return Page();
        }

        if (from is int fromId)
        {
            var source = await prompts.FindAsync(fromId);
            if (source is null)
                return NotFound();
            Form = PromptService.InputFrom(source);
            Form.Scope = PromptScope.SelectedCompanies;
            Form.CompanyIds = [];
            Form.IsActive = true;
            await LoadAsync(null, source);
            return Page();
        }

        Form = new PromptInput { Prompt = NewTypePrompt.Replace("\r\n", "\n"), Fields = [new PromptField()] };
        await LoadAsync(null, null);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int? id, int? from)
    {
        CaptureType? entry = null, source = null;
        PromptSaveResult result;
        if (id is int existingId)
        {
            entry = await prompts.FindAsync(existingId);
            if (entry is null)
                return NotFound();
            result = await prompts.UpdateAsync(existingId, Form);
        }
        else
        {
            if (from is int fromId)
            {
                source = await prompts.FindAsync(fromId);
                if (source is null)
                    return NotFound();
            }
            result = await prompts.CreateAsync(source?.Key ?? Key ?? "", Form);
        }

        if (!result.Ok)
        {
            // Only PromptRules' messages: the automatic "field is required" ones would also complain about empty field rows.
            ModelState.Clear();
            foreach (var e in result.Errors)
                ModelState.AddModelError(string.Empty, e.Message); // all shown together at the top of the form
            if (Form.Fields.Count == 0 && (entry?.Kind ?? source?.Kind ?? PromptKind.Type) == PromptKind.Type)
                Form.Fields.Add(new PromptField());
            await LoadAsync(entry, source);
            return Page();
        }

        TempData["Message"] = $"Saved as {PromptSet.VersionLabel(result.Entry!.CurrentVersion)}.";
        return RedirectToPage(new { id = result.Entry.Id, from = (int?)null });
    }

    private async Task LoadAsync(CaptureType? entry, CaptureType? source)
    {
        Companies = (await db.Companies.AsNoTracking().OrderBy(c => c.Name).Select(c => new { c.Id, c.Name, c.IsActive }).ToListAsync())
            .Select(c => (c.Id, c.Name, c.IsActive)).ToList();
        Source = source;
        if (entry is not null)
        {
            Id = entry.Id;
            Kind = entry.Kind;
            FixedKey = entry.Key;
            LockedGlobal = PromptService.IsLockedGlobal(entry);
            Version = entry.CurrentVersion;
            IsGlobalEntry = entry.Scope == PromptScope.Global;
            if (entry.Kind == PromptKind.Type)
                Preview = PromptSet.TypeText(entry);
        }
        else if (source is not null)
        {
            Kind = source.Kind;
            FixedKey = source.Key;
        }
    }
}

/// <summary>One row of the field table (partial _FieldRow).</summary>
public sealed record FieldRow(int Index, PromptField Field);
