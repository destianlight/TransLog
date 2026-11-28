using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TransLog.Web.Ai;
using TransLog.Web.Data;
using TransLog.Web.Security;
using TransLog.Web.Storage;

namespace TransLog.Web.Areas.SysAdmin.Pages;

/// <summary>What the AI test page shows after a run (partial _AiTestResult).</summary>
public sealed record AiTestView(string? Error, string? CompanyName = null, int Bytes = 0, int? Width = null, int? Height = null, SlipRead? Read = null);

/// <summary>
/// Part 5a2b1: a SysAdmin tries a photo with any company's prompts and any usable provider. Both answers, timing and cost
/// are shown; nothing is stored (the photo stays in memory and is deleted from the provider afterwards).
/// </summary>
[RequestSizeLimit(AiTestModel.MaxBytes + 1_000_000)]
[RequestFormLimits(MultipartBodyLengthLimit = AiTestModel.MaxBytes + 1_000_000)]
public class AiTestModel(AppDbContext db, AiProviders providers, SlipReader reader, AuditLogger audit) : PageModel
{
    public const int MaxBytes = 8 * 1024 * 1024;

    public List<(int Id, string Name, bool Active)> Companies { get; private set; } = [];

    public List<AiProvider> Usable { get; private set; } = [];

    public string GlobalName { get; private set; } = "";

    public async Task OnGetAsync()
    {
        Companies = (await db.Companies.AsNoTracking().OrderBy(c => c.Name).Select(c => new { c.Id, c.Name, c.IsActive }).ToListAsync())
            .Select(c => (c.Id, c.Name, c.IsActive)).ToList();
        Usable = await providers.UsableAsync();
        var key = await PlatformSettings.GetAsync(db, PlatformSettings.ProviderKey) ?? PlatformSettings.DefaultProvider;
        GlobalName = await db.AiProviders.AsNoTracking().Where(p => p.Key == key).Select(p => p.Name).FirstOrDefaultAsync() ?? key;
    }

    public async Task<IActionResult> OnPostRunAsync(int companyId, int? providerId, IFormFile? photo, CancellationToken ct)
    {
        var company = await db.Companies.AsNoTracking().Where(c => c.Id == companyId).Select(c => c.Name).FirstOrDefaultAsync(ct);
        if (company is null)
            return Show(new AiTestView("Choose a company."));
        if (photo is null || photo.Length == 0)
            return Show(new AiTestView("Choose a photo."));
        if (photo.Length > MaxBytes)
            return Show(new AiTestView("The photo is larger than 8 MB. Tick \"Shrink like the phone\"."));

        byte[] bytes;
        using (var ms = new MemoryStream())
        {
            await photo.CopyToAsync(ms, ct);
            bytes = ms.ToArray();
        }
        if (!IsPhoto(bytes))
            return Show(new AiTestView("Only JPEG, PNG or WebP photos can be read."));

        var size = JpegInfo.Read(bytes);
        var read = await reader.ReadAsync(companyId, bytes, providerId is > 0 ? providerId : null, ct);

        audit.Add("AiTestRun", $"{company}: {read.Provider?.Name ?? "no provider"}, {read.Outcome}"
            + (read.TypeKey is null ? "" : ", " + read.TypeKey)
            + (read.Cost is null ? "" : ", " + AiPricing.Rand(read.Cost.Zar)));
        await db.SaveChangesAsync(ct);

        return Show(new AiTestView(null, company, bytes.Length, size?.Width, size?.Height, read));
    }

    private PartialViewResult Show(AiTestView view) => Partial("/Areas/SysAdmin/Pages/_AiTestResult.cshtml", view);

    public static bool IsPhoto(byte[] b) =>
        (b.Length > 3 && b[0] == 0xFF && b[1] == 0xD8)
        || DeepSeekVisionClient.PhotoKind(b).Mime != "image/jpeg";

    public static string Seconds(TimeSpan t) => t.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + " s";

    public static string Tokens(AiTokens t) =>
        string.Create(CultureInfo.InvariantCulture, $"{t.Input} in ({t.Cached} cached) · {t.Output} out");
}
