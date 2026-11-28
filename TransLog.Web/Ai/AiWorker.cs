using System.Linq.Expressions;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TransLog.Web.Data;
using TransLog.Web.Storage;

namespace TransLog.Web.Ai;

/// <summary>
/// Part 5a2b2: wakes the worker. An upload calls <see cref="Wake"/>; the worker otherwise looks every 30 s.
/// The worker takes <see cref="Signal"/> before it looks for work, so a wake that comes while it looks is never missed.
/// </summary>
public sealed class AiQueue
{
    private readonly object _lock = new();
    private TaskCompletionSource _next = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task Signal
    {
        get
        {
            lock (_lock)
                return _next.Task;
        }
    }

    public void Wake()
    {
        TaskCompletionSource old;
        lock (_lock)
        {
            old = _next;
            _next = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        old.TrySetResult();
    }

    /// <summary>Waits until woken or the time is up. Throws only when the app stops.</summary>
    public static async Task WaitAsync(Task signal, TimeSpan timeout, CancellationToken stop)
    {
        try
        {
            await signal.WaitAsync(timeout, stop);
        }
        catch (TimeoutException)
        {
            // time for the regular look
        }
    }
}

/// <summary>
/// Part 5a2b2: one waiting slip. The Capture row is the queue: Status Processing = waiting to be read,
/// <see cref="Capture.AiNextTryAt"/> = not before, <see cref="Capture.AiLockedUntil"/> = taken by a worker.
/// Runs without a login, so every query ignores the tenant filter.
/// </summary>
public class CaptureReader(AppDbContext db, SlipReader reader, IFileStore files, TransLog.Web.Captures.CaptureLive live)
{
    /// <summary>Longer than the slowest read (upload plus four calls of up to 90 s each).</summary>
    public static readonly TimeSpan LockTime = TimeSpan.FromMinutes(10);

    /// <summary>Waits after a provider problem: 2, 10 and 30 minutes; the 4th problem sends the slip to review.</summary>
    public static readonly TimeSpan[] RetryWaits = [TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(30)];

    public static int MaxAttempts => RetryWaits.Length + 1;

    /// <summary>With no usable provider the slip keeps waiting and is looked at again every 5 minutes.</summary>
    public static readonly TimeSpan NoProviderWait = TimeSpan.FromMinutes(5);

    public static Expression<Func<Capture, bool>> Waiting(DateTime now) => c =>
        c.Status == CaptureStatus.Processing
        && (c.AiNextTryAt == null || c.AiNextTryAt <= now)
        && (c.AiLockedUntil == null || c.AiLockedUntil < now);

