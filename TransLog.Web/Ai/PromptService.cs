using System.Globalization;
using Microsoft.EntityFrameworkCore;
using TransLog.Web.Data;
using TransLog.Web.Security;

namespace TransLog.Web.Ai;

/// <summary>The result of a save: the entry when it worked, else the problems.</summary>
public sealed record PromptSaveResult(CaptureType? Entry, List<PromptError> Errors)
{
    public bool Ok => Entry is not null && Errors.Count == 0;
}

/// <summary>
/// Creates, edits and restores prompt entries (Part 5a1). Every save checks the rules, writes the next
/// <see cref="CaptureTypeVersion"/> (a full copy) and an audit entry. Nothing is ever overwritten or deleted.
/// </summary>
public class PromptService(AppDbContext db, AuditLogger audit, IHttpContextAccessor http)
{
    private int? UserId => http.HttpContext?.User.GetUserId();

    public Task<CaptureType?> FindAsync(int id) =>
        db.CaptureTypes.Include(t => t.Companies).FirstOrDefaultAsync(t => t.Id == id);

    /// <summary>The global system and detection entries must stay Global and on.</summary>
    public static bool IsLockedGlobal(CaptureType entry) => entry.Kind != PromptKind.Type && entry.Scope == PromptScope.Global;

    public async Task<PromptSaveResult> CreateAsync(string key, PromptInput input)
    {
        key = (key ?? "").Trim().ToLowerInvariant();
        PromptRules.Tidy(input);
        var kind = PromptRules.KindForKey(key);
        var errors = PromptRules.Check(kind, key, input, lockedGlobal: false);
        if (errors.Count == 0)
            errors.AddRange(await CheckWithDatabaseAsync(null, key, input));
        if (errors.Count > 0)
            return new(null, errors);

        var entry = new CaptureType { Kind = kind, Key = key, CurrentVersion = 0 };
        db.CaptureTypes.Add(entry);
        Apply(entry, input);
        await db.SaveChangesAsync(); // gives the entry its id for the version row
        AddVersion(entry, input);
        audit.Add("PromptCreated", Describe(entry, input));
        await db.SaveChangesAsync();
        return new(entry, []);
    }

    public async Task<PromptSaveResult> UpdateAsync(int id, PromptInput input, string action = "PromptUpdated", string? note = null)
    {
        var entry = await FindAsync(id);
        if (entry is null)
            return new(null, [new("", "Not found.")]);

        PromptRules.Tidy(input);
        var errors = PromptRules.Check(entry.Kind, entry.Key, input, IsLockedGlobal(entry));
        if (errors.Count == 0)
            errors.AddRange(await CheckWithDatabaseAsync(entry.Id, entry.Key, input));
        if (errors.Count > 0)
            return new(null, errors);

        Apply(entry, input);
        AddVersion(entry, input);
        audit.Add(action, Describe(entry, input) + (note is null ? "" : " · " + note));
        await db.SaveChangesAsync();
        return new(entry, []);
    }

    /// <summary>Saves an old version's content as a new version, through the same checks as a normal save.</summary>
    public async Task<PromptSaveResult> RestoreAsync(int id, int version)
    {
        var old = await db.CaptureTypeVersions.AsNoTracking()
            .FirstOrDefaultAsync(v => v.CaptureTypeId == id && v.Version == version);
        if (old is null)
            return new(null, [new("", "That version does not exist.")]);

        return await UpdateAsync(id, InputFrom(old), "PromptRestored", "content of " + PromptSet.VersionLabel(version));
    }

    public static PromptInput InputFrom(CaptureType e) => new()
    {
        Name = e.Name,
        Description = e.Description,
        Prompt = e.Prompt,
        Scope = e.Scope,
        IsActive = e.IsActive,
        CompanyIds = e.Companies.Select(c => c.CompanyId).Order().ToList(),
        Fields = PromptRules.FieldsFromJson(e.FieldsJson),
    };

    public static PromptInput InputFrom(CaptureTypeVersion v) => new()
    {
        Name = v.Name,
        Description = v.Description,
        Prompt = v.Prompt,
        Scope = v.Scope,
        IsActive = v.IsActive,
        CompanyIds = ParseIds(v.CompanyIds),
        Fields = PromptRules.FieldsFromJson(v.FieldsJson),
    };

