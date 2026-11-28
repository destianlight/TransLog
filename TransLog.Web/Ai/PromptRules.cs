using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using TransLog.Web.Data;

namespace TransLog.Web.Ai;

/// <summary>One field a capture type reads (stored in <see cref="CaptureType.FieldsJson"/>).</summary>
public sealed class PromptField
{
    public string Key { get; set; } = "";

    public string Label { get; set; } = "";

    /// <summary>One of <see cref="PromptRules.FieldTypes"/>: text, number, money, date, yesno.</summary>
    public string Type { get; set; } = "text";

    public string? Unit { get; set; }

    public bool Required { get; set; }

    /// <summary>The real column on Capture this field fills (one of <see cref="PromptRules.CoreColumns"/>), or null.</summary>
    public string? Core { get; set; }
}

/// <summary>What a SysAdmin typed on the edit page (or an old version being restored).</summary>
public sealed class PromptInput
{
    public string Name { get; set; } = "";

    public string? Description { get; set; }

    public string Prompt { get; set; } = "";

    public PromptScope Scope { get; set; }

    public bool IsActive { get; set; } = true;

    public List<int> CompanyIds { get; set; } = [];

    public List<PromptField> Fields { get; set; } = [];
}

/// <summary>A problem with a save. Field is the form name it belongs to ("Key", "Prompt", "Fields", …) or "" for the whole form.</summary>
public sealed record PromptError(string Field, string Message);

/// <summary>The checks that need no database (Part 5a1). The database checks are in <see cref="PromptService"/>.</summary>
public static partial class PromptRules
{
    public const string SystemKey = "system";
    public const string DetectKey = "detect";
    public const string OtherKey = "other";
    public const string TypesPlaceholder = "{types}";
    public const string FieldsPlaceholder = "{fields}";

    public static readonly string[] FieldTypes = ["text", "number", "money", "date", "yesno"];

    public static readonly Dictionary<string, string> FieldTypeNames = new()
    {
        ["text"] = "Text", ["number"] = "Number", ["money"] = "Money (R)", ["date"] = "Date", ["yesno"] = "Yes / no",
    };

    /// <summary>Core columns on Capture (filled in 5a2) and the field types that may fill each one.</summary>
    public static readonly Dictionary<string, string[]> CoreColumns = new()
    {
        ["Amount"] = ["money", "number"],
        ["Quantity"] = ["number"],
        ["Odometer"] = ["number"],
        ["ReferenceNo"] = ["text"],
        ["Vendor"] = ["text"],
        ["EventDate"] = ["date"],
    };

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string FieldsToJson(IEnumerable<PromptField> fields) => JsonSerializer.Serialize(fields.ToList(), Json);

