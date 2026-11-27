using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Html;
using Microsoft.EntityFrameworkCore;
using TransLog.Web.Data;
using TransLog.Web.Security;

namespace TransLog.Web;

/// <summary>
/// Icons from the one SVG sprite (wwwroot/resources/icons.svg, Bootstrap Icons 1.13 plus the TransLog mark).
/// To add an icon: copy its paths from icons.getbootstrap.com into a new &lt;symbol&gt; and raise the version below,
/// so phones fetch the new sprite instead of a cached one.
/// </summary>
public static class Icons
{
    public const string Sprite = "/resources/icons.svg?v=3c";

    /// <summary>An icon that sits in text: 1em high, takes the text colour, hidden from screen readers.</summary>
    public static IHtmlContent Svg(string name, string css = "bi") =>
        new HtmlString($"<svg class=\"{WebUtility.HtmlEncode(css)}\" aria-hidden=\"true\"><use href=\"{Sprite}#{WebUtility.HtmlEncode(name)}\"></use></svg>");
}

/// <summary>The coloured bar under the menu that says which area you are in.</summary>
public record AreaBadge(string Area, string Css, string Icon, string Title, string Subtitle);

/// <summary>Works out the area bar for the logged-in user (one small query for drivers, none for others).</summary>
public class AreaBadges(AppDbContext db)
{
    public async Task<AreaBadge?> ForAsync(ClaimsPrincipal user)
    {
        if (user.Identity?.IsAuthenticated != true)
            return null;

        if (user.IsInRole(Roles.SysAdmin))
            return new AreaBadge("sysadmin", "text-bg-dark", "shield-lock", "System admin", "TransLog staff · all companies");

        if (user.GetCompanyId() is null)
            return null;

        if (user.IsInRole(Roles.Driver))
        {
            var userId = user.GetUserId();
            // The tenant filter keeps this inside the driver's own company.
            var plate = await db.DriverVehicleLinks.AsNoTracking()
                .Where(l => l.UnlinkedAt == null && l.Driver!.UserId == userId)
                .Select(l => l.Vehicle!.RegistrationNo)
                .FirstOrDefaultAsync();
            return new AreaBadge("driver", "text-bg-success", "truck-front", user.GetFullName(),
                plate is null ? "Driver · no vehicle" : $"Driver · {plate}");
        }

        var role = Roles.Company.FirstOrDefault(user.IsInRole) ?? "";
        return new AreaBadge("company", "text-bg-primary", "buildings", user.GetCompanyName(), $"Company · {role}");
    }
}
