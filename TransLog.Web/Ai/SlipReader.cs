using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TransLog.Web.Data;

namespace TransLog.Web.Ai;

/// <summary>How a read ended (Part 5a2b).</summary>
public enum ReadOutcome
{
    /// <summary>Both steps answered: type and fields read. The slip goes to review.</summary>
    Read = 0,

    /// <summary>Step 1 said "other", gave an unknown type or low confidence; step 2 was not run.</summary>
    TypeNotSure = 1,

    /// <summary>A bad answer twice, or the provider refused the photo. The slip goes to review.</summary>
    CouldNotRead = 2,

    /// <summary>No answer, timeout, key refused, 429 or 5xx: not the slip's fault, try again later.</summary>
    ProviderProblem = 3,

    /// <summary>No provider that is on and has a key (or its kind is not built): the slip keeps waiting.</summary>
    NoProvider = 4,
}

/// <summary>One step of a read (upload, detect, read) with its timing, tokens and cost.</summary>
public sealed record ReadStep(string Name, bool Ok, TimeSpan Duration, AiTokens Tokens = default, int Tries = 1,
    string? Json = null, string? FinishReason = null, string? Error = null, AiCost? Cost = null);

/// <summary>One field as read: the text value, the AI's confidence and the core column it fills.</summary>
public sealed record ReadValue(string Key, string Label, string Type, string? Core, string? Value, string? Confidence);

/// <summary>Everything one read produced. 5a2b1 shows it on the AI test page; 5a2b2 stores it on the capture.</summary>
public sealed class SlipRead
{
    public ReadOutcome Outcome { get; set; }

    public string? Message { get; set; }

    public AiProvider? Provider { get; set; }

    public string? Model { get; set; }

    public int? SystemVersion { get; set; }

    public int? DetectVersion { get; set; }

    public int? TypeVersion { get; set; }

    /// <summary>The texts as sent (for the test page).</summary>
    public string SystemText { get; set; } = "";

    public string DetectText { get; set; } = "";

    public string? TypeText { get; set; }

    public CaptureType? Type { get; set; }

    /// <summary>The key step 1 answered (may be "other" or a key the company does not have).</summary>
    public string? TypeKey { get; set; }

    public string? TypeConfidence { get; set; }

    public string? TypeDescription { get; set; }

    public List<ReadStep> Steps { get; } = [];

    public string? DetectRawJson { get; set; }

    public string? AiRawJson { get; set; }

    public List<ReadValue> Values { get; } = [];

    public decimal? Amount { get; set; }

    public decimal? Quantity { get; set; }

    public decimal? Odometer { get; set; }

    public string? ReferenceNo { get; set; }

    public string? Vendor { get; set; }

    public DateOnly? EventDate { get; set; }

    public AiTokens Tokens { get; set; }

    /// <summary>Both calls added up; null when the provider has no price row for the model.</summary>
    public AiCost? Cost { get; set; }

    public TimeSpan Total { get; set; }

    public DateTime StartedAt { get; set; }
}

/// <summary>
/// Reads one photo (Part 5a2b): uploads it once, step 1 detects the type, step 2 reads that type's fields, then the
/// file is deleted. A bad answer is retried once. Prompts are sent with PromptSet.SystemText / DetectText / TypeText.
/// No database writes: the caller decides what to store.
/// </summary>
public class SlipReader(AppDbContext db, AiProviders providers, AiKeys keys, IVisionClientFactory factory)
{
    public const int DetectMaxTokens = 300;
    public const int ReadMaxTokens = 1500;

    /// <summary>Reads with the company's provider, or with <paramref name="providerId"/> when given (AI test page).</summary>
    public async Task<SlipRead> ReadAsync(int companyId, byte[] photo, int? providerId = null, CancellationToken ct = default)
    {
        var watch = Stopwatch.StartNew();
        var result = new SlipRead { StartedAt = DateTime.UtcNow };
        try
        {
            await RunAsync(result, companyId, photo, providerId, ct);
        }
        finally
        {
            result.Total = watch.Elapsed;
        }
        return result;
    }

    private async Task RunAsync(SlipRead r, int companyId, byte[] photo, int? providerId, CancellationToken ct)
    {
        // Prompts first: the test page shows them even when no provider can be used.
        var prompts = await PromptSet.ForCompanyAsync(db, companyId);
        r.SystemText = prompts.SystemText();
        r.DetectText = prompts.DetectText();
        r.SystemVersion = prompts.SystemPrompt?.CurrentVersion;
        r.DetectVersion = prompts.DetectPrompt?.CurrentVersion;

        AiProvider? provider;
        if (providerId is int id)
        {
            provider = await db.AiProviders.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);
            if (provider is not null && !providers.IsUsable(provider))
                provider = null;
        }
        else
            provider = (await providers.ForCompanyAsync(companyId)).Provider;

