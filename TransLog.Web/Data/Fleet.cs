using System.ComponentModel.DataAnnotations;

namespace TransLog.Web.Data;

/// <summary>
/// A truck, trailer or car of a company. Never deleted, only deactivated.
/// RegistrationNo is stored upper case without spaces ("LLT587NW"); see Fleet/FleetRules.cs.
/// </summary>
public class Vehicle
{
    public int Id { get; set; }

    public int CompanyId { get; set; }

    [MaxLength(20)]
    public string RegistrationNo { get; set; } = "";

    /// <summary>The company's own number for the vehicle, for example "T12".</summary>
    [MaxLength(30)]
    public string? FleetNo { get; set; }

    [MaxLength(50)]
    public string? Make { get; set; }

    [MaxLength(50)]
    public string? Model { get; set; }

    public int? Year { get; set; }

    /// <summary>One of VehicleOptions.Types.</summary>
    [MaxLength(30)]
    public string? VehicleType { get; set; }

    /// <summary>One of VehicleOptions.FuelTypes.</summary>
    [MaxLength(20)]
    public string? FuelType { get; set; }

    /// <summary>Litres.</summary>
    public decimal? TankCapacity { get; set; }

    /// <summary>Kilometres. Starts as typed here; later captures keep it up to date.</summary>
    public int? CurrentOdometer { get; set; }

    public DateOnly? LicenceDiscExpiry { get; set; }

    public DateOnly? RoadworthyExpiry { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>UTC.</summary>
    public DateTime CreatedAt { get; set; }

    public List<VehicleAlias> Aliases { get; set; } = [];
}

/// <summary>
/// Another way a slip prints this vehicle's registration (a misspelling, an old plate, a fleet name).
/// Stored in the same normal form as a registration; unique within the company.
/// </summary>
public class VehicleAlias
{
    public int Id { get; set; }

    public int CompanyId { get; set; }

    public int VehicleId { get; set; }

    public Vehicle? Vehicle { get; set; }

    [MaxLength(20)]
    public string Alias { get; set; } = "";
}

/// <summary>A driver of a company. Never deleted, only deactivated.</summary>
public class Driver
{
    public int Id { get; set; }

    public int CompanyId { get; set; }

    [MaxLength(60)]
    public string FirstName { get; set; } = "";

    [MaxLength(60)]
    public string Surname { get; set; } = "";

    /// <summary>South African cell number, stored as 10 digits starting with 0 ("0821234567").</summary>
    [MaxLength(15)]
    public string Cell { get; set; } = "";

    [MaxLength(256)]
    public string? Email { get; set; }

    [MaxLength(300)]
    public string? Address { get; set; }

    /// <summary>13-digit South African ID number. Personal information (POPIA): shown only on the edit page, never in lists.</summary>
    [MaxLength(13)]
    public string? IdNumber { get; set; }

    [MaxLength(30)]
    public string? LicenceNo { get; set; }

    /// <summary>One of DriverOptions.LicenceCodes.</summary>
    [MaxLength(10)]
    public string? LicenceCode { get; set; }

    public DateOnly? LicenceExpiry { get; set; }

    /// <summary>Professional driving permit.</summary>
    public DateOnly? PrdpExpiry { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>UTC.</summary>
    public DateTime CreatedAt { get; set; }

    public string FullName => $"{FirstName} {Surname}".Trim();
}
