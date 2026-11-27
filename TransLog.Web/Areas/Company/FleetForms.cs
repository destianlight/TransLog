using System.ComponentModel.DataAnnotations;
using TransLog.Web.Data;
using TransLog.Web.Fleet;

// The folder is Areas/Company, but the namespace is CompanyArea so it does not clash with the Company class.
namespace TransLog.Web.Areas.CompanyArea;

/// <summary>What the Owner or an Admin types for a vehicle.</summary>
public class VehicleForm
{
    [Required(ErrorMessage = "Enter the registration number."), StringLength(30)]
    public string RegistrationNo { get; set; } = "";

    [StringLength(30)]
    public string? FleetNo { get; set; }

    /// <summary>The make picked from the catalogue: its id, "other" (typed below), or empty.</summary>
    public string? MakeChoice { get; set; }

    /// <summary>The model picked from the catalogue (must belong to the chosen make).</summary>
    public int? VehicleModelId { get; set; }

    [StringLength(50)]
    public string? OtherMake { get; set; }

    [StringLength(50)]
    public string? OtherModel { get; set; }

    [Range(1950, 2100, ErrorMessage = "Enter a year such as 2019.")]
    public int? Year { get; set; }

    public string? VehicleType { get; set; }

    public string? FuelType { get; set; }

    [Range(0, 5000, ErrorMessage = "Enter the tank size in litres.")]
    public decimal? TankCapacity { get; set; }

    [Range(0, 5_000_000, ErrorMessage = "Enter the odometer in kilometres.")]
    public int? CurrentOdometer { get; set; }

    public DateOnly? LicenceDiscExpiry { get; set; }

    public DateOnly? RoadworthyExpiry { get; set; }

    public bool IsActive { get; set; } = true;

    public static VehicleForm From(Vehicle v) => new()
    {
        RegistrationNo = v.RegistrationNo,
        FleetNo = v.FleetNo,
        MakeChoice = v.VehicleModel is { } m ? m.MakeId.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : v.OtherMake is not null || v.OtherModel is not null ? OtherChoice
            : null,
        VehicleModelId = v.VehicleModelId,
        OtherMake = v.OtherMake,
        OtherModel = v.OtherModel,
        Year = v.Year,
        VehicleType = v.VehicleType,
        FuelType = v.FuelType,
        TankCapacity = v.TankCapacity,
        CurrentOdometer = v.CurrentOdometer,
        LicenceDiscExpiry = v.LicenceDiscExpiry,
        RoadworthyExpiry = v.RoadworthyExpiry,
        IsActive = v.IsActive,
    };

    public const string OtherChoice = "other";

    /// <summary>
    /// Copies everything except the registration, which the page normalises and checks first.
    /// <paramref name="model"/> is the catalogue model checked by FleetChecks.CheckModelAsync (null for none or "Other");
    /// its type, fuel and tank size fill in fields left empty.
    /// </summary>
    public void CopyTo(Vehicle v, VehicleModel? model)
    {
        v.FleetNo = Clean(FleetNo);
        v.VehicleModelId = model?.Id;
        var other = MakeChoice == OtherChoice;
        v.OtherMake = other ? Clean(OtherMake) : null;
        v.OtherModel = other ? Clean(OtherModel) : null;
        v.Year = Year;
        v.VehicleType = VehicleOptions.Types.Contains(VehicleType) ? VehicleType : model?.VehicleType;
        v.FuelType = VehicleOptions.FuelTypes.Contains(FuelType) ? FuelType : model?.FuelType;
        v.TankCapacity = TankCapacity ?? model?.TankCapacity;
        v.CurrentOdometer = CurrentOdometer;
        v.LicenceDiscExpiry = LicenceDiscExpiry;
        v.RoadworthyExpiry = RoadworthyExpiry;
        v.IsActive = IsActive;
    }

    internal static string? Clean(string? text) =>
        string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}

/// <summary>What the Owner or an Admin types for a driver.</summary>
public class DriverForm
{
    [Required(ErrorMessage = "Enter the first name."), StringLength(60)]
    public string FirstName { get; set; } = "";

    [Required(ErrorMessage = "Enter the surname."), StringLength(60)]
    public string Surname { get; set; } = "";

    [Required(ErrorMessage = "Enter the cell number."), StringLength(25)]
    public string Cell { get; set; } = "";

    [EmailAddress(ErrorMessage = "Enter a valid email."), StringLength(256)]
    public string? Email { get; set; }

    [StringLength(300)]
    public string? Address { get; set; }

    [StringLength(20)]
    public string? IdNumber { get; set; }

    [StringLength(30)]
    public string? LicenceNo { get; set; }

    public string? LicenceCode { get; set; }

    public DateOnly? LicenceExpiry { get; set; }

    public DateOnly? PrdpExpiry { get; set; }

    public bool IsActive { get; set; } = true;

    public static DriverForm From(Driver d) => new()
    {
        FirstName = d.FirstName,
        Surname = d.Surname,
        Cell = d.Cell,
        Email = d.Email,
        Address = d.Address,
        IdNumber = d.IdNumber,
        LicenceNo = d.LicenceNo,
        LicenceCode = d.LicenceCode,
        LicenceExpiry = d.LicenceExpiry,
        PrdpExpiry = d.PrdpExpiry,
        IsActive = d.IsActive,
    };

    /// <summary>Copies everything except the cell and ID number, which the page normalises and checks first.</summary>
    public void CopyTo(Driver d)
    {
        d.FirstName = FirstName.Trim();
        d.Surname = Surname.Trim();
        d.Email = VehicleForm.Clean(Email);
        d.Address = VehicleForm.Clean(Address);
        d.LicenceNo = VehicleForm.Clean(LicenceNo);
        d.LicenceCode = DriverOptions.LicenceCodes.Contains(LicenceCode) ? LicenceCode : null;
        d.LicenceExpiry = LicenceExpiry;
        d.PrdpExpiry = PrdpExpiry;
        d.IsActive = IsActive;
    }
}