        var apiKey = provider is null ? null : keys.KeyFor(provider);
        var client = provider is null || apiKey is null ? null : factory.Create(provider, apiKey);
        if (provider is null || client is null)
        {
            r.Outcome = ReadOutcome.NoProvider;
            r.Message = "No AI provider that is on and has a working API key.";
            return;
        }
        r.Provider = provider;
        r.Model = provider.Model;

        if (prompts.SystemPrompt is null || prompts.DetectPrompt is null)
        {
            r.Outcome = ReadOutcome.CouldNotRead;
            r.Message = "The system or detect prompt is missing.";
            return;
        }

        var price = await AiPricing.FindAsync(db, provider.Id, provider.Model, r.StartedAt);
        var rate = await PlatformSettings.UsdToZarAsync(db);

        var upload = await client.UploadAsync(photo, ct);
        r.Steps.Add(new ReadStep("Upload", upload.Ok, upload.Duration, Error: upload.Error));
        if (!upload.Ok)
        {
            r.Outcome = upload.Failure == VisionFailure.Refused ? ReadOutcome.CouldNotRead : ReadOutcome.ProviderProblem;
            r.Message = upload.Error;
            return;
        }

        try
        {
            // Step 1: detect the type.
            var detect = await AskAsync(client, "Detect", r.SystemText, upload.FileId!, r.DetectText, DetectMaxTokens, price, rate, ct);
            r.Steps.Add(detect.Step);
            r.DetectRawJson = detect.Answer.Json;
            if (!detect.Answer.Ok)
            {
                Fail(r, detect.Answer);
                return;
            }

            ReadDetect(r, prompts, detect.Answer.Json!);
            if (r.Type is null)
            {
                r.Outcome = ReadOutcome.TypeNotSure;
                r.Message = "Type not sure" + (r.TypeKey is null ? "." : $" (answered \"{r.TypeKey}\", {r.TypeConfidence ?? "no"} confidence).");
                return;
            }

            // Step 2: read that type's fields.
            r.TypeVersion = r.Type.CurrentVersion;
            r.TypeText = PromptSet.TypeText(r.Type);
            var read = await AskAsync(client, "Read", r.SystemText, upload.FileId!, r.TypeText, ReadMaxTokens, price, rate, ct);
            r.Steps.Add(read.Step);
            r.AiRawJson = read.Answer.Json;
            if (!read.Answer.Ok)
            {
                Fail(r, read.Answer);
                return;
            }

            ReadFields(r, PromptRules.FieldsFromJson(r.Type.FieldsJson), read.Answer.Json!);
            r.Outcome = ReadOutcome.Read;
        }
        finally
        {
            var w = Stopwatch.StartNew();
            await client.DeleteAsync(upload.FileId!, CancellationToken.None);
            r.Steps.Add(new ReadStep("Delete", true, w.Elapsed));
            Totals(r, price, rate);
        }
    }

    private static void Fail(SlipRead r, VisionAnswer a)
    {
        r.Outcome = a.Failure == VisionFailure.Provider ? ReadOutcome.ProviderProblem : ReadOutcome.CouldNotRead;
        r.Message = a.Failure == VisionFailure.Provider ? a.Error : "Could not be read: " + a.Error;
    }

    /// <summary>One call, retried once after a bad answer. Tokens of both tries count (both are paid for).</summary>
    private static async Task<(ReadStep Step, VisionAnswer Answer)> AskAsync(IVisionClient client, string name, string system,
        string fileId, string prompt, int maxTokens, AiPrice? price, decimal rate, CancellationToken ct)
    {
        var at = DateTime.UtcNow;
        var answer = await client.AskAsync(system, fileId, prompt, maxTokens, ct);
        var tokens = answer.Tokens;
        var duration = answer.Duration;
        var tries = 1;
        if (answer.Failure == VisionFailure.BadAnswer)
        {
            answer = await client.AskAsync(system, fileId, prompt, maxTokens, ct);
            tokens = Add(tokens, answer.Tokens);
            duration += answer.Duration;
            tries = 2;
        }
        var cost = price is null ? null : AiPricing.Cost(price, tokens, at, rate);
        var step = new ReadStep(name, answer.Ok, duration, tokens, tries, answer.Json, answer.FinishReason, answer.Error, cost);
        return (step, answer);
    }

    private static void Totals(SlipRead r, AiPrice? price, decimal rate)
    {
        var tokens = default(AiTokens);
        foreach (var s in r.Steps)
            tokens = Add(tokens, s.Tokens);
        r.Tokens = tokens;
        var costs = r.Steps.Where(s => s.Cost is not null).Select(s => s.Cost!).ToList();
        if (price is null || costs.Count == 0)
            return;
        r.Cost = new AiCost(price.Id, costs.Any(c => c.Peak), costs.Sum(c => c.Usd), rate, costs.Sum(c => c.Zar));
    }

    private static AiTokens Add(AiTokens a, AiTokens b) => new(a.Input + b.Input, a.Cached + b.Cached, a.Output + b.Output);

    /// <summary>Step 1's answer {"type", "confidence", "description"}: a known type with high or medium confidence is used.</summary>
    public static void ReadDetect(SlipRead r, PromptSet prompts, string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        r.TypeKey = Text(root, "type")?.Trim().ToLowerInvariant();
        r.TypeConfidence = Text(root, "confidence")?.Trim().ToLowerInvariant();
        r.TypeDescription = Text(root, "description");
        if (r.TypeKey is null || r.TypeKey == PromptRules.OtherKey || r.TypeConfidence is "low" or null)
            return;
        r.Type = prompts.Types.FirstOrDefault(t => t.Key == r.TypeKey);
    }

    /// <summary>
    /// Step 2's answer {"fields": {"key": {"value": …, "confidence": …}}}. Every listed field gets a row (null when
    /// missing); core columns are filled from the fields that carry them.
    /// </summary>
    public static void ReadFields(SlipRead r, List<PromptField> fields, string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var data = root.TryGetProperty("fields", out var f) && f.ValueKind == JsonValueKind.Object ? f : root;
        foreach (var field in fields)
        {
            string? value = null, confidence = null;
            if (data.TryGetProperty(field.Key, out var el))
            {
                if (el.ValueKind == JsonValueKind.Object)
                {
                    value = el.TryGetProperty("value", out var v) ? AsText(v) : null;
                    confidence = Text(el, "confidence")?.ToLowerInvariant();
                }
                else
                    value = AsText(el);
            }
            if (string.IsNullOrWhiteSpace(value))
            {
                value = null;
                confidence = null; // confidence means nothing on an empty field
            }
            r.Values.Add(new ReadValue(field.Key, field.Label, field.Type, field.Core, value, confidence));
            if (value is not null && field.Core is not null)
                SetCore(r, field, value);
        }
    }

    private static void SetCore(SlipRead r, PromptField field, string value)
    {
        switch (field.Core)
        {
            case "Amount": r.Amount = Number(value); break;
            case "Quantity": r.Quantity = Number(value); break;
            case "Odometer": r.Odometer = Number(value); break;
            case "ReferenceNo": r.ReferenceNo = Cut(value, 100); break;
            case "Vendor": r.Vendor = Cut(value, 200); break;
            case "EventDate": r.EventDate = Date(value); break;
        }
    }

    /// <summary>"R1 234.50", "1234,5", "412 l" → 1234.50, 1234.5, 412. A comma with three digits after it and a point elsewhere is a thousands separator.</summary>
    public static decimal? Number(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var chars = text.Where(c => char.IsDigit(c) || c is '.' or ',' or '-').ToArray();
        var s = new string(chars).Trim('.', ',');
        if (s.Length == 0)
            return null;
        if (s.Contains('.') && s.Contains(','))
            s = s.Replace(",", "", StringComparison.Ordinal);
        else
            s = s.Replace(',', '.');
        if (s.Count(c => c == '.') > 1)
            return null;
        return decimal.TryParse(s, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var n) ? n : null;
    }

    private static readonly string[] DateFormats =
        ["yyyy-MM-dd", "yyyy/MM/dd", "dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy", "d-M-yyyy", "dd.MM.yyyy", "dd/MM/yy", "d/M/yy"];

    /// <summary>YYYY-MM-DD as asked; South African day-first dates are accepted too. Times after the date are ignored.</summary>
    public static DateOnly? Date(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var s = text.Trim();
        var space = s.IndexOfAny(new[] { ' ', 'T' });
        if (space > 0)
            s = s[..space];
        return DateOnly.TryParseExact(s, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
    }

    private static string? Text(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) ? AsText(v) : null;

    private static string? AsText(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.String => v.GetString(),
        JsonValueKind.Number => v.GetRawText(),
        JsonValueKind.True => "yes",
        JsonValueKind.False => "no",
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        _ => v.GetRawText(),
    };

    private static string Cut(string s, int max) => s.Trim().Length <= max ? s.Trim() : s.Trim()[..max];
}
