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

    public DbSet<Vehicle> Vehicles => Set<Vehicle>();

    public DbSet<VehicleAlias> VehicleAliases => Set<VehicleAlias>();

    public DbSet<Driver> Drivers => Set<Driver>();

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

        // ---- Fleet (Part 3) ----
        // (CompanyId, Id) is an alternate key on vehicles and drivers, so later tables can point at
        // (CompanyId, VehicleId): a row can then only ever refer to a vehicle of its own company.
        b.Entity<Vehicle>(e =>
        {
            e.HasOne<Company>().WithMany().HasForeignKey(v => v.CompanyId).OnDelete(DeleteBehavior.Restrict);
            e.HasAlternateKey(v => new { v.CompanyId, v.Id });
            e.HasIndex(v => new { v.CompanyId, v.RegistrationNo }).IsUnique();
            e.Property(v => v.TankCapacity).HasPrecision(8, 1);
        });

        b.Entity<VehicleAlias>(e =>
        {
            e.HasOne<Company>().WithMany().HasForeignKey(a => a.CompanyId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(a => a.Vehicle)
                .WithMany(v => v.Aliases)
                .HasForeignKey(a => new { a.CompanyId, a.VehicleId })
                .HasPrincipalKey(v => new { v.CompanyId, v.Id })
                .OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(a => new { a.CompanyId, a.Alias }).IsUnique();
        });

        b.Entity<Driver>(e =>
        {
            e.HasOne<Company>().WithMany().HasForeignKey(d => d.CompanyId).OnDelete(DeleteBehavior.Restrict);
            e.HasAlternateKey(d => new { d.CompanyId, d.Id });
            e.HasIndex(d => new { d.CompanyId, d.Cell }).IsUnique();
            e.HasIndex(d => new { d.CompanyId, d.IdNumber }).IsUnique().HasFilter("[IdNumber] IS NOT NULL");
            e.Ignore(d => d.FullName);
        });

        // ---- Tenant filter ----
        // Every table that belongs to a company gets one line like this, so a company only ever
        // sees its own rows. System admin pages that must see everything use IgnoreQueryFilters().
        // AppUser and Company are left out on purpose: login must find any user, and company
        // pages always load the company by the id in the login cookie.
        b.Entity<AuditLog>().HasQueryFilter(a => a.CompanyId == CurrentCompanyId);
        b.Entity<Vehicle>().HasQueryFilter(v => v.CompanyId == CurrentCompanyId);
        b.Entity<VehicleAlias>().HasQueryFilter(a => a.CompanyId == CurrentCompanyId);
        b.Entity<Driver>().HasQueryFilter(d => d.CompanyId == CurrentCompanyId);
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
