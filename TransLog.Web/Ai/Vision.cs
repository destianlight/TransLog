using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using TransLog.Web.Data;

namespace TransLog.Web.Ai;

/// <summary>Why an AI call did not give a usable answer (Part 5a2b).</summary>
public enum VisionFailure
{
    None = 0,

    /// <summary>The answer was empty, cut off (finish_reason "length") or not a json object. Retried once.</summary>
    BadAnswer = 1,

    /// <summary>Not the slip's fault: no answer, timeout, 401/403, 429 or 5xx. The worker tries again later (5a2b2).</summary>
    Provider = 2,

    /// <summary>The provider refused this request (400, 413, 422: for example a photo it cannot open). Not retried.</summary>
    Refused = 3,
}

/// <summary>The photo uploaded to the provider (DeepSeek Files API), or why it failed.</summary>
public sealed record VisionUpload(string? FileId, TimeSpan Duration, VisionFailure Failure = VisionFailure.None, string? Error = null)
{
    public bool Ok => FileId is not null;
}

/// <summary>One AI answer: the json text, the tokens it cost and how long it took.</summary>
public sealed record VisionAnswer(
    string? Json,
    string? Model,
    AiTokens Tokens,
    string? FinishReason,
    TimeSpan Duration,
    VisionFailure Failure = VisionFailure.None,
    string? Error = null)
{
    public bool Ok => Failure == VisionFailure.None && Json is not null;
}

/// <summary>Talks to one AI provider about one photo (Part 5a2b). Built by <see cref="IVisionClientFactory"/>.</summary>
public interface IVisionClient
{
    /// <summary>Uploads the photo once; both calls then use the file id.</summary>
    Task<VisionUpload> UploadAsync(byte[] photo, CancellationToken ct = default);

    /// <summary>System prompt first, then the photo, then the step's text (keeps the provider's cache working).</summary>
    Task<VisionAnswer> AskAsync(string system, string fileId, string prompt, int maxTokens, CancellationToken ct = default);

    /// <summary>Deletes the uploaded photo (the 1-hour expiry is the fallback). Never throws.</summary>
    Task DeleteAsync(string fileId, CancellationToken ct = default);
}

/// <summary>Makes the client for a provider's kind. Tests replace it with a fake (no paid calls in tests).</summary>
public interface IVisionClientFactory
{
    /// <summary>Null when that kind of provider is not built yet.</summary>
    IVisionClient? Create(AiProvider provider, string apiKey);
}

public sealed class VisionClientFactory(IHttpClientFactory http) : IVisionClientFactory
{
    /// <summary>Named HttpClient for reading slips (longer timeout than the Test button's "ai" client).</summary>
    public const string HttpName = "ai-read";

    public IVisionClient? Create(AiProvider provider, string apiKey) => provider.Kind switch
    {
        AiProviderKind.DeepSeekFiles => new DeepSeekVisionClient(http.CreateClient(HttpName), provider.BaseUrl, provider.Model, apiKey),
        _ => null, // OpenAiCompatible (OpenRouter …) comes later
    };
}

/// <summary>
/// DeepSeek with its Files API (Part 5a2b): POST /files (purpose user_data, expires after 1 hour), then
/// POST /chat/completions with the file id, thinking off, json mode, temperature 0; DELETE /files/{id} at the end.
/// </summary>
public sealed class DeepSeekVisionClient(HttpClient http, string baseUrl, string model, string apiKey) : IVisionClient
{
    /// <summary>The Files API keeps files forever unless asked; 3600 s is its shortest expiry.</summary>
    public const int ExpirySeconds = 3600;

    private readonly string _base = baseUrl.TrimEnd('/');

    public async Task<VisionUpload> UploadAsync(byte[] photo, CancellationToken ct = default)
    {
        var watch = Stopwatch.StartNew();
        try
        {
            using var form = new MultipartFormDataContent();
            form.Add(new StringContent("user_data"), "purpose");
            form.Add(new StringContent("created_at"), "expires_after[anchor]");
            form.Add(new StringContent(ExpirySeconds.ToString(CultureInfo.InvariantCulture)), "expires_after[seconds]");
            var file = new ByteArrayContent(photo);
            var (mime, ext) = PhotoKind(photo);
            file.Headers.ContentType = new MediaTypeHeaderValue(mime);
            form.Add(file, "file", "slip" + ext);

            using var request = Request(HttpMethod.Post, "/files");
            request.Content = form;
            using var response = await http.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
                return new(null, watch.Elapsed, FailureFor(response.StatusCode), HttpError("Upload", response.StatusCode, body));
            var id = ReadFileId(body);
            return id is null
                ? new(null, watch.Elapsed, VisionFailure.Provider, "Upload: the answer had no file id.")
                : new(id, watch.Elapsed);
        }
        catch (Exception ex) when ((ex is TaskCanceledException or HttpRequestException) && !ct.IsCancellationRequested)
        {
            return new(null, watch.Elapsed, VisionFailure.Provider, "Upload: " + NoAnswer(ex));
        }
    }

    public async Task<VisionAnswer> AskAsync(string system, string fileId, string prompt, int maxTokens, CancellationToken ct = default)
    {
        var watch = Stopwatch.StartNew();
        try
        {
            using var request = Request(HttpMethod.Post, "/chat/completions");
            request.Content = new StringContent(ChatBody(model, system, fileId, prompt, maxTokens), Encoding.UTF8, "application/json");
            using var response = await http.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
                return new(null, null, default, null, watch.Elapsed, FailureFor(response.StatusCode), HttpError("Call", response.StatusCode, body));
            return ReadChat(body, watch.Elapsed);
        }
        catch (Exception ex) when ((ex is TaskCanceledException or HttpRequestException) && !ct.IsCancellationRequested)
        {
            return new(null, null, default, null, watch.Elapsed, VisionFailure.Provider, NoAnswer(ex));
        }
    }

