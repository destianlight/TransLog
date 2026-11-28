using System.Globalization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using TransLog.Web.Data;

namespace TransLog.Web.Areas.SysAdmin.Pages;

/// <summary>
/// Part 5a2b2: the slips the background worker read, across all companies: type, time, tokens, cost, errors and both
/// raw answers. No photos (SysAdmins do not open slip photos). Also what is still waiting.
/// Part 5b: 20 reads per page with Newer / Older.
/// </summary>
public class AiReadsModel(AppDbContext db) : PageModel
{
    /// <summary>Part 5b: 20 reads per page (Newer / Older), and at most 20 waiting slips listed.</summary>
    public const int Shown = 20;

    public sealed record Row(int CaptureId, string Company, CaptureStatus Status, DateTime? ReadAt, DateTime ReceivedAt,
        string? TypeName, string? TypeConfidence, string? Model, int? Input, int? Cached, int? Output, decimal? CostZar,
        bool? Peak, int? Milliseconds, string? Versions, string? Error, string? DetectJson, string? ReadJson, int Attempts, DateTime? NextTry);

    public List<Row> Reads { get; private set; } = [];

    public List<Row> Waiting { get; private set; } = [];

    public int WaitingCount { get; private set; }

    public (int Count, decimal Zar) LastDay { get; private set; }

    public (int Count, decimal Zar) Last30Days { get; private set; }

    public List<SelectListItem> CompanyChoices { get; private set; } = [];

    public int? CompanyId { get; private set; }

    public int PageNo { get; private set; } = 1;

    public bool HasNext { get; private set; }

    public async Task OnGetAsync(int? companyId, int p = 1)
    {
        CompanyId = companyId;
        PageNo = Math.Clamp(p, 1, 10_000);
        var names = await db.Companies.AsNoTracking().OrderBy(c => c.Name).ToDictionaryAsync(c => c.Id, c => c.Name);
        CompanyChoices = names.Select(n => new SelectListItem(n.Value, n.Key.ToString(CultureInfo.InvariantCulture), n.Key == companyId)).ToList();

        var all = db.Captures.IgnoreQueryFilters().AsNoTracking();
        if (companyId is int id)
            all = all.Where(c => c.CompanyId == id);

        var now = DateTime.UtcNow;
        var day = now.AddDays(-1);
        var month = now.AddDays(-30);
        var recent = await all.Where(c => c.AiReadAt >= month)
            .Select(c => new { c.AiReadAt, c.AiCostZar })
            .ToListAsync();
        LastDay = (recent.Count(c => c.AiReadAt >= day), recent.Where(c => c.AiReadAt >= day).Sum(c => c.AiCostZar ?? 0));
        Last30Days = (recent.Count, recent.Sum(c => c.AiCostZar ?? 0));

        WaitingCount = await all.CountAsync(c => c.Status == CaptureStatus.Processing);

        Reads = await RowsAsync(all.Where(c => c.AiReadAt != null).OrderByDescending(c => c.AiReadAt).ThenByDescending(c => c.Id)
            .Skip((PageNo - 1) * Shown).Take(Shown + 1), names);
        HasNext = Reads.Count > Shown;
        if (HasNext)
            Reads.RemoveAt(Reads.Count - 1);
        Waiting = await RowsAsync(all.Where(c => c.Status == CaptureStatus.Processing).OrderBy(c => c.ReceivedAt).Take(Shown), names);
    }

    /// <summary>Query values for a page link that keeps the company filter.</summary>
    public Dictionary<string, string> With(int page)
    {
        var values = new Dictionary<string, string>();
        if (CompanyId is int id)
            values["companyId"] = id.ToString(CultureInfo.InvariantCulture);
        if (page > 1)
            values["p"] = page.ToString(CultureInfo.InvariantCulture);
        return values;
    }

    private static async Task<List<Row>> RowsAsync(IQueryable<Capture> query, Dictionary<int, string> names)
    {
        var rows = await query.Select(c => new
        {
            c.Id,
            c.CompanyId,
            c.Status,
            c.AiReadAt,
            c.ReceivedAt,
            TypeName = c.CaptureType != null ? c.CaptureType.Name : null,
            c.TypeConfidence,
            c.AiModel,
            c.AiInputTokens,
            c.AiCachedTokens,
            c.AiOutputTokens,
            c.AiCostZar,
            c.AiPeak,
            c.AiMilliseconds,
            c.SystemVersion,
            c.DetectVersion,
            c.TypeVersion,
            c.ErrorMessage,
            c.DetectRawJson,
            c.AiRawJson,
            c.AiAttempts,
            c.AiNextTryAt,
        }).ToListAsync();

        return rows.Select(c => new Row(c.Id, names.TryGetValue(c.CompanyId, out var n) ? n : "?", c.Status, c.AiReadAt, c.ReceivedAt,
            c.TypeName, c.TypeConfidence, c.AiModel, c.AiInputTokens, c.AiCachedTokens, c.AiOutputTokens, c.AiCostZar, c.AiPeak,
            c.AiMilliseconds, Versions(c.SystemVersion, c.DetectVersion, c.TypeVersion), c.ErrorMessage, c.DetectRawJson, c.AiRawJson,
            c.AiAttempts, c.AiNextTryAt)).ToList();
    }

    private static string? Versions(int? system, int? detect, int? type) =>
        system is null ? null
            : string.Create(CultureInfo.InvariantCulture, $"system v{system}, detect v{detect}") + (type is int t ? string.Create(CultureInfo.InvariantCulture, $", type v{t}") : "");

    public static string When(DateTime? utc) => utc is DateTime u ? AppCulture.ToSast(u).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) : "";

    public static string Seconds(int? ms) => ms is int m ? (m / 1000m).ToString("0.0", CultureInfo.InvariantCulture) + " s" : "";

    public static string StatusText(CaptureStatus s) => s switch
    {
        CaptureStatus.Processing => "Waiting to be read",
        CaptureStatus.NeedsReview => "Being checked",
        CaptureStatus.Approved => "Approved",
        CaptureStatus.Rejected => "Rejected",
        CaptureStatus.Failed => "Could not be read",
        CaptureStatus.Removed => "Removed",
        _ => s.ToString(),
    };
}
