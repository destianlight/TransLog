using System.Globalization;
using Microsoft.EntityFrameworkCore;
using TransLog.Web.Ai;
using TransLog.Web.Data;
using TransLog.Web.Security;

namespace TransLog.Web.Captures;

/// <summary>
/// Part 5b2: what the driver and the office can do to a slip: remove it (soft delete), restore it, and correct the
/// values the AI read. Every change goes to the company activity log; changed values also go to CaptureEdit.
/// The caller has loaded the capture through the tenant filter and checked who may do it.
/// </summary>
public class CaptureActions(AppDbContext db, AuditLogger audit, AiQueue queue, CaptureLive live)
{
    public const int MaxValue = 500;

    /// <summary>A driver may remove his slip only while it is waiting or being checked.</summary>
    public static bool DriverMayRemove(CaptureStatus s) => s is CaptureStatus.Uploading or CaptureStatus.Processing or CaptureStatus.NeedsReview;

    /// <summary>Values can be corrected only while the slip is being checked and its type is known.</summary>
    public static bool ValuesEditable(Capture c) => c.Status == CaptureStatus.NeedsReview && c.CaptureTypeId is not null;

    public async Task RemoveAsync(Capture c, int userId, bool byDriver)
    {
        if (c.Status == CaptureStatus.Removed)
            return;
        c.StatusBeforeRemove = c.Status;
        c.Status = CaptureStatus.Removed;
        c.RemovedAt = DateTime.UtcNow;
        c.RemovedByUserId = userId;
        audit.Add("CaptureRemoved", $"#{c.Id}{(byDriver ? " by the driver" : "")}", c.CompanyId);
        await db.SaveChangesAsync();
        await NotifyAsync(c);
    }

    public async Task RestoreAsync(Capture c)
    {
        if (c.Status != CaptureStatus.Removed)
            return;
        var back = c.StatusBeforeRemove ?? (c.AiReadAt is null ? CaptureStatus.Processing : CaptureStatus.NeedsReview);
        if (back == CaptureStatus.Uploading)
            back = CaptureStatus.Processing;
        if (back == CaptureStatus.Processing && c.AiReadAt is not null)
            back = CaptureStatus.NeedsReview; // read while it was removed
        c.Status = back;
        c.StatusBeforeRemove = null;
        c.RemovedAt = null;
        c.RemovedByUserId = null;
        if (back == CaptureStatus.Processing)
        {
            c.AiNextTryAt = null;
            c.AiLockedUntil = null;
        }
        audit.Add("CaptureRestored", $"#{c.Id}", c.CompanyId);
        await db.SaveChangesAsync();
        if (back == CaptureStatus.Processing)
            queue.Wake();
        await NotifyAsync(c);
    }

    /// <summary>
    /// Saves corrected values (form values by field key; a missing key leaves that field alone). Returns the problems;
    /// nothing is saved when there is one. Numbers are stored with ".", dates as yyyy-MM-dd, yes/no as yes or no.
    /// </summary>
    public async Task<(List<string> Errors, int Changed)> SaveValuesAsync(Capture c, IReadOnlyList<PromptField> fields,
        IReadOnlyDictionary<string, string?> input, int userId, bool byDriver)
    {
        var errors = new List<string>();
        var data = CaptureReader.ReadDataJson(c.DataJson);
        var changes = new List<(PromptField Field, string? Old, string? New)>();

        foreach (var f in fields)
        {
            if (!input.TryGetValue(f.Key, out var raw))
                continue;
            var (value, error) = Clean(f, raw);
            if (error is not null)
            {
                errors.Add(f.Label + ": " + error);
                continue;
            }
            data.TryGetValue(f.Key, out var old);
            if (!SameValue(f, old, value))
                changes.Add((f, old, value));
        }
        if (errors.Count > 0 || changes.Count == 0)
            return (errors, 0);

        var now = DateTime.UtcNow;
        foreach (var (f, old, value) in changes)
        {
            data[f.Key] = value;
            SetCore(c, f, value);
            db.CaptureEdits.Add(new CaptureEdit
            {
                CompanyId = c.CompanyId,
                CaptureId = c.Id,
                FieldKey = f.Key,
                OldValue = Cut(old),
                NewValue = Cut(value),
                UserId = userId,
                ByDriver = byDriver,
                At = now,
            });
        }
        c.DataJson = System.Text.Json.JsonSerializer.Serialize(data);
        audit.Add("CaptureEdited", $"#{c.Id}: {string.Join(", ", changes.Select(x => x.Field.Label))}{(byDriver ? " by the driver" : "")}", c.CompanyId);
        await db.SaveChangesAsync();
        await NotifyAsync(c);
        return (errors, changes.Count);
    }

    /// <summary>The value as it is stored, or an error. Empty means "not on the slip".</summary>
    public static (string? Value, string? Error) Clean(PromptField f, string? raw)
    {
        var text = raw?.Trim();
        if (string.IsNullOrEmpty(text))
            return (null, null);
        switch (f.Type)
        {
            case "number":
            case "money":
                return SlipReader.Number(text) is decimal n && text.Any(char.IsDigit)
                    ? (n.ToString(CultureInfo.InvariantCulture), null)
                    : (null, "type a number, for example 412.5");
            case "date":
                return SlipReader.Date(text) is DateOnly d
                    ? (d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), null)
                    : (null, "type a date, for example 2026-11-28");
            case "yesno":
                return text.ToLowerInvariant() switch
                {
                    "yes" or "y" or "true" => ("yes", null),
                    "no" or "n" or "false" => ("no", null),
                    _ => (null, "choose yes or no"),
                };
            default:
                return text.Length > MaxValue ? (null, $"at most {MaxValue} characters") : (text, null);
        }
    }

    /// <summary>"412.50" and "412.5" are the same number; text is compared exactly.</summary>
    public static bool SameValue(PromptField f, string? old, string? value)
    {
        if (string.IsNullOrWhiteSpace(old) && value is null)
            return true;
        if (old is null || value is null)
            return false;
        return f.Type switch
        {
            "number" or "money" => SlipReader.Number(old) == SlipReader.Number(value),
            "date" => SlipReader.Date(old) == SlipReader.Date(value),
            _ => old.Trim() == value,
        };
    }

    private static void SetCore(Capture c, PromptField f, string? value)
    {
        switch (f.Core)
        {
            case "Amount": c.Amount = SlipReader.Number(value); break;
            case "Quantity": c.Quantity = SlipReader.Number(value); break;
            case "Odometer": c.Odometer = SlipReader.Number(value); break;
            case "ReferenceNo": c.ReferenceNo = value is null ? null : value.Length > 100 ? value[..100] : value; break;
            case "Vendor": c.Vendor = value is null ? null : value.Length > 200 ? value[..200] : value; break;
            case "EventDate": c.EventDate = SlipReader.Date(value); break;
        }
    }

    private static string? Cut(string? s) => s is { Length: > MaxValue } ? s[..MaxValue] : s;

    private async Task NotifyAsync(Capture c)
    {
        var driverUserId = c.DriverId is int d
            ? await db.Drivers.IgnoreQueryFilters().Where(x => x.Id == d && x.CompanyId == c.CompanyId).Select(x => x.UserId).FirstOrDefaultAsync()
            : null;
        await live.ChangedAsync(c.CompanyId, c.Id, c.Status, false, c.CapturedByUserId, driverUserId);
    }
}