    public async Task DeleteAsync(string fileId, CancellationToken ct = default)
    {
        try
        {
            using var request = Request(HttpMethod.Delete, "/files/" + Uri.EscapeDataString(fileId));
            using var response = await http.SendAsync(request, ct);
        }
        catch (Exception ex) when (ex is TaskCanceledException or HttpRequestException)
        {
            // The file expires after an hour anyway.
        }
    }

    private HttpRequestMessage Request(HttpMethod method, string path)
    {
        var r = new HttpRequestMessage(method, _base + path);
        r.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        return r;
    }

    /// <summary>The chat request: system prompt, then the photo, then the step's text.</summary>
    public static string ChatBody(string model, string system, string fileId, string prompt, int maxTokens)
    {
        var body = new JsonObject
        {
            ["model"] = model,
            ["messages"] = new JsonArray
            {
                new JsonObject { ["role"] = "system", ["content"] = system },
                new JsonObject
                {
                    ["role"] = "user",
                    ["content"] = new JsonArray
                    {
                        new JsonObject { ["type"] = "file", ["file_id"] = fileId },
                        new JsonObject { ["type"] = "text", ["text"] = prompt },
                    },
                },
            },
            ["thinking"] = new JsonObject { ["type"] = "disabled" },
            ["response_format"] = new JsonObject { ["type"] = "json_object" },
            ["temperature"] = 0,
            ["max_tokens"] = maxTokens,
            ["stream"] = false,
        };
        return body.ToJsonString();
    }

    /// <summary>Reads a chat answer. Empty content, "length" or text that is not a json object is a bad answer, never a result.</summary>
    public static VisionAnswer ReadChat(string body, TimeSpan duration)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var tokens = ReadUsage(root);
            var usedModel = root.TryGetProperty("model", out var m) ? m.GetString() : null;
            string? content = null, finish = null;
            if (root.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0)
            {
                var c = choices[0];
                if (c.TryGetProperty("finish_reason", out var f) && f.ValueKind == JsonValueKind.String)
                    finish = f.GetString();
                if (c.TryGetProperty("message", out var msg) && msg.TryGetProperty("content", out var text) && text.ValueKind == JsonValueKind.String)
                    content = text.GetString();
            }

            if (finish == "length")
                return new(null, usedModel, tokens, finish, duration, VisionFailure.BadAnswer, "The answer was cut off (token limit).");
            var json = CleanJson(content);
            if (json is null)
                return new(null, usedModel, tokens, finish, duration, VisionFailure.BadAnswer,
                    string.IsNullOrWhiteSpace(content) ? "The answer was empty." : "The answer was not a json object.");
            return new(json, usedModel, tokens, finish, duration);
        }
        catch (JsonException)
        {
            return new(null, null, default, null, duration, VisionFailure.BadAnswer, "The provider's answer was not understood.");
        }
    }

    /// <summary>Tokens: DeepSeek gives prompt_cache_hit_tokens; OpenAI-style answers give prompt_tokens_details.cached_tokens.</summary>
    public static AiTokens ReadUsage(JsonElement root)
    {
        if (!root.TryGetProperty("usage", out var u) || u.ValueKind != JsonValueKind.Object)
            return default;
        var input = Int(u, "prompt_tokens");
        var output = Int(u, "completion_tokens");
        var cached = Int(u, "prompt_cache_hit_tokens");
        if (cached == 0 && u.TryGetProperty("prompt_tokens_details", out var d) && d.ValueKind == JsonValueKind.Object)
            cached = Int(d, "cached_tokens");
        return new AiTokens(input, cached, output);
    }

    private static int Int(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : 0;

    /// <summary>The json object in the answer (a ```json fence is removed), or null when there is none.</summary>
    public static string? CleanJson(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return null;
        var text = content.Trim();
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start)
            return null;
        text = text[start..(end + 1)];
        try
        {
            using var doc = JsonDocument.Parse(text);
            return doc.RootElement.ValueKind == JsonValueKind.Object ? text : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string? ReadFileId(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String ? id.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static VisionFailure FailureFor(HttpStatusCode status) => (int)status switch
    {
        400 or 404 or 413 or 415 or 422 => VisionFailure.Refused,
        _ => VisionFailure.Provider, // 401, 402 (no balance), 403, 429, 5xx …
    };

    /// <summary>JPEG, PNG or WebP from the first bytes (the Files API also checks the content).</summary>
    public static (string Mime, string Ext) PhotoKind(byte[] b)
    {
        if (b.Length > 3 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47)
            return ("image/png", ".png");
        if (b.Length > 11 && b[0] == 'R' && b[1] == 'I' && b[2] == 'F' && b[3] == 'F' && b[8] == 'W' && b[9] == 'E' && b[10] == 'B' && b[11] == 'P')
            return ("image/webp", ".webp");
        return ("image/jpeg", ".jpg");
    }

    private static string HttpError(string what, HttpStatusCode status, string body)
    {
        var code = ((int)status).ToString(CultureInfo.InvariantCulture);
        var text = body.Length <= 200 ? body : body[..200] + "…";
        return status switch
        {
            HttpStatusCode.Unauthorized => $"{what}: the API key was refused (401).",
            HttpStatusCode.PaymentRequired => $"{what}: the provider says the account has no balance (402).",
            HttpStatusCode.TooManyRequests => $"{what}: too many requests (429), try again later.",
            _ => $"{what}: the provider answered {code}: {text}",
        };
    }

    private static string NoAnswer(Exception ex) =>
        ex is TaskCanceledException ? "no answer in time." : "could not reach the provider (" + ex.Message + ").";
}
