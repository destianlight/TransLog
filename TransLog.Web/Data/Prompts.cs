using System.ComponentModel.DataAnnotations;

namespace TransLog.Web.Data;

/// <summary>What a prompt entry is for. Stored as a number; only add new values at the end (5d adds Task = 3).</summary>
public enum PromptKind
{
    /// <summary>The shared system prompt: rules for every AI call (key "system").</summary>
    System = 0,

    /// <summary>Step 1: say which type of slip the photo shows (key "detect"); holds the {types} placeholder.</summary>
    Detect = 1,

    /// <summary>One capture type (fuel, weighbridge, …): description for the detector, field list and reading prompt.</summary>
    Type = 2,
}

/// <summary>Who an entry is for. Stored as a number; never reorder.</summary>
public enum PromptScope
{
    /// <summary>Every company, unless a company has its own entry with the same key.</summary>
    Global = 0,

    /// <summary>Only the companies ticked in <see cref="CaptureTypeCompany"/>; replaces the global entry with the same key for them.</summary>
    SelectedCompanies = 1,
}

/// <summary>
/// One prompt entry (Part 5a1): the shared system prompt, the type detection prompt, or a capture type.
/// A platform table managed by SysAdmins (no tenant filter). Never deleted, only switched off.
/// Every save writes a full copy to <see cref="CaptureTypeVersion"/>.
/// </summary>
public class CaptureType
{
    public int Id { get; set; }

    public PromptKind Kind { get; set; }

    /// <summary>Lower case letters, digits and "_"; fixed after creation. Only one Global entry per key.</summary>
    [MaxLength(40)]
    public string Key { get; set; } = "";

    [MaxLength(100)]
    public string Name { get; set; } = "";

    /// <summary>For types: what the type detector reads (when is a photo this type). A note for the other kinds.</summary>
    [MaxLength(1000)]
    public string? Description { get; set; }

    /// <summary>The field list as json (see <see cref="TransLog.Web.Ai.PromptField"/>); "[]" for system and detect.</summary>
    public string FieldsJson { get; set; } = "[]";

    public string Prompt { get; set; } = "";

    public PromptScope Scope { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>The latest version number (1 after creation).</summary>
    public int CurrentVersion { get; set; }

    /// <summary>UTC.</summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary>Empty for the built-in entries seeded at startup.</summary>
    public int? UpdatedByUserId { get; set; }

    public List<CaptureTypeCompany> Companies { get; set; } = [];
}

/// <summary>A company an entry with scope Selected companies is for.</summary>
public class CaptureTypeCompany
{
    public int CaptureTypeId { get; set; }

    public int CompanyId { get; set; }
}

/// <summary>A full copy of an entry as it was saved. Never changed; Restore saves an old copy as a new version.</summary>
public class CaptureTypeVersion
{
    public int Id { get; set; }

    public int CaptureTypeId { get; set; }

    public int Version { get; set; }

    [MaxLength(100)]
    public string Name { get; set; } = "";

    [MaxLength(1000)]
    public string? Description { get; set; }

    public string FieldsJson { get; set; } = "[]";

    public string Prompt { get; set; } = "";

    public PromptScope Scope { get; set; }

    /// <summary>The ticked companies at that time, as "3,7" (empty for Global).</summary>
    [MaxLength(2000)]
    public string CompanyIds { get; set; } = "";

    public bool IsActive { get; set; }

    /// <summary>UTC.</summary>
    public DateTime CreatedAt { get; set; }

    public int? CreatedByUserId { get; set; }
}