    public static List<PromptField> FieldsFromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];
        try
        {
            return JsonSerializer.Deserialize<List<PromptField>>(json, Json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public static bool IsValidKey(string? key) => key is not null && KeyRegex().IsMatch(key);

    public static bool IsValidFieldKey(string? key) => key is not null && FieldKeyRegex().IsMatch(key);

    /// <summary>The kind an entry key belongs to: "system" and "detect" are fixed; every other key is a capture type.</summary>
    public static PromptKind KindForKey(string key) => key switch
    {
        SystemKey => PromptKind.System,
        DetectKey => PromptKind.Detect,
        _ => PromptKind.Type,
    };

    /// <summary>Trims the input, drops empty field rows and keeps the company list only for Selected companies.</summary>
    public static void Tidy(PromptInput input)
    {
        input.Name = (input.Name ?? "").Trim();
        input.Description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim();
        input.Prompt = (input.Prompt ?? "").Trim();
        input.CompanyIds = input.Scope == PromptScope.SelectedCompanies ? input.CompanyIds.Distinct().Order().ToList() : [];
        input.Fields = input.Fields
            .Where(f => !string.IsNullOrWhiteSpace(f.Key) || !string.IsNullOrWhiteSpace(f.Label))
            .Select(f => new PromptField
            {
                Key = (f.Key ?? "").Trim().ToLowerInvariant(),
                Label = (f.Label ?? "").Trim(),
                Type = string.IsNullOrWhiteSpace(f.Type) ? "text" : f.Type.Trim().ToLowerInvariant(),
                Unit = string.IsNullOrWhiteSpace(f.Unit) ? null : f.Unit.Trim(),
                Required = f.Required,
                Core = string.IsNullOrWhiteSpace(f.Core) ? null : f.Core.Trim(),
            })
            .ToList();

        // A field without a key gets one made from its label ("Load number" → load_number), kept unique.
        var used = input.Fields.Where(f => f.Key.Length > 0).Select(f => f.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var f in input.Fields.Where(f => f.Key.Length == 0))
        {
            var key = MakeKey(f.Label, 1);
            var n = 2;
            while (!used.Add(key))
                key = Fit(MakeKey(f.Label, 1), "_" + (n++).ToString(System.Globalization.CultureInfo.InvariantCulture));
            f.Key = key;
        }
    }

    /// <summary>
    /// A key from a name or label: lower case letters, digits and _ ("Load / proof of delivery" → load_proof_of_delivery).
    /// Starts with a letter (an "x_" is put in front otherwise); at least <paramref name="minLength"/> + 1 characters; at most 40.
    /// </summary>
    public static string MakeKey(string? text, int minLength = 1)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var ch in (text ?? "").Trim().ToLowerInvariant())
        {
            if (ch is >= 'a' and <= 'z' or >= '0' and <= '9')
                sb.Append(ch);
            else if (sb.Length > 0 && sb[^1] != '_')
                sb.Append('_');
        }
        var key = sb.ToString().Trim('_');
        if (key.Length == 0)
            key = "field";
        if (key[0] is < 'a' or > 'z')
            key = "x_" + key;
        if (key.Length <= minLength)
            key += "_1";
        return key.Length > 40 ? key[..40].TrimEnd('_') : key;
    }

    private static string Fit(string key, string suffix) =>
        (key.Length + suffix.Length > 40 ? key[..(40 - suffix.Length)] : key) + suffix;

    /// <summary>
    /// Checks one entry. <paramref name="lockedGlobal"/> is true for the existing global system and detect entries:
    /// they must stay Global and on, or every company would stop being read.
    /// </summary>
    public static List<PromptError> Check(PromptKind kind, string key, PromptInput input, bool lockedGlobal)
    {
        var errors = new List<PromptError>();

        if (!IsValidKey(key))
            errors.Add(new("Key", "The key must start with a letter and have 2 to 40 lower case letters, digits or _."));
        else if (KindForKey(key) != kind || key == OtherKey)
            errors.Add(new("Key", $"The keys \"{SystemKey}\", \"{DetectKey}\" and \"{OtherKey}\" are reserved."));

        if (input.Name.Length == 0)
            errors.Add(new("Name", "Enter a name."));
        else if (input.Name.Length > 100)
            errors.Add(new("Name", "The name can have at most 100 characters."));
        if (input.Description is { Length: > 1000 })
            errors.Add(new("Description", "The description can have at most 1000 characters."));

        // {types}, {fields} and the word json are optional since the 5a1 fix: PromptSet adds what is missing.
        if (input.Prompt.Length == 0)
            errors.Add(new("Prompt", "Enter the prompt text."));

        if (input.Scope == PromptScope.SelectedCompanies && input.CompanyIds.Count == 0)
            errors.Add(new("CompanyIds", "Tick at least one company, or choose Global."));

        if (lockedGlobal)
        {
            if (input.Scope != PromptScope.Global)
                errors.Add(new("Scope", "The global system and detection prompts must stay Global. Make a version for selected companies instead."));
            if (!input.IsActive)
                errors.Add(new("IsActive", "The global system and detection prompts cannot be switched off: no slip could be read."));
        }

        if (kind == PromptKind.Type)
        {
            if (input.Description is null)
                errors.Add(new("Description", "Describe when a photo is this type: the type detector reads it."));
            errors.AddRange(CheckFields(input.Fields));
        }
        else if (input.Fields.Count > 0)
            errors.Add(new("Fields", "Only capture types have fields."));

        return errors;
    }

    public static List<PromptError> CheckFields(List<PromptField> fields)
    {
        var errors = new List<PromptError>();
        if (fields.Count == 0)
        {
            errors.Add(new("Fields", "Add at least one field."));
            return errors;
        }

        var keys = new HashSet<string>(StringComparer.Ordinal);
        var cores = new HashSet<string>(StringComparer.Ordinal);
        foreach (var f in fields)
        {
            var name = f.Label.Length > 0 ? f.Label : f.Key;
            if (!IsValidFieldKey(f.Key))
                errors.Add(new("Fields", $"Field \"{name}\": the key must start with a letter and have up to 40 lower case letters, digits or _."));
            else if (!keys.Add(f.Key))
                errors.Add(new("Fields", $"The field key \"{f.Key}\" is used twice."));
            if (f.Label.Length == 0)
                errors.Add(new("Fields", $"Field \"{f.Key}\": enter a label."));
            if (!FieldTypes.Contains(f.Type))
                errors.Add(new("Fields", $"Field \"{name}\": unknown type \"{f.Type}\"."));
            if (f.Unit is { Length: > 20 })
                errors.Add(new("Fields", $"Field \"{name}\": the unit can have at most 20 characters."));
            if (f.Core is not null)
            {
                if (!CoreColumns.TryGetValue(f.Core, out var allowed))
                    errors.Add(new("Fields", $"Field \"{name}\": unknown core column \"{f.Core}\"."));
                else if (!cores.Add(f.Core))
                    errors.Add(new("Fields", $"The core column {f.Core} is used by more than one field."));
                else if (!allowed.Contains(f.Type))
                    errors.Add(new("Fields", $"Field \"{name}\": core column {f.Core} needs the type {string.Join(" or ", allowed.Select(a => FieldTypeNames[a]))}."));
            }
        }
        return errors;
    }

    [GeneratedRegex("^[a-z][a-z0-9_]{1,39}$")]
    private static partial Regex KeyRegex();

    [GeneratedRegex("^[a-z][a-z0-9_]{0,39}$")]
    private static partial Regex FieldKeyRegex();
}
