using Clienta.Api.Data;
using Clienta.Api.Entities;
using Clienta.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Clienta.Api.Services;

public class OnboardingService : IOnboardingService
{
    private readonly AppDbContext _db;
    private readonly TokenService _tokenService;
    private readonly IEmailService _emailService;
    private readonly ITenantSeedService _tenantSeedService;

    public OnboardingService(
        AppDbContext db,
        TokenService tokenService,
        IEmailService emailService,
        ITenantSeedService tenantSeedService)
    {
        _db = db;
        _tokenService = tokenService;
        _emailService = emailService;
        _tenantSeedService = tenantSeedService;
    }

    public async Task<string> CreateTenantAsync(
        string companyName,
        string subdomain,
        string adminEmail,
        string password)
    {
        // Validate subdomain is unique
        if (await _db.Tenants.AnyAsync(t => t.Subdomain == subdomain))
            throw new InvalidOperationException("Subdomain already taken");

        // Check if pending registration exists
        if (await _db.PendingTenantRegistrations.AnyAsync(p => p.Subdomain == subdomain))
            throw new InvalidOperationException("Subdomain already reserved");

        // Generate verification token
        var verificationToken = _tokenService.GenerateSecureToken();
        var tokenHash = _tokenService.HashToken(verificationToken);

        // Create pending registration
        var pending = new PendingTenantRegistration
        {
            Id = Guid.NewGuid(),
            CompanyName = companyName,
            Subdomain = subdomain.ToLower(),
            AdminEmail = adminEmail,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            VerificationTokenHash = tokenHash,
            ExpiryDate = DateTime.UtcNow.AddHours(24),
            CreatedAt = DateTime.UtcNow
        };

        _db.PendingTenantRegistrations.Add(pending);
        await _db.SaveChangesAsync();

        // TODO: Send verification email (disabled for development)
        // var verificationUrl = $"https://yourapp.com/verify?token={verificationToken}";
        // await _emailService.SendEmailAsync(
        //     adminEmail,
        //     "Verify your email",
        //     $"Please verify your email by clicking: {verificationUrl}");

        // For development: auto-verify immediately
        return await VerifyEmailAsync(verificationToken);
    }

    public async Task<string> VerifyEmailAsync(string token)
    {
        var tokenHash = _tokenService.HashToken(token);

        var pending = await _db.PendingTenantRegistrations
            .FirstOrDefaultAsync(p => p.VerificationTokenHash == tokenHash);

        if (pending == null)
            throw new InvalidOperationException("Invalid verification token");

        if (pending.ExpiryDate < DateTime.UtcNow)
            throw new InvalidOperationException("Verification token expired");

        // Use transaction for atomic operation
        using var transaction = await _db.Database.BeginTransactionAsync();

        try
        {
            // Ensure Subdomain is set and safe
            var safeSubdomain = string.IsNullOrWhiteSpace(pending.Subdomain)
                ? (pending.CompanyName ?? "company").ToLower().Replace(" ", "")
                : pending.Subdomain.ToLower();

            var tenant = new Tenant
            {
                Id = Guid.NewGuid(),
                Name = pending?.CompanyName ?? string.Empty,
                Subdomain = safeSubdomain,
                Plan = PlanType.Trial,
                BillingCycle = BillingCycle.Monthly,
                SubscriptionStatus = SubscriptionStatus.Trialing,
                TrialEndsAt = DateTime.UtcNow.AddDays(14),
                UserLimit = 10,
                MessageLimit = 100,
                IsSuspended = false,
                CreatedAt = DateTime.UtcNow
            };

            _db.Tenants.Add(tenant);
            await _db.SaveChangesAsync();

            // Create admin user
            var adminUser = new User
            {
                Id = Guid.NewGuid(),
                TenantId = tenant.Id,
                Email = pending.AdminEmail ?? throw new InvalidOperationException("AdminEmail is required for admin user creation."),
                PasswordHash = pending.PasswordHash ?? throw new InvalidOperationException("PasswordHash is required for admin user creation."),
                Role = "Admin",
                FullName = "Admin",
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            _db.Users.Add(adminUser);
            await _db.SaveChangesAsync();

            // Create BusinessUser mapping
            var businessUser = new BusinessUser
            {
                Id = Guid.NewGuid(),
                TenantId = tenant.Id,
                UserId = adminUser.Id
            };

            _db.BusinessUsers.Add(businessUser);
            await _db.SaveChangesAsync();

            // Seed advanced demo data for the new tenant
            await _tenantSeedService.SeedAdvancedAsync(tenant.Id, adminUser.Id);

            // Delete pending registration
            _db.PendingTenantRegistrations.Remove(pending);
            await _db.SaveChangesAsync();

            await transaction.CommitAsync();

            return $"{pending.Subdomain}.yourapp.com";
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }
}
