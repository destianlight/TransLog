using System.ComponentModel.DataAnnotations;
using TransLog.Web.Data;

namespace TransLog.Web.Areas.SysAdmin;

public class CompanyForm
{
    [Required(ErrorMessage = "Enter the company name."), StringLength(150)]
    public string Name { get; set; } = "";

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

    public static CompanyForm From(Company c) => new()
    {
        Name = c.Name,
        RegistrationNo = c.RegistrationNo,
        VatNo = c.VatNo,
        Email = c.Email,
        Phone = c.Phone,
        Address = c.Address,
    };

    public void CopyTo(Company c)
    {
        c.Name = Name.Trim();
        c.RegistrationNo = RegistrationNo?.Trim();
        c.VatNo = VatNo?.Trim();
        c.Email = Email?.Trim();
        c.Phone = Phone?.Trim();
        c.Address = Address?.Trim();
    }
}

public class NewUserForm
{
    [Required(ErrorMessage = "Enter the full name."), StringLength(150)]
    public string FullName { get; set; } = "";

    [Required(ErrorMessage = "Enter the email."), EmailAddress(ErrorMessage = "Enter a valid email."), StringLength(256)]
    public string Email { get; set; } = "";

    [Required(ErrorMessage = "Enter a password."), StringLength(100, MinimumLength = 6, ErrorMessage = "Use at least 6 characters.")]
    public string Password { get; set; } = "";

    /// <summary>Only for company users.</summary>
    public string? Role { get; set; }
}

public class EditUserForm
{
    [Required(ErrorMessage = "Enter the full name."), StringLength(150)]
    public string FullName { get; set; } = "";

    [Required(ErrorMessage = "Enter the email."), EmailAddress(ErrorMessage = "Enter a valid email."), StringLength(256)]
    public string Email { get; set; } = "";

    /// <summary>Only for company users.</summary>
    public string? Role { get; set; }

    public bool IsActive { get; set; }

    /// <summary>Leave empty to keep the current password.</summary>
    [StringLength(100, MinimumLength = 6, ErrorMessage = "Use at least 6 characters.")]
    public string? NewPassword { get; set; }

    public bool Unlock { get; set; }
}

public record UserRow(int Id, string FullName, string Email, string Role, bool IsActive, bool LockedOut);
