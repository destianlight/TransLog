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
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            FullName = config["SysAdmin:FullName"] ?? "TransLog admin",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
        };

        var result = await userManager.CreateAsync(admin, password);
        if (!result.Succeeded)
            throw new InvalidOperationException("Could not create the first system admin: "
                + string.Join("; ", result.Errors.Select(e => e.Description)));

        await userManager.AddToRoleAsync(admin, Roles.SysAdmin);
        logger.LogInformation("First system admin {Email} created.", email);
    }
}
