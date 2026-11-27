using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TransLog.Web.Security;

namespace TransLog.Web.Data;

/// <summary>
/// Runs at startup: applies database migrations, makes sure the roles exist,
/// and creates the first system admin from server settings (SysAdmin:Email, SysAdmin:Password)
/// when there is none yet. After that the settings are ignored.
/// </summary>
public static class DbInitializer
{
    public static async Task RunAsync(IServiceProvider services, IConfiguration config, ILogger logger)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;

        var db = sp.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();

        var roleManager = sp.GetRequiredService<RoleManager<AppRole>>();
        foreach (var role in Roles.All)
        {
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new AppRole(role));
        }

        var userManager = sp.GetRequiredService<UserManager<AppUser>>();

        // Part 3c: driver logins made before the Driver role existed were Capturers; move them over.
        var driverLogins = await db.Drivers.IgnoreQueryFilters()
            .Where(d => d.UserId != null)
            .Select(d => d.UserId!.Value)
            .ToListAsync();
        foreach (var userId in driverLogins)
        {
            var login = await userManager.FindByIdAsync(userId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (login is null || await userManager.IsInRoleAsync(login, Roles.Driver))
                continue;
            await userManager.RemoveFromRolesAsync(login, await userManager.GetRolesAsync(login));
            await userManager.AddToRoleAsync(login, Roles.Driver);
            logger.LogInformation("Driver login {UserId} moved to the Driver role.", userId);
        }

        if ((await userManager.GetUsersInRoleAsync(Roles.SysAdmin)).Count > 0)
            return;

        var email = config["SysAdmin:Email"]?.Trim();
        var password = config["SysAdmin:Password"];
        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
        {
            logger.LogWarning("No system admin exists. Set SysAdmin:Email and SysAdmin:Password in the server settings and restart.");
            return;
        }

        var admin = new AppUser
        {
            FullName = config["SysAdmin:FullName"] ?? "TransLog admin",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
        };
        // SysAdmin:Cell is optional here; the admin can add it later on their user page.
        Logins.Apply(admin, email, config["SysAdmin:Cell"]);

        var result = await userManager.CreateAsync(admin, password);
        if (!result.Succeeded)
            throw new InvalidOperationException("Could not create the first system admin: "
                + string.Join("; ", result.Errors.Select(e => e.Description)));

        await userManager.AddToRoleAsync(admin, Roles.SysAdmin);
        logger.LogInformation("First system admin {Email} created.", email);
    }
}
