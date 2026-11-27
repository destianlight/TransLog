using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.AspNetCore.Http;
using TransLog.Web.Security;

namespace TransLog.Web.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options, CurrentCompany currentCompany)
    : IdentityDbContext<AppUser, AppRole, int>(options)
{
    /// <summary>
    /// The logged-in user's company (null for system admins and visitors).
    /// The tenant filters below read it on every query.
    /// </summary>
    public int? CurrentCompanyId => currentCompany.Id;

    public DbSet<Company> Companies => Set<Company>();

    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        b.Entity<AppUser>()
            .HasOne(u => u.Company)
            .WithMany(c => c.Users)
            .HasForeignKey(u => u.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        // Company names are unique (SQL Server compares them without case).
        b.Entity<Company>().HasIndex(c => c.Name).IsUnique();

        b.Entity<AuditLog>().HasIndex(a => a.CreatedAt);
        b.Entity<AuditLog>().HasIndex(a => new { a.CompanyId, a.CreatedAt });

        // ---- Tenant filter ----
        // Every table that belongs to a company gets one line like this, so a company only ever
        // sees its own rows. System admin pages that must see everything use IgnoreQueryFilters().
        // AppUser and Company are left out on purpose: login must find any user, and company
        // pages always load the company by the id in the login cookie.
        b.Entity<AuditLog>().HasQueryFilter(a => a.CompanyId == CurrentCompanyId);
    }
}

/// <summary>
/// Used only by the "Add migration" workflow (dotnet ef). It never connects to this server.
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=.;Database=TransLog_Design;Trusted_Connection=True;TrustServerCertificate=True",
                sql => sql.UseCompatibilityLevel(150))
            .Options;
        return new AppDbContext(options, new CurrentCompany(new HttpContextAccessor()));
    }
}
