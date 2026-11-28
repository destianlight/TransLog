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

    public DbSet<DriverVehicleLink> DriverVehicleLinks => Set<DriverVehicleLink>();

    public DbSet<Capture> Captures => Set<Capture>();

    public DbSet<CaptureImage> CaptureImages => Set<CaptureImage>();

    public DbSet<CaptureEdit> CaptureEdits => Set<CaptureEdit>();

    public DbSet<CaptureType> CaptureTypes => Set<CaptureType>();

    public DbSet<CaptureTypeCompany> CaptureTypeCompanies => Set<CaptureTypeCompany>();

    public DbSet<CaptureTypeVersion> CaptureTypeVersions => Set<CaptureTypeVersion>();

    public DbSet<AiProvider> AiProviders => Set<AiProvider>();

    public DbSet<AiPrice> AiPrices => Set<AiPrice>();

    public DbSet<PlatformSetting> PlatformSettings => Set<PlatformSetting>();

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

        // Part 3c: a cell number belongs to one login on the whole platform (empty for the first system admin until set).
        b.Entity<AppUser>().HasIndex(u => u.PhoneNumber).IsUnique().HasFilter("[PhoneNumber] IS NOT NULL");
        b.Entity<AppUser>().Property(u => u.PhoneNumber).HasMaxLength(15);

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
            e.Ignore(v => v.MakeAndModel);
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
            e.HasOne<AppUser>().WithMany().HasForeignKey(d => d.UserId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(d => d.UserId).IsUnique().HasFilter("[UserId] IS NOT NULL");
            e.Ignore(d => d.FullName);
        });

        // Part 3b: links between drivers and vehicles. The database itself allows only one open link
        // (UnlinkedAt is empty) per driver and per vehicle; history rows are never deleted.
        b.Entity<DriverVehicleLink>(e =>
        {
            e.HasOne<Company>().WithMany().HasForeignKey(l => l.CompanyId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(l => l.Driver)
                .WithMany()
                .HasForeignKey(l => new { l.CompanyId, l.DriverId })
                .HasPrincipalKey(d => new { d.CompanyId, d.Id })
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(l => l.Vehicle)
                .WithMany()
                .HasForeignKey(l => new { l.CompanyId, l.VehicleId })
                .HasPrincipalKey(v => new { v.CompanyId, v.Id })
                .OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(l => l.DriverId).IsUnique().HasFilter("[UnlinkedAt] IS NULL");
            e.HasIndex(l => l.VehicleId).IsUnique().HasFilter("[UnlinkedAt] IS NULL");
            e.HasIndex(l => new { l.CompanyId, l.VehicleId, l.LinkedAt });
            e.HasIndex(l => new { l.CompanyId, l.DriverId, l.LinkedAt });
        });

        // ---- Captures (Part 4a) ----
        // A capture can only point at a vehicle and a driver of its own company (composite keys, like the links).
        b.Entity<Capture>(e =>
        {
            e.HasOne<Company>().WithMany().HasForeignKey(c => c.CompanyId).OnDelete(DeleteBehavior.Restrict);
            e.HasAlternateKey(c => new { c.CompanyId, c.Id });
            e.HasOne(c => c.Vehicle)
                .WithMany()
                .HasForeignKey(c => new { c.CompanyId, c.VehicleId })
                .HasPrincipalKey(v => new { v.CompanyId, v.Id })
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(c => c.Driver)
                .WithMany()
                .HasForeignKey(c => new { c.CompanyId, c.DriverId })
                .HasPrincipalKey(d => new { d.CompanyId, d.Id })
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne<AppUser>().WithMany().HasForeignKey(c => c.CapturedByUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(c => new { c.CompanyId, c.ClientKey }).IsUnique();
            e.HasIndex(c => new { c.CompanyId, c.CapturedAt });
            e.HasIndex(c => new { c.CompanyId, c.DriverId, c.CapturedAt });
            e.HasIndex(c => new { c.CompanyId, c.VehicleId, c.CapturedAt });

            // Part 5a2b2: what the AI read, and the queue (Status Processing = waiting to be read).
            e.HasOne(c => c.CaptureType).WithMany().HasForeignKey(c => c.CaptureTypeId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<AiProvider>().WithMany().HasForeignKey(c => c.AiProviderId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<AiPrice>().WithMany().HasForeignKey(c => c.AiPriceId).OnDelete(DeleteBehavior.Restrict);
            e.Property(c => c.Amount).HasPrecision(18, 2);
            e.Property(c => c.Quantity).HasPrecision(18, 3);
            e.Property(c => c.Odometer).HasPrecision(12, 1);
            e.Property(c => c.AiCostUsd).HasPrecision(12, 6);
            e.Property(c => c.AiUsdToZar).HasPrecision(10, 4);
            e.Property(c => c.AiCostZar).HasPrecision(12, 4);
            e.HasIndex(c => new { c.CompanyId, c.EventDate });
            e.HasIndex(c => new { c.Status, c.AiNextTryAt });
            e.HasIndex(c => c.AiReadAt);
        });

        b.Entity<CaptureImage>(e =>
        {
            e.HasOne(i => i.Capture)
                .WithMany(c => c.Images)
                .HasForeignKey(i => new { i.CompanyId, i.CaptureId })
                .HasPrincipalKey(c => new { c.CompanyId, c.Id })
                .OnDelete(DeleteBehavior.Restrict);
            e.Property(i => i.Latitude).HasPrecision(9, 6);
            e.Property(i => i.Longitude).HasPrecision(9, 6);
            e.Property(i => i.Sha256).IsFixedLength().IsUnicode(false);
            e.HasIndex(i => new { i.CompanyId, i.Sha256 });
        });

        // Part 5b2: changed values, one row per field per save.
        b.Entity<CaptureEdit>(e =>
        {
            e.HasOne(x => x.Capture)
                .WithMany()
                .HasForeignKey(x => new { x.CompanyId, x.CaptureId })
                .HasPrincipalKey(c => new { c.CompanyId, c.Id })
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.CompanyId, x.CaptureId });
        });

        // ---- AI prompts and capture types (Part 5a1) ----
        // Platform tables managed by SysAdmins: no tenant filter. Only one Global entry per key (the database checks it too).
        b.Entity<CaptureType>(e =>
        {
            e.HasIndex(t => t.Key).IsUnique().HasFilter("[Scope] = 0");
            e.HasIndex(t => new { t.Kind, t.Key });
            e.HasOne<AppUser>().WithMany().HasForeignKey(t => t.UpdatedByUserId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<CaptureTypeCompany>(e =>
        {
            e.HasKey(c => new { c.CaptureTypeId, c.CompanyId });
            e.HasOne<CaptureType>().WithMany(t => t.Companies).HasForeignKey(c => c.CaptureTypeId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Company>().WithMany().HasForeignKey(c => c.CompanyId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(c => c.CompanyId);
        });

        b.Entity<CaptureTypeVersion>(e =>
        {
            e.HasOne<CaptureType>().WithMany().HasForeignKey(v => v.CaptureTypeId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<AppUser>().WithMany().HasForeignKey(v => v.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(v => new { v.CaptureTypeId, v.Version }).IsUnique();
        });

        // ---- AI providers, prices and platform settings (Part 5a2a) ----
        // Platform tables managed by SysAdmins: no tenant filter.
        b.Entity<AiProvider>(e =>
        {
            e.HasIndex(p => p.Key).IsUnique();
            e.HasOne<AppUser>().WithMany().HasForeignKey(p => p.UpdatedByUserId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<AiPrice>(e =>
        {
            e.HasOne<AiProvider>().WithMany().HasForeignKey(p => p.AiProviderId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<AppUser>().WithMany().HasForeignKey(p => p.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(p => new { p.AiProviderId, p.Model, p.EffectiveFrom });
            foreach (var name in new[] { nameof(AiPrice.InputPerM), nameof(AiPrice.CachedInputPerM), nameof(AiPrice.OutputPerM),
                         nameof(AiPrice.OffPeakInputPerM), nameof(AiPrice.OffPeakCachedPerM), nameof(AiPrice.OffPeakOutputPerM) })
                e.Property(name).HasPrecision(12, 6);
        });

        b.Entity<PlatformSetting>(e =>
        {
            e.HasKey(s => s.Key);
            e.HasOne<AppUser>().WithMany().HasForeignKey(s => s.UpdatedByUserId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Company>().HasOne<AiProvider>().WithMany().HasForeignKey(c => c.AiProviderId).OnDelete(DeleteBehavior.Restrict);

        // ---- Tenant filter ----
        // Every table that belongs to a company gets one line like this, so a company only ever
        // sees its own rows. System admin pages that must see everything use IgnoreQueryFilters().
        // AppUser and Company are left out on purpose: login must find any user, and company
        // pages always load the company by the id in the login cookie.
        b.Entity<AuditLog>().HasQueryFilter(a => a.CompanyId == CurrentCompanyId);
        b.Entity<Vehicle>().HasQueryFilter(v => v.CompanyId == CurrentCompanyId);
        b.Entity<VehicleAlias>().HasQueryFilter(a => a.CompanyId == CurrentCompanyId);
        b.Entity<Driver>().HasQueryFilter(d => d.CompanyId == CurrentCompanyId);
        b.Entity<DriverVehicleLink>().HasQueryFilter(l => l.CompanyId == CurrentCompanyId);
        b.Entity<Capture>().HasQueryFilter(c => c.CompanyId == CurrentCompanyId);
        b.Entity<CaptureImage>().HasQueryFilter(i => i.CompanyId == CurrentCompanyId);
        b.Entity<CaptureEdit>().HasQueryFilter(x => x.CompanyId == CurrentCompanyId);
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
