using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TransLog.Web.Ai;
using TransLog.Web.Captures;
using TransLog.Web.Data;
using TransLog.Web.Security;

// The folder is Areas/Driver, but the namespace is DriverArea so it does not clash with the Driver class.
namespace TransLog.Web.Areas.DriverArea.Pages;

/// <summary>
/// Part 5b2: one slip for the driver, opened from My slips: the photo large, what the AI read, and while the slip is
/// being checked (and the company allows it) a simple form to correct the values. Remove while waiting or being checked.
/// Only the driver's own slips; others are not found. The office review page is separate (Part 6).
/// </summary>
public class SlipModel(AppDbContext db, CaptureActions actions) : PageModel
{
    public sealed record FieldView(PromptField Field, string? Value, string? AiValue, bool ChangedByDriver, bool ChangedByOffice);

    public Capture Slip { get; private set; } = null!;

    public int? ImageId { get; private set; }

    public string? TypeName { get; private set; }

    public string? Vehicle { get; private set; }

    public List<FieldView> Fields { get; private set; } = [];

    public bool CanEdit { get; private set; }

    public bool EditsLocked { get; private set; }

    public bool CanRemove { get; private set; }

    public List<string> Errors { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(int id)
    {
        if (!await LoadAsync(id))
            return NotFound();
        return Page();
    }

    public async Task<IActionResult> OnPostSaveAsync(int id)
    {
        if (!await LoadAsync(id))
            return NotFound();
        if (!CanEdit)
        {
            Errors.Add(EditsLocked ? "Your company does not let drivers correct values." : "This slip can no longer be changed.");
            return Page();
        }

        var input = new Dictionary<string, string?>();
        foreach (var f in Fields)
        {
            if (Request.Form.TryGetValue("v." + f.Field.Key, out var posted))
                input[f.Field.Key] = posted.ToString();
        }
        var (errors, changed) = await actions.SaveValuesAsync(Slip, Fields.Select(f => f.Field).ToList(), input, User.GetUserId()!.Value, byDriver: true);
        if (errors.Count > 0)
        {
            Errors = errors;
            // Show what was typed, so it can be fixed.
            Fields = Fields.Select(f => f with { Value = input.TryGetValue(f.Field.Key, out var v) ? v : f.Value }).ToList();
            return Page();
        }
        TempData["Message"] = changed == 0 ? "Nothing changed." : changed == 1 ? "1 value saved." : changed + " values saved.";
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostRemoveAsync(int id)
    {
        if (!await LoadAsync(id))
            return NotFound();
        if (!CanRemove)
        {
            Errors.Add("This slip can no longer be removed. Ask the office.");
            return Page();
        }
        await actions.RemoveAsync(Slip, User.GetUserId()!.Value, byDriver: true);
        TempData["Message"] = "Slip removed.";
        return RedirectToPage("/Slips");
    }

    private async Task<bool> LoadAsync(int id)
    {
        var userId = User.GetUserId();
        // The tenant filter keeps it inside the driver's company; only slips he sent or that are his.
        var slip = await db.Captures
            .Include(c => c.CaptureType)
            .Include(c => c.Vehicle)
            .Include(c => c.Images)
            .Where(c => c.Id == id && (c.CapturedByUserId == userId || c.Driver!.UserId == userId))
            .FirstOrDefaultAsync();
        if (slip is null)
            return false;

        Slip = slip;
        ImageId = slip.Images.OrderBy(i => i.SortOrder).Select(i => (int?)i.Id).FirstOrDefault();
        TypeName = slip.CaptureType?.Name;
        Vehicle = slip.Vehicle?.RegistrationNo;
        EditsLocked = await db.Companies.Where(c => c.Id == slip.CompanyId).Select(c => c.DriverEditsLocked).FirstOrDefaultAsync();
        CanEdit = !EditsLocked && CaptureActions.ValuesEditable(slip);
        CanRemove = CaptureActions.DriverMayRemove(slip.Status);

        var data = CaptureReader.ReadDataJson(slip.DataJson);
        var edits = await db.CaptureEdits.AsNoTracking().Where(e => e.CaptureId == id).OrderBy(e => e.At).ThenBy(e => e.Id).ToListAsync();
        Fields = PromptRules.FieldsFromJson(slip.CaptureType?.FieldsJson)
            .Select(f =>
            {
                var mine = edits.Where(e => e.FieldKey == f.Key).ToList();
                return new FieldView(f, data.TryGetValue(f.Key, out var v) ? v : null,
                    mine.Count > 0 ? mine[0].OldValue : null,
                    mine.Any(e => e.ByDriver), mine.Any(e => !e.ByDriver));
            })
            .ToList();
        return true;
    }
}