    /// <summary>Takes the oldest waiting slip (null when there is none). Safe when two copies of the app run.</summary>
    public async Task<int?> ClaimAsync(CancellationToken ct = default)
    {
        for (var i = 0; i < 3; i++)
        {
            var now = DateTime.UtcNow;
            var id = await db.Captures.IgnoreQueryFilters().AsNoTracking()
                .Where(Waiting(now))
                .OrderBy(c => c.ReceivedAt).ThenBy(c => c.Id)
                .Select(c => (int?)c.Id)
                .FirstOrDefaultAsync(ct);
            if (id is null)
                return null;
            DateTime? until = now + LockTime;
            var taken = await db.Captures.IgnoreQueryFilters()
                .Where(c => c.Id == id)
                .Where(Waiting(now))
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.AiLockedUntil, until), ct);
            if (taken == 1)
                return id;
        }
        return null;
    }

    /// <summary>Reads a claimed slip and stores the result. Returns how it ended (null when it was no longer waiting).</summary>
    public async Task<ReadOutcome?> ProcessAsync(int captureId, CancellationToken ct = default)
    {
        var capture = await db.Captures.IgnoreQueryFilters().Include(c => c.Images)
            .FirstOrDefaultAsync(c => c.Id == captureId, ct);
        if (capture is null || capture.Status != CaptureStatus.Processing)
            return null;

        // Part 5b: the lists show "Reading now…" while the AI works on it.
        var driverUserId = capture.DriverId is int driverId
            ? await db.Drivers.IgnoreQueryFilters().Where(d => d.Id == driverId).Select(d => d.UserId).FirstOrDefaultAsync(ct)
            : null;
        await NotifyAsync(capture, driverUserId, reading: true);

        var image = capture.Images.OrderBy(i => i.SortOrder).FirstOrDefault();
        var photo = image is null ? null : await ReadFileAsync(image.FilePath, ct);
        if (photo is null)
        {
            await KeepRemovedAsync(capture);
            if (capture.Status == CaptureStatus.Removed)
                return null;
            capture.Status = CaptureStatus.NeedsReview;
            capture.ErrorMessage = "Could not be read: the photo file is missing.";
            capture.AiLockedUntil = null;
            capture.AiNextTryAt = null;
            await db.SaveChangesAsync(CancellationToken.None);
            await NotifyAsync(capture, driverUserId, reading: false);
            return ReadOutcome.CouldNotRead;
        }

        var read = await reader.ReadAsync(capture.CompanyId, photo, null, ct);
        Apply(capture, read, DateTime.UtcNow);
        await KeepRemovedAsync(capture);
        await db.SaveChangesAsync(CancellationToken.None);
        await NotifyAsync(capture, driverUserId, reading: false);
        return read.Outcome;
    }

    /// <summary>
    /// Part 5b2: the slip may have been removed while it was being read. Then it stays removed (the read is kept, so a
    /// restore brings it back as being checked) and the queue fields are cleared.
    /// </summary>
    private async Task KeepRemovedAsync(Capture capture)
    {
        var now = await db.Captures.IgnoreQueryFilters().Where(c => c.Id == capture.Id).Select(c => c.Status).FirstAsync(CancellationToken.None);
        if (now != CaptureStatus.Removed)
            return;
        if (capture.Status != CaptureStatus.Processing)
            capture.StatusBeforeRemove = capture.Status;
        capture.Status = CaptureStatus.Removed;
        capture.AiLockedUntil = null;
        capture.AiNextTryAt = null;
    }

    private Task NotifyAsync(Capture c, int? driverUserId, bool reading) =>
        live.ChangedAsync(c.CompanyId, c.Id, c.Status, reading, c.CapturedByUserId, driverUserId);

    private async Task<byte[]?> ReadFileAsync(string path, CancellationToken ct)
    {
        await using var stream = files.OpenRead(path);
        if (stream is null)
            return null;
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, ct);
        return ms.ToArray();
    }

    /// <summary>
    /// What a read does to the slip. Read, Type not sure and Could not be read go to NeedsReview ("Being checked") with
    /// everything the AI gave. A provider problem waits 2, 10, 30 minutes, then goes to review. No provider: keeps waiting.
    /// </summary>
    public static void Apply(Capture c, SlipRead r, DateTime now)
    {
        c.AiLockedUntil = null;
        switch (r.Outcome)
        {
            case ReadOutcome.NoProvider:
                c.AiNextTryAt = now + NoProviderWait;
                c.ErrorMessage = Cut("Waiting: " + r.Message, 500);
                return;

            case ReadOutcome.ProviderProblem:
                c.AiAttempts++;
                if (c.AiAttempts < MaxAttempts)
                {
                    c.AiNextTryAt = now + RetryWaits[c.AiAttempts - 1];
                    c.ErrorMessage = Cut("Waiting: " + r.Message, 500);
                    return;
                }
                Store(c, r, now);
                c.ErrorMessage = Cut("Could not be read: the AI provider did not answer (" + r.Message + ")", 500);
                return;

            default:
                Store(c, r, now);
                c.ErrorMessage = r.Outcome switch
                {
                    ReadOutcome.Read => null,
                    ReadOutcome.TypeNotSure => Cut(r.Message ?? "Type not sure.", 500),
                    _ => Cut(r.Message is { } m && m.StartsWith("Could not be read", StringComparison.Ordinal) ? m : "Could not be read: " + r.Message, 500),
                };
                return;
        }
    }

    private static void Store(Capture c, SlipRead r, DateTime now)
    {
        c.Status = CaptureStatus.NeedsReview;
        c.AiNextTryAt = null;
        c.CaptureTypeId = r.Type?.Id;
        c.TypeConfidence = r.TypeConfidence is null ? null : Cut(r.TypeConfidence, 10);
        c.EventDate = r.EventDate;
        c.Amount = r.Amount;
        c.Quantity = r.Quantity;
        c.Odometer = r.Odometer;
        c.ReferenceNo = r.ReferenceNo;
        c.Vendor = r.Vendor;
        c.DetectRawJson = r.DetectRawJson;
        c.AiRawJson = r.AiRawJson;
        c.DataJson = r.Values.Count == 0 ? null : DataJsonOf(r.Values);
        c.SystemVersion = r.SystemVersion;
        c.DetectVersion = r.DetectVersion;
        c.TypeVersion = r.Type is null ? null : r.TypeVersion;
        c.AiProviderId = r.Provider?.Id;
        c.AiModel = r.Model is null ? null : Cut(r.Model, 100);
        c.AiInputTokens = r.Tokens.Input;
        c.AiCachedTokens = r.Tokens.Cached;
        c.AiOutputTokens = r.Tokens.Output;
        c.AiPriceId = r.Cost?.PriceId;
        c.AiPeak = r.Cost?.Peak;
        c.AiCostUsd = r.Cost?.Usd;
        c.AiUsdToZar = r.Cost?.UsdToZar;
        c.AiCostZar = r.Cost?.Zar;
        c.AiReadAt = now;
        c.AiMilliseconds = (int)Math.Min(int.MaxValue, r.Total.TotalMilliseconds);
    }

    /// <summary>{"litres": "412.5", "pump": null, …} in the order of the type's fields.</summary>
    public static string DataJsonOf(IEnumerable<ReadValue> values)
    {
        var map = new Dictionary<string, string?>();
        foreach (var v in values)
            map[v.Key] = v.Value;
        return JsonSerializer.Serialize(map);
    }

    /// <summary>Reads DataJson back; an empty dictionary when there is none or it cannot be read.</summary>
    public static Dictionary<string, string?> ReadDataJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new();
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string?>>(json) ?? new();
        }
        catch (JsonException)
        {
            return new();
        }
    }

    private static string Cut(string s, int max) => s.Length <= max ? s : s[..max];
}

