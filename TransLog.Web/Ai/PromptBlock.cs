using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using TransLog.Web.Data;

namespace TransLog.Web.Ai;

/// <summary>What a pasted block holds. Every part is optional; missing parts leave the form as it is.</summary>
public sealed class PromptBlockData
{
    public string? Name { get; set; }

    public string? Key { get; set; }

    public string? Description { get; set; }

    public List<PromptField>? Fields { get; set; }

    public string? Prompt { get; set; }
}

/// <summary>
/// Part 5a1 fix: a whole entry as one block of text (a small json object), so it can be copied out, sent to Claude
/// in a chat, and pasted back. Scope, companies and on/off are not in the block: they stay as set on the page.
/// </summary>
public static class PromptBlock
{
    private static readonly JsonSerializerOptions WriteOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // The block is shown in a textarea (Razor encodes it) and copied as text: keep ' and … readable.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly JsonSerializerOptions ReadOptions = new(JsonSerializerDefaults.Web)
    {
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    public static string Write(CaptureType e)
    {
        var block = new Dictionary<string, object?>
        {
            ["translog"] = "prompt",
            ["name"] = e.Name,
            ["key"] = e.Key,
            ["description"] = e.Description,
        };
        if (e.Kind == PromptKind.Type)
            block["fields"] = PromptRules.FieldsFromJson(e.FieldsJson);
        block["prompt"] = e.Prompt;
        return JsonSerializer.Serialize(block, WriteOptions);
    }

    /// <summary>
    /// Reads a pasted block. Text around it (a chat message, ```json fences) is ignored.
    /// Null when the text is not a block: the caller then treats it as a plain prompt.
    /// </summary>
    public static PromptBlockData? Read(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start)
            return null;
        try
        {
            var data = JsonSerializer.Deserialize<PromptBlockData>(text[start..(end + 1)], ReadOptions);
            if (data is null || (data.Name is null && data.Prompt is null && data.Fields is null && data.Description is null))
                return null;
            return data;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Puts a block's parts into the form input; returns which parts were filled.</summary>
    public static List<string> Apply(PromptBlockData block, PromptInput input, PromptKind kind)
    {
        var filled = new List<string>();
        if (block.Name is not null) { input.Name = block.Name; filled.Add("name"); }
        if (block.Description is not null) { input.Description = block.Description; filled.Add("description"); }
        if (block.Prompt is not null) { input.Prompt = block.Prompt; filled.Add("prompt"); }
        if (kind == PromptKind.Type && block.Fields is not null)
        {
            input.Fields = block.Fields.Where(f => f is not null)
                .Select(f => new PromptField
                {
                    Key = f.Key ?? "", Label = f.Label ?? "", Type = string.IsNullOrWhiteSpace(f.Type) ? "text" : f.Type,
                    Unit = f.Unit, Required = f.Required, Core = f.Core,
                })
                .ToList();
            filled.Add($"{input.Fields.Count} fields");
        }
        return filled;
    }
}
