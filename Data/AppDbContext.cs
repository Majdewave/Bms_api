using Microsoft.EntityFrameworkCore;
using Clienta.Api.Entities;
using Clienta.Api.Services;
using Microsoft.Extensions.Logging;

namespace Clienta.Api.Data;

public class AppDbContext : DbContext
{
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<AppDbContext> _logger;

    public AppDbContext(
        DbContextOptions<AppDbContext> options,
        ITenantContext tenantContext,
        ILogger<AppDbContext>? logger = null)
        : base(options)
    {
        _tenantContext = tenantContext;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<AppDbContext>.Instance;
}

    public DbSet<User> Users => Set<User>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<UserPermission> UserPermissions => Set<UserPermission>();
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<BusinessUser> BusinessUsers => Set<BusinessUser>();
    public DbSet<Client> Clients => Set<Client>();
    public DbSet<Appointment> Appointments => Set<Appointment>();
    public DbSet<Note> Notes => Set<Note>();
    public DbSet<ClientFile> ClientFiles => Set<ClientFile>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<UserToken> UserTokens => Set<UserToken>();
    public DbSet<PendingTenantRegistration> PendingTenantRegistrations => Set<PendingTenantRegistration>();
    public DbSet<ProcessedStripeEvent> ProcessedStripeEvents => Set<ProcessedStripeEvent>();
    public DbSet<Service> Services => Set<Service>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<UserPermission>()
            .HasOne(p => p.User)
            .WithMany(u => u.Permissions)
            .HasForeignKey(p => p.UserId);

        // Global Tenant Filters (CRITICAL for shared database multi-tenancy)
        modelBuilder.Entity<User>()
            .HasQueryFilter(u => u.TenantId == _tenantContext.TenantId);

        modelBuilder.Entity<Client>()
            .HasQueryFilter(c => c.TenantId == _tenantContext.TenantId);

        modelBuilder.Entity<Appointment>()
            .HasQueryFilter(a => a.TenantId == _tenantContext.TenantId);

        modelBuilder.Entity<Note>()
            .HasQueryFilter(n => n.TenantId == _tenantContext.TenantId);

        modelBuilder.Entity<ClientFile>()
            .HasQueryFilter(f => f.TenantId == _tenantContext.TenantId);

        modelBuilder.Entity<UserToken>()
            .HasQueryFilter(ut => ut.TenantId == _tenantContext.TenantId);

        // Tenant unique index on Subdomain
        modelBuilder.Entity<Tenant>()
            .HasIndex(t => t.Subdomain)
            .IsUnique();

        // UserToken relationships and unique index for tenant isolation
        modelBuilder.Entity<UserToken>()
            .HasIndex(t => new { t.TenantId, t.TokenHash })
            .IsUnique();

        modelBuilder.Entity<UserToken>()
            .HasOne(ut => ut.Tenant)
            .WithMany()
            .HasForeignKey(ut => ut.TenantId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<UserToken>()
            .HasOne(ut => ut.User)
            .WithMany()
            .HasForeignKey(ut => ut.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Explicit FK configuration for Note
        modelBuilder.Entity<Note>()
            .HasOne(n => n.Tenant)
            .WithMany()
            .HasForeignKey(n => n.TenantId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ProcessedStripeEvent>(entity =>
        {
            entity.HasKey(e => e.EventId);
            entity.Property(e => e.ProcessedAt).IsRequired();
        });

        modelBuilder.Entity<AuditLog>()
            .HasOne<User>()
            .WithMany()
            .HasForeignKey(a => a.UserId)
            .OnDelete(DeleteBehavior.SetNull);
    }

    public override async Task<int> SaveChangesAsync(
        CancellationToken cancellationToken = default)
    {
        ApplyTenantId();
        var auditEntries = new List<AuditLog>();

        Console.WriteLine("TenantId: " + _tenantContext.TenantId);
        Console.WriteLine("UserId: " + _tenantContext.UserId);

        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.Entity is AuditLog ||
                entry.State == EntityState.Detached ||
                entry.State == EntityState.Unchanged)
                continue;

            var audit = new AuditLog
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantContext.TenantId,
                UserId = _tenantContext.UserId == Guid.Empty 
                    ? null 
                    : _tenantContext.UserId,
                EntityName = entry.Entity.GetType().Name,
                EntityId = entry.Properties
                    .FirstOrDefault(p => p.Metadata.IsPrimaryKey())?
                    .CurrentValue?.ToString() ?? "",
                ActionType = entry.State.ToString(),
                CreatedAt = DateTime.UtcNow
            };

            if (entry.State == EntityState.Modified)
            {
                var oldValues = new Dictionary<string, object?>();
                var newValues = new Dictionary<string, object?>();

                foreach (var property in entry.Properties)
                {
                    if (property.IsModified)
                    {
                        oldValues[property.Metadata.Name] =
                            property.OriginalValue;

                        newValues[property.Metadata.Name] =
                            property.CurrentValue;
                    }
                }

                audit.OldValues =
                    System.Text.Json.JsonSerializer.Serialize(oldValues);

                audit.NewValues =
                    System.Text.Json.JsonSerializer.Serialize(newValues);
            }

            if (entry.State == EntityState.Added)
            {
                audit.NewValues =
                    System.Text.Json.JsonSerializer.Serialize(
                        entry.CurrentValues.ToObject());
            }

            if (entry.State == EntityState.Deleted)
            {
                audit.OldValues =
                    System.Text.Json.JsonSerializer.Serialize(
                        entry.OriginalValues.ToObject());
            }

            auditEntries.Add(audit);
        }

        foreach (var entry in ChangeTracker.Entries())
        {
            Console.WriteLine($"Entity: {entry.Entity.GetType().Name} | State: {entry.State}");
        }

        var result = await base.SaveChangesAsync(cancellationToken);

        return result;
    }

    private void ApplyTenantId()
    {
        var tenantId = _tenantContext.TenantId;

        if (tenantId == Guid.Empty)
        {
            _logger.LogWarning("ApplyTenantId: TenantId is empty");
            return;
        }

        var entries = ChangeTracker
            .Entries<ITenantEntity>()
            .Where(e => e.State == EntityState.Added);

        foreach (var entry in entries)
        {
            if (entry.Entity.TenantId == Guid.Empty)
            {
                entry.Entity.TenantId = tenantId;
            }
        }
    }
    }
