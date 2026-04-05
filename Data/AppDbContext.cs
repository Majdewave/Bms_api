using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Clienta.Api.Entities;
using Clienta.Api.Services;
using Microsoft.Extensions.Logging;
using System.Text.Json;

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
    public DbSet<TenantFeatures> TenantFeatures => Set<TenantFeatures>();
    public DbSet<Invoice> Invoices { get; set; } = null!;
    public DbSet<InvoiceLineItem> InvoiceLineItems { get; set; } = null!;
    public DbSet<Prescription> Prescriptions { get; set; } = null!;
    public DbSet<ClientConsent> ClientConsents { get; set; } = null!;
    public DbSet<ClientTreatmentPhoto> ClientTreatmentPhotos { get; set; } = null!;
    public DbSet<ConsentTemplate> ConsentTemplates { get; set; } = null!;
    public DbSet<Drug> Drugs { get; set; } = null!;
    public DbSet<Business> Businesses { get; set; } = null!;
    // Removed Staffs DbSet

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Appointment>()
            .HasOne(a => a.Staff)
            .WithMany()
            .HasForeignKey(a => a.StaffId)
            .OnDelete(DeleteBehavior.Restrict);

        var drugsComparer = new ValueComparer<List<string>>(
            (left, right) =>
                (left ?? new List<string>()).SequenceEqual(right ?? new List<string>()),
            list => (list ?? new List<string>())
                .Aggregate(0, (hash, item) => HashCode.Combine(hash, item)),
            list => (list ?? new List<string>()).ToList());

        modelBuilder.Entity<Prescription>()
            .Property(p => p.Drugs)
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>()
            )
            .Metadata.SetValueComparer(drugsComparer);

        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<UserPermission>()
            .HasOne(p => p.User)
            .WithMany(u => u.Permissions)
            .HasForeignKey(p => p.UserId);

        // Global Tenant Filters (CRITICAL for shared database multi-tenancy)
        modelBuilder.Entity<User>()
            .HasQueryFilter(u => _tenantContext.TenantId == Guid.Empty || u.TenantId == _tenantContext.TenantId);

        modelBuilder.Entity<Client>()
            .HasQueryFilter(c => _tenantContext.TenantId == Guid.Empty || c.TenantId == _tenantContext.TenantId);

        modelBuilder.Entity<Appointment>()
            .HasQueryFilter(a => _tenantContext.TenantId == Guid.Empty || a.TenantId == _tenantContext.TenantId);

        modelBuilder.Entity<Note>()
            .HasQueryFilter(n => _tenantContext.TenantId == Guid.Empty || n.TenantId == _tenantContext.TenantId);

        modelBuilder.Entity<ClientConsent>()
            .HasQueryFilter(c => _tenantContext.TenantId == Guid.Empty || c.TenantId == _tenantContext.TenantId);

        modelBuilder.Entity<ClientTreatmentPhoto>()
            .HasQueryFilter(p => _tenantContext.TenantId == Guid.Empty || p.TenantId == _tenantContext.TenantId);

        modelBuilder.Entity<ConsentTemplate>()
            .HasQueryFilter(t => _tenantContext.TenantId == Guid.Empty || t.TenantId == _tenantContext.TenantId);

        modelBuilder.Entity<ClientFile>()
            .HasQueryFilter(f => _tenantContext.TenantId == Guid.Empty || f.TenantId == _tenantContext.TenantId);

        modelBuilder.Entity<TenantFeatures>()
            .HasQueryFilter(tf => _tenantContext.TenantId == Guid.Empty || tf.TenantId == _tenantContext.TenantId);

        modelBuilder.Entity<UserToken>()
            .HasQueryFilter(ut => _tenantContext.TenantId == Guid.Empty || ut.TenantId == _tenantContext.TenantId);

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

        modelBuilder.Entity<TenantFeatures>()
            .HasIndex(tf => tf.TenantId)
            .IsUnique();

        modelBuilder.Entity<TenantFeatures>()
            .Property(tf => tf.ReportsEnabled)
            .HasDefaultValue(true);

        modelBuilder.Entity<TenantFeatures>()
            .Property(tf => tf.InvoicesEnabled)
            .HasDefaultValue(true);

        modelBuilder.Entity<TenantFeatures>()
            .Property(tf => tf.PrescriptionsEnabled)
            .HasDefaultValue(false);

        modelBuilder.Entity<TenantFeatures>()
            .Property(tf => tf.BeforeAfterPhotosEnabled)
            .HasDefaultValue(true);

        modelBuilder.Entity<ClientTreatmentPhoto>()
            .HasOne(p => p.Client)
            .WithMany()
            .HasForeignKey(p => p.ClientId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<TenantFeatures>()
            .HasOne(tf => tf.Tenant)
            .WithMany()
            .HasForeignKey(tf => tf.TenantId)
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

        modelBuilder.Entity<Invoice>()
            .HasMany(i => i.LineItems)
            .WithOne()
            .HasForeignKey(li => li.InvoiceId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<AuditLog>()
            .HasOne<User>()
            .WithMany()
            .HasForeignKey(a => a.UserId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<Drug>().HasData(
            new Drug { Id = new Guid("a1000000-0000-0000-0000-000000000001"), Name = "BORIC ACID", Dosage = "600 MG", CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
            new Drug { Id = new Guid("a1000000-0000-0000-0000-000000000002"), Name = "Ibuprofen", Dosage = "200mg", CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
            new Drug { Id = new Guid("a1000000-0000-0000-0000-000000000003"), Name = "Amoxicillin", Dosage = "500mg", CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
            new Drug { Id = new Guid("a1000000-0000-0000-0000-000000000004"), Name = "Paracetamol", Dosage = "500mg", CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) }
        );
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
