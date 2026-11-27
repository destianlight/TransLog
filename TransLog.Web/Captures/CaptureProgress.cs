using TransLog.Web.Data;

namespace TransLog.Web.Captures;

/// <summary>Which captures a list shows (Part 4b): all, only those still busy, or only those done.</summary>
public enum CaptureShow
{
    All,
    Busy,
    Done,
}

/// <summary>
/// The four-step progress bar on every slip (Part 4b): Sent → Reading → Checking → Done.
/// Drawn by Pages/Shared/_CaptureProgress.cshtml. In 4b it shows the status when the page opened;
/// Part 5 moves it live with SignalR (the bar carries data-capture-id and data-status for that).
/// </summary>
public sealed record CaptureProgress(int CaptureId, CaptureStatus Status)
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
        _ => "",
    };

    public string Label => Status switch
    {
        CaptureStatus.Uploading => "Sending",
        CaptureStatus.Processing => "Waiting to be read",
        CaptureStatus.NeedsReview => "Being checked",
        CaptureStatus.Approved => "Approved",
        CaptureStatus.Rejected => "Rejected",
        CaptureStatus.Failed => "Could not be read",
        _ => "",
    };

    /// <summary>Reads the "show" query value (all, busy, done); anything else is All.</summary>
    public static CaptureShow ParseShow(string? text) => text?.ToLowerInvariant() switch
    {
        "busy" => CaptureShow.Busy,
        "done" => CaptureShow.Done,
        _ => CaptureShow.All,
    };

    public static IQueryable<Capture> Filter(IQueryable<Capture> query, CaptureShow show) => show switch
    {
        CaptureShow.Busy => query.Where(c => Busy.Contains(c.Status)),
        CaptureShow.Done => query.Where(c => Done.Contains(c.Status)),
        _ => query,
    };
}
