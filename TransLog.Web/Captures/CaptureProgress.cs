using TransLog.Web.Data;

namespace TransLog.Web.Captures;

/// <summary>Which captures a list shows (Part 4b): all, only those still busy, or only those done.</summary>
public enum CaptureShow
{
    All,
    Busy,
    Done,

    /// <summary>Part 5b2: only removed slips (office Captures list).</summary>
    Removed,
}

/// <summary>
/// The four-step progress bar on every slip (Part 4b): Sent → Reading → Checking → Done.
/// Drawn by Pages/Shared/_CaptureProgress.cshtml. Part 5b: the lists fetch the row again when a live message arrives.
/// <paramref name="Reading"/> = the AI is reading it right now (waiting and locked by the worker): "Reading now…".
/// </summary>
public sealed record CaptureProgress(int CaptureId, CaptureStatus Status, bool Reading = false)
{
    public static readonly string[] Steps = ["Sent", "Reading", "Checking", "Done"];

    /// <summary>Not finished yet: still sending, waiting to be read or being checked.</summary>
    public static readonly CaptureStatus[] Busy = [CaptureStatus.Uploading, CaptureStatus.Processing, CaptureStatus.NeedsReview];

    public static readonly CaptureStatus[] Done = [CaptureStatus.Approved, CaptureStatus.Rejected, CaptureStatus.Failed];

    /// <summary>How many steps are finished (0 to 4). The step after them is the one in progress.</summary>
    public int Finished => Status switch
    {
        CaptureStatus.Uploading => 0,
        CaptureStatus.Processing => 1,
        CaptureStatus.NeedsReview => 2,
        _ => 4,
    };

    /// <summary>"ok" (green) or "bad" (red) once done; empty while busy.</summary>
    public string Tone => Status switch
    {
        CaptureStatus.Approved => "ok",
        CaptureStatus.Rejected or CaptureStatus.Failed => "bad",
        CaptureStatus.Removed => "off",
        _ => "",
    };

    public string Label => Status switch
    {
        CaptureStatus.Uploading => "Sending",
        CaptureStatus.Processing => Reading ? "Reading now…" : "Waiting to be read",
        CaptureStatus.NeedsReview => "Being checked",
        CaptureStatus.Approved => "Approved",
        CaptureStatus.Rejected => "Rejected",
        CaptureStatus.Failed => "Could not be read",
        CaptureStatus.Removed => "Removed",
        _ => "",
    };

    /// <summary>Reads the "show" query value (all, busy, done); anything else is All.</summary>
    public static CaptureShow ParseShow(string? text) => text?.ToLowerInvariant() switch
    {
        "busy" => CaptureShow.Busy,
        "done" => CaptureShow.Done,
        "removed" => CaptureShow.Removed,
        _ => CaptureShow.All,
    };

    /// <summary>
    /// Part 5b: the date that counts is the slip date; a slip not read yet (no slip date) counts by its photo date.
    /// </summary>
    public static IQueryable<Capture> FromDay(IQueryable<Capture> query, DateOnly day)
    {
        var start = AppCulture.SastDayStartUtc(day);
        return query.Where(c => (c.EventDate != null && c.EventDate >= day) || (c.EventDate == null && c.CapturedAt >= start));
    }

    public static IQueryable<Capture> ToDay(IQueryable<Capture> query, DateOnly day)
    {
        var end = AppCulture.SastDayStartUtc(day.AddDays(1));
        return query.Where(c => (c.EventDate != null && c.EventDate <= day) || (c.EventDate == null && c.CapturedAt < end));
    }

    public static IQueryable<Capture> OnDay(IQueryable<Capture> query, DateOnly day) => ToDay(FromDay(query, day), day);

    public static IQueryable<Capture> Filter(IQueryable<Capture> query, CaptureShow show) => show switch
    {
        CaptureShow.Busy => query.Where(c => Busy.Contains(c.Status)),
        CaptureShow.Done => query.Where(c => Done.Contains(c.Status)),
        CaptureShow.Removed => query.Where(c => c.Status == CaptureStatus.Removed),
        _ => query.Where(c => c.Status != CaptureStatus.Removed), // Part 5b2: All leaves removed slips out
    };
}
