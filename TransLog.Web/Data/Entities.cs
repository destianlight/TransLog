using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;

namespace TransLog.Web.Data;

/// <summary>A transport company (a tenant). Never deleted, only deactivated.</summary>
public class Company
{
    public int Id { get; set; }

    [MaxLength(150)]
    public string Name { get; set; } = "";

    [MaxLength(30)]
    public string? RegistrationNo { get; set; }

    [MaxLength(30)]
    public string? VatNo { get; set; }

    [MaxLength(256)]
    public string? Email { get; set; }

    [MaxLength(30)]
    public string? Phone { get; set; }

    [MaxLength(300)]
    public string? Address { get; set; }

    /// <summary>Who may link a driver to a vehicle (Part 3). Set by the Owner.</summary>
    public DriverLinking DriverLinking { get; set; } = DriverLinking.DriversAndCompany;

    /// <summary>Part 5a2a: the AI provider for this company; empty = the global one (Platform settings). Set by SysAdmins only.</summary>
    public int? AiProviderId { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>UTC.</summary>
    public DateTime CreatedAt { get; set; }

    public List<AppUser> Users { get; set; } = [];
}

/// <summary>Who may link a driver to a vehicle. Stored as a number; never reorder.</summary>
public enum DriverLinking
{
    /// <summary>A driver picks their own vehicle from a list; the Owner or Admin can also switch it.</summary>
    DriversAndCompany = 0,

    /// <summary>Only the Owner or Admin links drivers to vehicles.</summary>
    CompanyOnly = 1,
}

/// <summary>A person who logs in. CompanyId is null for TransLog system admins.</summary>
public class AppUser : IdentityUser<int>
{
    [MaxLength(150)]
    public string FullName { get; set; } = "";

    public int? CompanyId { get; set; }

    public Company? Company { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>UTC.</summary>
    public DateTime CreatedAt { get; set; }
}

public class AppRole : IdentityRole<int>
{
    public AppRole() { }

    public AppRole(string name) : base(name) { }
}

/// <summary>Who did what, for admin actions and password changes.</summary>
public class AuditLog
{
    public long Id { get; set; }

    /// <summary>UTC.</summary>
    public DateTime CreatedAt { get; set; }

    public int? UserId { get; set; }

    [MaxLength(256)]
    public string? UserName { get; set; }

    public int? CompanyId { get; set; }

    [MaxLength(100)]
    public string Action { get; set; } = "";

    [MaxLength(1000)]
    public string? Details { get; set; }
}
