using System.ComponentModel.DataAnnotations;

namespace TransLog.Web.Data;

/// <summary>How TransLog talks to a provider. Stored as a number; only add new values at the end.</summary>
public enum AiProviderKind
{
    /// <summary>DeepSeek's own API: the photo is uploaded once to the Files API and both calls use it (5a2b).</summary>
    DeepSeekFiles = 0,

    /// <summary>Reserved for later (OpenRouter, OpenAI, Gemini …): the photo is sent inline in each call. Not built yet.</summary>
    OpenAiCompatible = 1,
}

/// <summary>
/// An AI provider managed in the SysAdmin console (Part 5a2a). A platform table (no tenant filter).
/// Never deleted, only switched off. The API key is stored encrypted and never shown again.
/// </summary>
public class AiProvider
{
    public int Id { get; set; }

    /// <summary>Lower case, unique, fixed after creation ("deepseek"). A server setting Ai:Providers:&lt;key&gt;:ApiKey overrides the typed key.</summary>
    [MaxLength(40)]
    public string Key { get; set; } = "";

    [MaxLength(100)]
    public string Name { get; set; } = "";

    public AiProviderKind Kind { get; set; }

    [MaxLength(200)]
    public string BaseUrl { get; set; } = "";

    /// <summary>The model name sent with each call ("deepseek-flash").</summary>
    [MaxLength(100)]
    public string Model { get; set; } = "";

    /// <summary>Encrypted with ASP.NET Data Protection (purpose TransLog.AiKeys); null when no key was typed.</summary>
    [MaxLength(2000)]
    public string? ApiKeyProtected { get; set; }

    /// <summary>The last 4 characters of the typed key, for "set ••••abcd".</summary>
    [MaxLength(4)]
    public string? ApiKeyHint { get; set; }

    /// <summary>The provider's own web domain ("deepseek.com"); 5d only trusts prices found there.</summary>
    [MaxLength(100)]
    public string? OfficialDomain { get; set; }

    [MaxLength(300)]
    public string? PricingUrl { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>UTC.</summary>
    public DateTime UpdatedAt { get; set; }

    public int? UpdatedByUserId { get; set; }
}

/// <summary>Where a price row came from. Stored as a number; never reorder.</summary>
public enum AiPriceSource
{
    Manual = 0,

    /// <summary>Found by "Check provider" and approved by a SysAdmin (5d).</summary>
    PriceCheck = 1,
}

/// <summary>
/// A provider's price for one model from a date (Part 5a2a). Rows are never changed: a new price is a new row,
/// and the row with the latest EffectiveFrom on or before the reading time counts. US dollars per 1 million tokens.
/// </summary>
public class AiPrice
{
    public int Id { get; set; }

    public int AiProviderId { get; set; }

    [MaxLength(100)]
    public string Model { get; set; } = "";

    /// <summary>UTC.</summary>
    public DateTime EffectiveFrom { get; set; }

    /// <summary>Input not from the cache (peak price, or the only price when there is no off-peak price).</summary>
    public decimal InputPerM { get; set; }

    public decimal CachedInputPerM { get; set; }

    public decimal OutputPerM { get; set; }

    /// <summary>Off-peak prices; all three empty = one price all day.</summary>
    public decimal? OffPeakInputPerM { get; set; }

    public decimal? OffPeakCachedPerM { get; set; }

    public decimal? OffPeakOutputPerM { get; set; }

    /// <summary>Peak hours in UTC, "01:00-04:00,06:00-10:00".</summary>
    [MaxLength(100)]
    public string? PeakHoursUtc { get; set; }

    /// <summary>Peak days, "Mon-Fri".</summary>
    [MaxLength(50)]
    public string? PeakDays { get; set; }

    public AiPriceSource Source { get; set; }

    /// <summary>UTC.</summary>
    public DateTime CreatedAt { get; set; }

    public int? CreatedByUserId { get; set; }
}

/// <summary>An app-wide switch or value managed by SysAdmins on Platform settings (Part 5a2a). Every change is audited.</summary>
public class PlatformSetting
{
    [MaxLength(100)]
    public string Key { get; set; } = "";

    [MaxLength(1000)]
    public string Value { get; set; } = "";

    /// <summary>UTC.</summary>
    public DateTime UpdatedAt { get; set; }

    public int? UpdatedByUserId { get; set; }
}
