using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TransLog.Web.Data;
using TransLog.Web.Fleet;

namespace TransLog.Web.Security;

/// <summary>
/// Part 3c: a user logs in with an email or a cell number. The cell number (Identity's PhoneNumber)
/// is stored as 10 digits ("0821234567") and is unique across the whole platform; the email is
/// optional for drivers and unique when given.
/// </summary>
public static class Logins
{
    public const string CellTaken = "This cell number is already used by another TransLog account. Contact TransLog.";

    public const string EmailTaken = "This email is already used by another TransLog account.";

    public const string CellInvalid = "Enter a South African cell number, for example 082 123 4567.";

    /// <summary>
    /// Sets the email, cell number and user name of a login. The user name is the email when there is one,
    /// otherwise the cell number (it is only shown in the audit log; login looks up email or cell).
    /// </summary>
    public static void Apply(AppUser user, string? email, string? typedCell)
    {
        var cleanEmail = string.IsNullOrWhiteSpace(email) ? null : email.Trim();
        var cell = CellNumbers.Normalize(typedCell);
        user.Email = cleanEmail;
        user.EmailConfirmed = cleanEmail is not null;
        user.PhoneNumber = cell.Length == 0 ? null : cell;
        user.UserName = cleanEmail ?? (cell.Length == 0 ? user.UserName : cell);
    }

    /// <summary>What a user can type in the login box, for the Copy details text: "sipho@x.co.za or 0821234567".</summary>
    public static string Describe(AppUser user) =>
        string.Join(" or ", new[] { user.Email, user.PhoneNumber }.Where(x => !string.IsNullOrEmpty(x)));

    /// <summary>The user for what was typed in the login box: an email (has "@") or a cell number in any common form.</summary>
    public static async Task<AppUser?> FindAsync(UserManager<AppUser> users, string typed)
    {
        typed = typed.Trim();
        if (typed.Contains('@'))
        {
            var normalized = users.NormalizeEmail(typed);
            return await users.Users.FirstOrDefaultAsync(u => u.NormalizedEmail == normalized);
        }

        var cell = CellNumbers.Normalize(typed);
        if (!CellNumbers.IsValid(cell))
            return null;
        return await users.Users.FirstOrDefaultAsync(u => u.PhoneNumber == cell);
    }
}

/// <summary>
/// Runs on every create and update of a login (UserManager): the cell number must be valid and unused,
/// and the email unused. Identity's own email check is off (RequireUniqueEmail = false) because drivers
/// may have no email.
/// </summary>
public class AppUserValidator : IUserValidator<AppUser>
{
    public async Task<IdentityResult> ValidateAsync(UserManager<AppUser> manager, AppUser user)
    {
        var errors = new List<IdentityError>();

        if (!string.IsNullOrEmpty(user.PhoneNumber))
        {
            if (!CellNumbers.IsValid(user.PhoneNumber))
                errors.Add(new IdentityError { Code = "InvalidCell", Description = Logins.CellInvalid });
            else if (await manager.Users.AnyAsync(u => u.PhoneNumber == user.PhoneNumber && u.Id != user.Id))
                errors.Add(new IdentityError { Code = "DuplicateCell", Description = Logins.CellTaken });
        }

        if (!string.IsNullOrWhiteSpace(user.Email))
        {
            var normalized = manager.NormalizeEmail(user.Email);
            if (await manager.Users.AnyAsync(u => u.NormalizedEmail == normalized && u.Id != user.Id))
                errors.Add(new IdentityError { Code = "DuplicateEmail", Description = Logins.EmailTaken });
        }

        return errors.Count == 0 ? IdentityResult.Success : IdentityResult.Failed([.. errors]);
    }
}
