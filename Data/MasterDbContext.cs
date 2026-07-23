using Microsoft.EntityFrameworkCore;
using Clienta.Api.Entities;

namespace Clienta.Api.Data;

/// <summary>
/// Separate DbContext for master/global data that is NOT tenant-scoped.
/// This avoids the need for .IgnoreQueryFilters() when accessing global data.
/// 
/// Contains:
/// - Tenants (system-wide)
/// - BusinessUsers (user-tenant mappings)
/// 
/// Does NOT apply global tenant filters.
/// </summary>
public class MasterDbContext : DbContext
{
    public MasterDbContext(DbContextOptions<MasterDbContext> options)
        : base(options) { }

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<BusinessUser> BusinessUsers => Set<BusinessUser>();
    public DbSet<PlatformUser> PlatformUsers => Set<PlatformUser>();
    public DbSet<PlatformSettings> PlatformSettings => Set<PlatformSettings>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Tenant unique index on Subdomain
        modelBuilder.Entity<Tenant>()
            .HasIndex(t => t.Subdomain)
            .IsUnique();

        modelBuilder.Entity<Tenant>()
            .HasOne(t => t.OwnerUser)
            .WithMany()
            .HasForeignKey(t => t.OwnerUserId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Tenant>()
            .HasMany(t => t.Users)
            .WithOne()
            .HasForeignKey(u => u.TenantId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<BusinessUser>()
            .HasOne(bu => bu.Tenant)
            .WithMany()
            .HasForeignKey(bu => bu.TenantId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<BusinessUser>()
            .HasOne(bu => bu.User)
            .WithMany()
            .HasForeignKey(bu => bu.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<PlatformUser>()
            .Property(pu => pu.Role)
            .HasConversion<string>();

        modelBuilder.Entity<PlatformUser>()
            .HasIndex(pu => pu.Email)
            .IsUnique();

        modelBuilder.Entity<PlatformSettings>()
            .HasKey(settings => settings.Id);

        // No global query filters - this is master data accessible to all tenants
    }
}
