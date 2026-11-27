using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TransLog.Web.Data;

namespace TransLog.Web.Security;

public static class Roles
{
    /// <summary>TransLog staff. Not tied to a company.</summary>
    public const string SysAdmin = "SysAdmin";

    public const string Owner = "Owner";
    public const string Admin = "Admin";
    public const string Capturer = "Capturer";
    public const string Viewer = "Viewer";

    /// <summary>A driver of a company (Part 3c). Opens only the Driver area; the login belongs to a Driver record.</summary>
    public const string Driver = "Driver";

    public static readonly string[] All = [SysAdmin, Owner, Admin, Capturer, Viewer, Driver];

    /// <summary>Office roles of a company: they open the company area. Driver logins are made from the driver's page.</summary>
    public static readonly string[] Company = [Owner, Admin, Capturer, Viewer];

    /// <summary>Roles an Owner can give in the company area. Owners are managed by TransLog only.</summary>
    public static readonly string[] OwnerCanGive = [Admin, Capturer, Viewer];
}

public static class Policies
{
    public const string SysAdmin = "SysAdmin";

    /// <summary>Any user of a company: opens the company area.</summary>
    public const string CompanyMember = "CompanyMember";

    /// <summary>A company Owner: users, settings and activity.</summary>
    public const string CompanyOwner = "CompanyOwner";

    /// <summary>A company Owner or Admin: drivers and vehicles.</summary>
    public const string CompanyAdmin = "CompanyAdmin";

    /// <summary>A driver of a company: opens the Driver area only.</summary>
    public const string DriverMember = "DriverMember";
}

public static class AppClaims
{
    public const string CompanyId = "translog:company_id";
    public const string FullName = "translog:full_name";
    public const string CompanyName = "translog:company_name";
}

public static class ClaimsPrincipalExtensions
{
    public static int? GetUserId(this ClaimsPrincipal user) =>
        int.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : null;

    public static int? GetCompanyId(this ClaimsPrincipal user) =>
        int.TryParse(user.FindFirstValue(AppClaims.CompanyId), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : null;

    public static string GetFullName(this ClaimsPrincipal user) =>
        user.FindFirstValue(AppClaims.FullName) is { Length: > 0 } name ? name : user.Identity?.Name ?? "";

    public static string GetCompanyName(this ClaimsPrincipal user) =>
        user.FindFirstValue(AppClaims.CompanyName) ?? "";
}

/// <summary>
/// Adds the company id, company name and full name to the login cookie. The cookie is rebuilt every
/// 5 minutes (security stamp check), so a renamed company shows its new name soon after.
/// </summary>
public class AppClaimsFactory(UserManager<AppUser> users, RoleManager<AppRole> roles, IOptions<IdentityOptions> options, AppDbContext db)
    : UserClaimsPrincipalFactory<AppUser, AppRole>(users, roles, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(AppUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        identity.AddClaim(new Claim(AppClaims.FullName, user.FullName));
        if (user.CompanyId is int companyId)
        {
            identity.AddClaim(new Claim(AppClaims.CompanyId, companyId.ToString(CultureInfo.InvariantCulture)));
            var name = await db.Companies.Where(c => c.Id == companyId).Select(c => c.Name).FirstOrDefaultAsync();
            identity.AddClaim(new Claim(AppClaims.CompanyName, name ?? ""));
        }
        return identity;
    }
}

/// <summary>
/// The company of the logged-in user, read by the tenant filter on every database query.
/// Null for TransLog system admins and visitors.
/// </summary>
public class CurrentCompany(IHttpContextAccessor http)
{
    public int? Id => http.HttpContext?.User.GetCompanyId();
}

/// <summary>Adds an audit entry to the context; the caller saves.</summary>
public class AuditLogger(AppDbContext db, IHttpContextAccessor http)
{
    public void Add(string action, string? details = null, int? companyId = null)
    {
        var user = http.HttpContext?.User;
        db.AuditLogs.Add(new AuditLog
        {
            CreatedAt = DateTime.UtcNow,
            UserId = user?.GetUserId(),
            UserName = user?.Identity?.Name,
            CompanyId = companyId,
            Action = action,
            Details = details is { Length: > 1000 } ? details[..1000] : details,
        });
    }
}
