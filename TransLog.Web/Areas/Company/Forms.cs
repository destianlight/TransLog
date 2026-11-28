using System.ComponentModel.DataAnnotations;
using TransLog.Web.Data;

// The folder is Areas/Company, but the namespace is CompanyArea so it does not clash with the Company class.
namespace TransLog.Web.Areas.CompanyArea;

/// <summary>What the Owner may change. The name and the on/off switch stay with TransLog.</summary>
public class CompanySettingsForm
{
    [StringLength(30)]
    public string? RegistrationNo { get; set; }

    [StringLength(30)]
    public string? VatNo { get; set; }

    [EmailAddress(ErrorMessage = "Enter a valid email."), StringLength(256)]
    public string? Email { get; set; }

    [StringLength(30)]
    public string? Phone { get; set; }

    [StringLength(300)]
    public string? Address { get; set; }

    public DriverLinking DriverLinking { get; set; }

    /// <summary>Part 5b2: drivers may correct the values the AI read on their slip page.</summary>
    public bool DriversMayEditValues { get; set; } = true;

    public static CompanySettingsForm From(Company c) => new()
    {
        RegistrationNo = c.RegistrationNo,
        VatNo = c.VatNo,
        Email = c.Email,
        Phone = c.Phone,
        Address = c.Address,
        DriverLinking = c.DriverLinking,
        DriversMayEditValues = !c.DriverEditsLocked,
    };

    public void CopyTo(Company c)
    {
        c.RegistrationNo = RegistrationNo?.Trim();
        c.VatNo = VatNo?.Trim();
        c.Email = Email?.Trim();
        c.Phone = Phone?.Trim();
        c.Address = Address?.Trim();
        c.DriverLinking = Enum.IsDefined(DriverLinking) ? DriverLinking : DriverLinking.DriversAndCompany;
        c.DriverEditsLocked = !DriversMayEditValues;
    }
}
