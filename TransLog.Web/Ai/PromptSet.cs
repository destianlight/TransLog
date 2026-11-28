using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TransLog.Web.Data;

namespace TransLog.Web.Ai;

/// <summary>
/// The prompts that count for one company (Part 5a1; used by the reading engine from 5a2).
/// Layering per key: an active entry for selected companies that includes the company replaces the global entry;
/// a key that only exists for selected companies is an extra type for them; switched-off entries are left out.
/// (Part 7 adds the company's own entries as the first layer.)
/// </summary>
public sealed class PromptSet
{
    public PromptSet(CaptureType? systemPrompt, CaptureType? detectPrompt, IReadOnlyList<CaptureType> types)
    {
        SystemPrompt = systemPrompt;
        DetectPrompt = detectPrompt;
        Types = types;
    }

    /// <summary>The shared system prompt entry (not named "System": that would hide the System namespace).</summary>
    public CaptureType? SystemPrompt { get; }

    public CaptureType? DetectPrompt { get; }

    /// <summary>Active capture types for the company, ordered by name.</summary>
    public IReadOnlyList<CaptureType> Types { get; }

    /// <summary>Loads every entry with its companies and resolves it for one company.</summary>
    public static async Task<PromptSet> ForCompanyAsync(AppDbContext db, int companyId)
    {
        var entries = await db.CaptureTypes.AsNoTracking().Include(t => t.Companies)
            .Where(t => t.IsActive)
            .ToListAsync();
        return Resolve(entries, companyId);
    }

    /// <summary>The layering rule on entries already loaded (with their Companies).</summary>
    public static PromptSet Resolve(IEnumerable<CaptureType> entries, int companyId)
    {
        var chosen = new Dictionary<string, CaptureType>(StringComparer.Ordinal);
        foreach (var group in entries.Where(e => e.IsActive).GroupBy(e => e.Key, StringComparer.Ordinal))
        {
            var mine = group.Where(e => e.Scope == PromptScope.SelectedCompanies && e.Companies.Any(c => c.CompanyId == companyId))
                .OrderByDescending(e => e.UpdatedAt)
                .FirstOrDefault();
            var global = group.FirstOrDefault(e => e.Scope == PromptScope.Global);
            if ((mine ?? global) is { } pick)
                chosen[group.Key] = pick;
        }

        chosen.TryGetValue(PromptRules.SystemKey, out var system);
        chosen.TryGetValue(PromptRules.DetectKey, out var detect);
        var types = chosen.Values.Where(e => e.Kind == PromptKind.Type)
            .OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        return new PromptSet(system?.Kind == PromptKind.System ? system : null, detect?.Kind == PromptKind.Detect ? detect : null, types);
    }

    /// <summary>The json example step 1 is asked for when its prompt has none (DeepSeek's json mode needs the word and an example).</summary>
    public const string DetectExample = "Answer in json like this example:\n{\"type\": \"fuel\", \"confidence\": \"high\", \"description\": \"Diesel slip from Engen Vryburg, 412 litres\"}";

    /// <summary>The shared system prompt as sent: a json rule is added when the text has none.</summary>
    public string SystemText()
    {
        if (SystemPrompt is null)
            return "";
        var text = SystemPrompt.Prompt;
        return text.Contains("json", StringComparison.OrdinalIgnoreCase)
            ? text
            : text + "\n\nAnswer with one json object only, in exactly the shape of the example you are given.";
    }

    /// <summary>
    /// The step 1 prompt as sent: {types} becomes one line per type ("- key: description") plus "other".
    /// Without {types} the list is added at the end; without the word json the example answer is added too.
    /// </summary>
    public string DetectText()
    {
        if (DetectPrompt is null)
            return "";
        var text = DetectPrompt.Prompt;
        text = text.Contains(PromptRules.TypesPlaceholder, StringComparison.Ordinal)
            ? text.Replace(PromptRules.TypesPlaceholder, TypesList(), StringComparison.Ordinal)
            : text + "\n\nThe document types (key: when to choose it):\n" + TypesList() + "\n\nChoose the one key that fits best.";
        return text.Contains("json", StringComparison.OrdinalIgnoreCase) ? text : text + "\n\n" + DetectExample;
    }

    public string TypesList()
    {
        var sb = new StringBuilder();
        foreach (var t in Types)
            sb.Append("- ").Append(t.Key).Append(": ").Append(OneLine(t.Description ?? t.Name)).Append('\n');
        sb.Append("- ").Append(PromptRules.OtherKey).Append(": anything else, or no document in the photo");
        return sb.ToString();
    }

    /// <summary>A type's step 2 prompt as sent: {fields} becomes the field list and an example answer in json (added at the end when missing).</summary>
    public static string TypeText(CaptureType type)
    {
        var fields = FieldsText(PromptRules.FieldsFromJson(type.FieldsJson));
        return type.Prompt.Contains(PromptRules.FieldsPlaceholder, StringComparison.Ordinal)
            ? type.Prompt.Replace(PromptRules.FieldsPlaceholder, fields, StringComparison.Ordinal)
            : type.Prompt.TrimEnd() + "\n\n" + fields;
    }

    public static string FieldsText(List<PromptField> fields)
    {
        var sb = new StringBuilder("Fields to read:\n");
        foreach (var f in fields)
        {
            sb.Append("- ").Append(f.Key).Append(" (").Append(TypeHint(f.Type));
            if (f.Unit is not null)
                sb.Append(", ").Append(f.Unit);
            sb.Append("): ").Append(f.Label);
            if (f.Required)
                sb.Append(". Usually printed; look carefully before giving null");
            sb.Append('\n');
        }

        var example = new Dictionary<string, object>();
        foreach (var f in fields)
            example[f.Key] = new Dictionary<string, object> { ["value"] = ExampleValue(f.Type), ["confidence"] = "high" };
        sb.Append("\nAnswer in json exactly in this shape, with every field listed (value null when not printed):\n");
        sb.Append(JsonSerializer.Serialize(new Dictionary<string, object> { ["fields"] = example }));
        return sb.ToString();
    }

    private static string TypeHint(string type) => type switch
    {
        "number" => "number",
        "money" => "amount in rand, number",
        "date" => "date YYYY-MM-DD",
        "yesno" => "yes or no",
        _ => "text as printed",
    };

    private static object ExampleValue(string type) => type switch
    {
        "number" => 123.4m,
        "money" => 1234.56m,
        "date" => "2026-01-31",
        "yesno" => "yes",
        _ => "as printed",
    };

    private static string OneLine(string text) =>
        string.Join(' ', text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    /// <summary>"v3" for lists.</summary>
    public static string VersionLabel(int version) => "v" + version.ToString(CultureInfo.InvariantCulture);
}
