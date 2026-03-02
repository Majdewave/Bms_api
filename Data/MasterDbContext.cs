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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Tenant unique index on Subdomain
        modelBuilder.Entity<Tenant>()
            .HasIndex(t => t.Subdomain)
            .IsUnique();

        // No global query filters - this is master data accessible to all tenants
    }
}
