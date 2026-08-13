// ...existing code...
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Clienta.Api.Entities;
using Clienta.Api.Services;
using Clienta.Api.Services.WhatsApp;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Clienta.Api.Data;

public class AppDbContext : DbContext
{
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<AppDbContext> _logger;

    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();

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
    public DbSet<PlatformUser> PlatformUsers => Set<PlatformUser>();
    public DbSet<PlatformSettings> PlatformSettings => Set<PlatformSettings>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<UserPermission> UserPermissions => Set<UserPermission>();
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<BusinessUser> BusinessUsers => Set<BusinessUser>();
    public DbSet<Client> Clients => Set<Client>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<DepartmentFeature> DepartmentFeatures => Set<DepartmentFeature>();
    public DbSet<StaffDepartment> StaffDepartments => Set<StaffDepartment>();
    public DbSet<Appointment> Appointments => Set<Appointment>();
    public DbSet<Note> Notes => Set<Note>();
    public DbSet<ClientFile> ClientFiles => Set<ClientFile>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<UserToken> UserTokens => Set<UserToken>();
    public DbSet<PendingTenantRegistration> PendingTenantRegistrations => Set<PendingTenantRegistration>();
    public DbSet<ProcessedStripeEvent> ProcessedStripeEvents => Set<ProcessedStripeEvent>();
    public DbSet<Service> Services => Set<Service>();
    public DbSet<TenantFeatures> TenantFeatures => Set<TenantFeatures>();
    public DbSet<QueueDisplaySettings> QueueDisplaySettings => Set<QueueDisplaySettings>();
    public DbSet<QueueDisplayAdvertisementImage> QueueDisplayAdvertisementImages => Set<QueueDisplayAdvertisementImage>();
    public DbSet<Invoice> Invoices { get; set; } = null!;
    public DbSet<InvoiceLineItem> InvoiceLineItems { get; set; } = null!;
    public DbSet<Quote> Quotes { get; set; } = null!;
    public DbSet<QuoteLineItem> QuoteLineItems { get; set; } = null!;
    public DbSet<Prescription> Prescriptions { get; set; } = null!;
    public DbSet<ClientConsent> ClientConsents { get; set; } = null!;
    public DbSet<ClientTreatmentPhoto> ClientTreatmentPhotos { get; set; } = null!;
    public DbSet<ConsentTemplate> ConsentTemplates { get; set; } = null!;
    public DbSet<Drug> Drugs { get; set; } = null!;
    public DbSet<Business> Businesses { get; set; } = null!;
    // Removed Staffs DbSet
    public DbSet<VisitSummary> VisitSummaries { get; set; } = null!;
    public DbSet<WhatsAppSettings> WhatsAppSettings { get; set; } = null!;
    public DbSet<WhatsAppTemplate> WhatsAppTemplates { get; set; } = null!;
    public DbSet<WhatsAppMessage> WhatsAppMessages { get; set; } = null!;
    public DbSet<WhatsAppOAuthState> WhatsAppOAuthStates { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Appointment>()
            .HasOne(a => a.Staff)
            .WithMany()
            .HasForeignKey(a => a.StaffId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Appointment>()
            .HasOne(a => a.Department)
            .WithMany()
            .HasForeignKey(a => a.DepartmentId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<VisitSummary>()
            .HasOne(v => v.Appointment)
            .WithMany()
            .HasForeignKey(v => v.AppointmentId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<VisitSummary>()
            .HasIndex(v => v.AppointmentId);

        modelBuilder.Entity<Tenant>()
            .HasOne(t => t.OwnerUser)
            .WithMany()
            .HasForeignKey(t => t.OwnerUserId)
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


        // Tenant isolation for Drugs
        modelBuilder.Entity<Drug>()
            .HasQueryFilter(d => _tenantContext.TenantId == Guid.Empty || d.TenantId == _tenantContext.TenantId);

        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<UserPermission>()
            .HasOne(p => p.User)
            .WithMany(u => u.Permissions)
            .HasForeignKey(p => p.UserId);

        modelBuilder.Entity<PlatformUser>()
            .Property(pu => pu.Role)
            .HasConversion<string>();

        modelBuilder.Entity<PlatformUser>()
            .HasIndex(pu => pu.Email)
            .IsUnique();

        modelBuilder.Entity<PlatformSettings>()
            .HasKey(settings => settings.Id);

        // Global Tenant Filters (CRITICAL for shared database multi-tenancy)
        modelBuilder.Entity<User>()
            .HasQueryFilter(u => _tenantContext.TenantId == Guid.Empty || u.TenantId == _tenantContext.TenantId);

        modelBuilder.Entity<Client>()
            .HasQueryFilter(c => _tenantContext.TenantId == Guid.Empty || c.TenantId == _tenantContext.TenantId);

        modelBuilder.Entity<Department>()
            .HasQueryFilter(d => _tenantContext.TenantId == Guid.Empty || d.TenantId == _tenantContext.TenantId);

        modelBuilder.Entity<DepartmentFeature>()
            .HasQueryFilter(df => _tenantContext.TenantId == Guid.Empty || df.Department.TenantId == _tenantContext.TenantId);

        modelBuilder.Entity<Service>()
            .HasQueryFilter(s => _tenantContext.TenantId == Guid.Empty || s.TenantId == _tenantContext.TenantId);

        modelBuilder.Entity<StaffDepartment>()
            .HasQueryFilter(sd => _tenantContext.TenantId == Guid.Empty || sd.TenantId == _tenantContext.TenantId);

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

        modelBuilder.Entity<QueueDisplaySettings>()
            .HasQueryFilter(qds => _tenantContext.TenantId == Guid.Empty || qds.TenantId == _tenantContext.TenantId);

        modelBuilder.Entity<QueueDisplayAdvertisementImage>()
            .HasQueryFilter(ad => _tenantContext.TenantId == Guid.Empty || ad.TenantId == _tenantContext.TenantId);

        modelBuilder.Entity<UserToken>()
            .HasQueryFilter(ut => _tenantContext.TenantId == Guid.Empty || ut.TenantId == _tenantContext.TenantId);

        // Tenant unique index on Subdomain
        modelBuilder.Entity<Tenant>()
            .HasIndex(t => t.Subdomain)
            .IsUnique();

        modelBuilder.Entity<Department>()
            .HasIndex(d => new { d.TenantId, d.Name })
            .IsUnique();

        modelBuilder.Entity<Client>()
            .HasIndex(c => new { c.TenantId, c.IdNumber })
            .IsUnique()
            .HasFilter("\"IdNumber\" IS NOT NULL AND btrim(\"IdNumber\") <> ''");

        modelBuilder.Entity<Department>()
            .HasOne(d => d.Tenant)
            .WithMany(t => t.Departments)
            .HasForeignKey(d => d.TenantId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<DepartmentFeature>()
            .HasIndex(df => new { df.DepartmentId, df.FeatureKey })
            .IsUnique();

        modelBuilder.Entity<DepartmentFeature>()
            .HasOne(df => df.Department)
            .WithMany(d => d.Features)
            .HasForeignKey(df => df.DepartmentId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Appointment>()
            .HasOne(a => a.Department)
            .WithMany()
            .HasForeignKey(a => a.DepartmentId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<Service>()
            .HasOne(s => s.Department)
            .WithMany()
            .HasForeignKey(s => s.DepartmentId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<StaffDepartment>()
            .HasIndex(sd => new { sd.TenantId, sd.StaffId, sd.DepartmentId })
            .IsUnique();

        modelBuilder.Entity<StaffDepartment>()
            .HasOne(sd => sd.Tenant)
            .WithMany()
            .HasForeignKey(sd => sd.TenantId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<StaffDepartment>()
            .HasOne(sd => sd.Staff)
            .WithMany(bu => bu.StaffDepartments)
            .HasForeignKey(sd => sd.StaffId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<StaffDepartment>()
            .HasOne(sd => sd.Department)
            .WithMany(d => d.StaffDepartments)
            .HasForeignKey(sd => sd.DepartmentId)
            .OnDelete(DeleteBehavior.Cascade);

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
            .Property(tf => tf.QuotesEnabled)
            .HasDefaultValue(false);

        modelBuilder.Entity<TenantFeatures>()
            .Property(tf => tf.PrescriptionsEnabled)
            .HasDefaultValue(false);

        modelBuilder.Entity<TenantFeatures>()
            .Property(tf => tf.BeforeAfterPhotosEnabled)
            .HasDefaultValue(true);

        modelBuilder.Entity<TenantFeatures>()
            .Property(tf => tf.TeamChatEnabled)
            .HasDefaultValue(false);

        modelBuilder.Entity<TenantFeatures>()
            .Property(tf => tf.QueueDisplayEnabled)
            .HasDefaultValue(false);

        modelBuilder.Entity<QueueDisplaySettings>()
            .HasIndex(qds => qds.TenantId)
            .IsUnique();

        modelBuilder.Entity<QueueDisplaySettings>()
            .HasIndex(qds => qds.PublicToken)
            .IsUnique();

        modelBuilder.Entity<QueueDisplaySettings>()
            .Property(qds => qds.PrivacyMode)
            .HasDefaultValue(QueueDisplayPrivacyMode.FullName);

        modelBuilder.Entity<QueueDisplaySettings>()
            .Property(qds => qds.Theme)
            .HasDefaultValue(QueueDisplayTheme.Default);

        modelBuilder.Entity<QueueDisplaySettings>()
            .Property(qds => qds.AdvertisementType)
            .HasDefaultValue(QueueDisplayAdvertisementType.Image);

        modelBuilder.Entity<QueueDisplaySettings>()
            .HasOne(qds => qds.Tenant)
            .WithMany()
            .HasForeignKey(qds => qds.TenantId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<QueueDisplayAdvertisementImage>()
            .HasIndex(ad => new { ad.TenantId, ad.QueueDisplaySettingsId, ad.DisplayOrder });

        modelBuilder.Entity<QueueDisplayAdvertisementImage>()
            .HasOne(ad => ad.QueueDisplaySettings)
            .WithMany(qds => qds.AdvertisementImages)
            .HasForeignKey(ad => ad.QueueDisplaySettingsId)
            .OnDelete(DeleteBehavior.Cascade);

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

        modelBuilder.Entity<Appointment>()
         .HasIndex(a => new { a.TenantId, a.Status, a.StartTime })
         .HasDatabaseName("IX_Appointments_Tenant_Status_StartTime");

        modelBuilder.Entity<Appointment>()
            .Property(a => a.AppointmentDate)
            .HasColumnType("date");

        modelBuilder.Entity<Appointment>()
            .HasIndex(a => new { a.TenantId, a.AppointmentDate, a.QueueNumber })
            .HasDatabaseName("IX_Appointments_Tenant_AppointmentDate_Status_QueueNumber")
            .HasFilter("\"QueueNumber\" IS NOT NULL AND \"Status\" IN ('Scheduled','Waiting','InProgress')")
            .IsUnique();

        modelBuilder.Entity<ClientConsent>()
            .HasIndex(c => c.AppointmentId)
            .HasDatabaseName("IX_ClientConsents_AppointmentId");

        modelBuilder.Entity<Invoice>()
            .HasQueryFilter(invoice => _tenantContext.TenantId == Guid.Empty || invoice.TenantId == _tenantContext.TenantId);

        modelBuilder.Entity<Invoice>()
            .HasIndex(invoice => new { invoice.TenantId, invoice.InvoiceNumber })
            .IsUnique();

        modelBuilder.Entity<Invoice>()
            .HasMany(i => i.LineItems)
            .WithOne()
            .HasForeignKey(li => li.InvoiceId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Quote>()
            .HasQueryFilter(quote => _tenantContext.TenantId == Guid.Empty || quote.TenantId == _tenantContext.TenantId);

        modelBuilder.Entity<Quote>()
            .HasIndex(quote => new { quote.TenantId, quote.QuoteNumber })
            .IsUnique();

        modelBuilder.Entity<Quote>()
            .HasMany(q => q.LineItems)
            .WithOne()
            .HasForeignKey(li => li.QuoteId)
            .OnDelete(DeleteBehavior.Cascade);

        var whatsAppAccessTokenConverter = new ValueConverter<string?, string?>(
            value => WhatsAppTokenProtector.Encrypt(value),
            value => WhatsAppTokenProtector.Decrypt(value));

        modelBuilder.Entity<WhatsAppSettings>()
            .Property(s => s.AccessToken)
            .HasConversion(whatsAppAccessTokenConverter);

        modelBuilder.Entity<WhatsAppSettings>()
            .HasIndex(s => s.TenantId)
            .IsUnique();

        modelBuilder.Entity<WhatsAppSettings>()
            .HasIndex(s => new { s.ConnectionStatus, s.TokenExpiresAt });

        modelBuilder.Entity<WhatsAppTemplate>()
            .HasIndex(t => new { t.TenantId, t.MetaTemplateName })
            .IsUnique();

        modelBuilder.Entity<WhatsAppMessage>()
            .HasIndex(m => new { m.TenantId, m.CreatedAt });

        modelBuilder.Entity<WhatsAppOAuthState>()
            .HasIndex(s => new { s.TenantId, s.NonceHash })
            .IsUnique();

        modelBuilder.Entity<WhatsAppOAuthState>()
            .HasIndex(s => new { s.TenantId, s.ExpiresAt });

        modelBuilder.Entity<WhatsAppOAuthState>()
            .HasIndex(s => new { s.TenantId, s.UsedAt });

        modelBuilder.Entity<WhatsAppSettings>()
            .HasQueryFilter(s => _tenantContext.TenantId == Guid.Empty || s.TenantId == _tenantContext.TenantId);

        modelBuilder.Entity<WhatsAppTemplate>()
            .HasQueryFilter(t => _tenantContext.TenantId == Guid.Empty || t.TenantId == _tenantContext.TenantId);

        modelBuilder.Entity<WhatsAppMessage>()
            .HasQueryFilter(m => _tenantContext.TenantId == Guid.Empty || m.TenantId == _tenantContext.TenantId);

        modelBuilder.Entity<WhatsAppOAuthState>()
            .HasQueryFilter(s => _tenantContext.TenantId == Guid.Empty || s.TenantId == _tenantContext.TenantId);

        modelBuilder.Entity<AuditLog>()
            .HasOne<User>()
            .WithMany()
            .HasForeignKey(a => a.UserId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<Drug>().HasData(
            new Drug
            {
                Id = new Guid("a1000000-0000-0000-0000-000000000001"),
                Name = "BORIC ACID",
                Dosage = "600 MG",
                TenantId = new Guid("40aff269-58db-4d97-b391-ccbb701cd458"), // 🔥 חובה
                CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            new Drug
            {
                Id = new Guid("a1000000-0000-0000-0000-000000000002"),
                Name = "Ibuprofen",
                Dosage = "200mg",
                TenantId = new Guid("40aff269-58db-4d97-b391-ccbb701cd458"),
                CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            new Drug
            {
                Id = new Guid("a1000000-0000-0000-0000-000000000003"),
                Name = "Amoxicillin",
                Dosage = "500mg",
                TenantId = new Guid("40aff269-58db-4d97-b391-ccbb701cd458"),
                CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            new Drug
            {
                Id = new Guid("a1000000-0000-0000-0000-000000000004"),
                Name = "Paracetamol",
                Dosage = "500mg",
                TenantId = new Guid("40aff269-58db-4d97-b391-ccbb701cd458"),
                CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            }
        );

        modelBuilder.Entity<User>()
            .HasIndex(u => new { u.Email, u.TenantId })
            .IsUnique();
    }

    public override async Task<int> SaveChangesAsync(
        CancellationToken cancellationToken = default)
    {
        ApplyTenantId();
        SynchronizeDerivedAppointmentDate();
        var auditEntries = new List<AuditLog>();

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

        var result = await base.SaveChangesAsync(cancellationToken);

        return result;
    }

    private void ApplyTenantId()
    {
        var tenantId = _tenantContext.TenantId;

        if (tenantId == Guid.Empty)
        {
            return;
        }

        var entries = ChangeTracker
            .Entries<ITenantEntity>()
            .Where(e => e.State == EntityState.Added && e.Entity.TenantId == Guid.Empty);

        foreach (var entry in entries)
        {
            if (entry.Entity.TenantId == Guid.Empty)
            {
                entry.Entity.TenantId = tenantId;
            }
        }
    }

    private void SynchronizeDerivedAppointmentDate()
    {
        // AppointmentDate is a derived persistence field, never a client/business input.
        // Keep it self-healed from StartTime for every added/updated appointment.
        var appointmentEntries = ChangeTracker.Entries<Appointment>()
            .Where(e => e.State == EntityState.Added || e.State == EntityState.Modified);

        foreach (var entry in appointmentEntries)
        {
            var normalizedStartTime = entry.Entity.StartTime.Kind switch
            {
                DateTimeKind.Utc => entry.Entity.StartTime,
                DateTimeKind.Local => entry.Entity.StartTime.ToUniversalTime(),
                _ => DateTime.SpecifyKind(entry.Entity.StartTime, DateTimeKind.Utc)
            };

            if (entry.Entity.StartTime != normalizedStartTime)
            {
                entry.Entity.StartTime = normalizedStartTime;
            }

            var derivedDate = new DateTime(
                normalizedStartTime.Year,
                normalizedStartTime.Month,
                normalizedStartTime.Day,
                0,
                0,
                0,
                DateTimeKind.Utc);

            if (entry.Entity.AppointmentDate != derivedDate)
            {
                entry.Entity.AppointmentDate = derivedDate;
            }
        }
    }
    }