    public static List<int> ParseIds(string? text) =>
        (text ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0)
            .Where(n => n > 0)
            .ToList();

    /// <summary>
    /// Only one Global entry per key; a company may be in only one active Selected-companies entry per key
    /// (so it never gets two fuel types); ticked companies must exist.
    /// </summary>
    private async Task<List<PromptError>> CheckWithDatabaseAsync(int? id, string key, PromptInput input)
    {
        var errors = new List<PromptError>();
        var others = await db.CaptureTypes.AsNoTracking().Include(t => t.Companies)
            .Where(t => t.Key == key && t.Id != (id ?? 0))
            .ToListAsync();

        if (others.Any(o => o.Kind != PromptRules.KindForKey(key)))
            errors.Add(new("Key", "This key is used by an entry of another kind."));

        if (input.Scope == PromptScope.Global && others.Any(o => o.Scope == PromptScope.Global))
            errors.Add(new("Scope", $"There is already a Global entry with the key \"{key}\". Edit that one, or choose Selected companies."));

        if (input.Scope == PromptScope.SelectedCompanies && input.CompanyIds.Count > 0)
        {
            var names = await db.Companies.AsNoTracking()
                .Where(c => input.CompanyIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.Name);
            if (names.Count != input.CompanyIds.Count)
                errors.Add(new("CompanyIds", "A ticked company does not exist."));

            if (input.IsActive)
            {
                foreach (var other in others.Where(o => o.IsActive && o.Scope == PromptScope.SelectedCompanies))
                {
                    foreach (var companyId in other.Companies.Select(c => c.CompanyId).Where(input.CompanyIds.Contains))
                    {
                        var name = names.GetValueOrDefault(companyId, "A company");
                        errors.Add(new("CompanyIds",
                            $"{name} already has an active \"{key}\" entry for selected companies ({other.Name}). Untick it there first, or switch that entry off."));
                    }
                }
            }
        }
        return errors;
    }

    private void Apply(CaptureType entry, PromptInput input)
    {
        entry.Name = input.Name;
        entry.Description = input.Description;
        entry.Prompt = input.Prompt;
        entry.FieldsJson = PromptRules.FieldsToJson(input.Fields);
        entry.Scope = input.Scope;
        entry.IsActive = input.IsActive;
        entry.UpdatedAt = DateTime.UtcNow;
        entry.UpdatedByUserId = UserId;

        // The join rows are removed one by one (no cascading deletes in TransLog).
        var keep = input.CompanyIds.ToHashSet();
        foreach (var link in entry.Companies.Where(c => !keep.Contains(c.CompanyId)).ToList())
        {
            db.CaptureTypeCompanies.Remove(link); // marked deleted first, so cutting it loose below is allowed
            entry.Companies.Remove(link);
        }
        foreach (var companyId in keep.Where(c => entry.Companies.All(l => l.CompanyId != c)))
            entry.Companies.Add(new CaptureTypeCompany { CompanyId = companyId });
    }

    private void AddVersion(CaptureType entry, PromptInput input)
    {
        entry.CurrentVersion++;
        db.CaptureTypeVersions.Add(new CaptureTypeVersion
        {
            CaptureTypeId = entry.Id,
            Version = entry.CurrentVersion,
            Name = entry.Name,
            Description = entry.Description,
            FieldsJson = entry.FieldsJson,
            Prompt = entry.Prompt,
            Scope = entry.Scope,
            CompanyIds = string.Join(',', input.CompanyIds.Select(i => i.ToString(CultureInfo.InvariantCulture))),
            IsActive = entry.IsActive,
            CreatedAt = entry.UpdatedAt,
            CreatedByUserId = entry.UpdatedByUserId,
        });
    }

    private static string Describe(CaptureType entry, PromptInput input) =>
        $"{entry.Key} ({entry.Name}) {PromptSet.VersionLabel(entry.CurrentVersion)}, "
        + (input.Scope == PromptScope.Global ? "Global" : $"{input.CompanyIds.Count} selected companies")
        + (input.IsActive ? "" : ", off");
}
