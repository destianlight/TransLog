using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TransLog.Web.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<AppUser, AppRole, int>(options)
{
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

        b.Entity<Company>().HasIndex(c => c.Name);

        b.Entity<AuditLog>().HasIndex(a => a.CreatedAt);
        b.Entity<AuditLog>().HasIndex(a => new { a.CompanyId, a.CreatedAt });
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
        return new AppDbContext(options);
    }
}
