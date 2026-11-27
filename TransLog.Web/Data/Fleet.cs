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

    /// <summary>
    /// Make, model and variant are typed freely, with suggestions from every company's vehicles (Part 3d).
    /// On save a value that matches a known one apart from capitals, spaces and dashes takes the known spelling.
    /// </summary>
    [MaxLength(50)]
    public string? Make { get; set; }

    /// <summary>The model family, for example "Actros".</summary>
    [MaxLength(50)]
    public string? Model { get; set; }

    /// <summary>The exact version, for example "2645 LS".</summary>
    [MaxLength(50)]
    public string? Variant { get; set; }

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

    /// <summary>"Mercedes-Benz Actros 2645 LS", or "" when not set.</summary>
    public string MakeAndModel =>
        string.Join(" ", new[] { Make, Model, Variant }.Where(x => !string.IsNullOrWhiteSpace(x)));
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

    /// <summary>The driver's login (a Driver user of the same company), when they have one.</summary>
    public int? UserId { get; set; }

    /// <summary>
    /// Profile photo (Part 4c): relative path under the file store, for example "drivers/12/34/abc.jpg"; the small copy is
    /// the same name ending in "-t.jpg". Square, 600 px. Null when there is none. See Fleet/DriverPhotos.cs.
    /// </summary>
    [MaxLength(200)]
    public string? PhotoPath { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>UTC.</summary>
    public DateTime CreatedAt { get; set; }

    public string FullName => $"{FirstName} {Surname}".Trim();
}

/// <summary>Who made a link.</summary>
public enum LinkSource
{
    /// <summary>The driver picked the vehicle on their own phone.</summary>
    Driver = 0,

    /// <summary>The Owner or an Admin did. Stored as a number; never reorder.</summary>
    Company = 1,
}

/// <summary>
/// One period during which a driver drove a vehicle. A link is open while UnlinkedAt is empty.
/// Rows are never deleted, so the history shows who drove which vehicle when. The database allows
/// one open link per driver and one per vehicle.
/// </summary>
public class DriverVehicleLink
{
    public int Id { get; set; }

    public int CompanyId { get; set; }

    public int DriverId { get; set; }

    public Driver? Driver { get; set; }

    public int VehicleId { get; set; }

    public Vehicle? Vehicle { get; set; }

    /// <summary>UTC.</summary>
    public DateTime LinkedAt { get; set; }

    /// <summary>UTC. Empty while the link is open.</summary>
    public DateTime? UnlinkedAt { get; set; }

    public LinkSource LinkedBy { get; set; }

    public int? LinkedByUserId { get; set; }

    public int? UnlinkedByUserId { get; set; }
}