/// <summary>
/// Part 5a2b2: reads waiting slips in the background, inside the web app (no separate service). Two slips at a time.
/// Looks at startup, whenever an upload wakes it, and every 30 s. Settings: <c>Ai:Worker:Enabled</c> (default true),
/// <c>Ai:Worker:EverySeconds</c> (default 30), <c>Ai:Worker:StartDelaySeconds</c> (default 5).
/// </summary>
public sealed class AiWorker(IServiceScopeFactory scopes, AiQueue queue, IConfiguration config, ILogger<AiWorker> log) : BackgroundService
{
    public const int AtOnce = 2;

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        if (!config.GetValue("Ai:Worker:Enabled", true))
            return;
        var every = TimeSpan.FromSeconds(Math.Max(1, config.GetValue("Ai:Worker:EverySeconds", 30)));
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(Math.Max(0, config.GetValue("Ai:Worker:StartDelaySeconds", 5))), stop);
            await Task.WhenAll(Enumerable.Range(0, AtOnce).Select(_ => LoopAsync(every, stop)));
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            // the app is stopping; a slip being read is picked up again after its lock time
        }
    }

    private async Task LoopAsync(TimeSpan every, CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            var signal = queue.Signal;
            var worked = false;
            try
            {
                using var scope = scopes.CreateScope();
                var job = scope.ServiceProvider.GetRequiredService<CaptureReader>();
                if (await job.ClaimAsync(stop) is int id)
                {
                    worked = true;
                    var outcome = await job.ProcessAsync(id, stop);
                    log.LogInformation("AI read capture {CaptureId}: {Outcome}", id, outcome);
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // The database or the file store failed; the slip's lock runs out and it is tried again.
                log.LogError(ex, "The AI worker failed");
                worked = false;
            }

            if (!worked)
                await AiQueue.WaitAsync(signal, every, stop);
        }
    }
}
