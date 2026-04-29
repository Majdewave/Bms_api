using Clienta.Api.Data;
using Clienta.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace Clienta.Api.Services;

public class InviteService
{
    private readonly AppDbContext _context;
    private readonly TokenService _tokenService;
    private readonly IEmailService _emailService;
    private readonly IConfiguration _config;

    public InviteService(
        AppDbContext context,
        TokenService tokenService,
        IEmailService emailService,
        IConfiguration config)
    {
        _context = context;
        _tokenService = tokenService;
        _emailService = emailService;
        _config = config;
    }

    public async Task<bool> InviteUserAsync(string email, Guid tenantId)
    {
        // Check user limit
        var tenant = await _context.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == tenantId);

        if (tenant == null)
            throw new InvalidOperationException("Tenant not found");

        if (tenant.UserLimit > 0) // -1 means unlimited
        {
            var currentUsers = await _context.Users
                .CountAsync(u => u.TenantId == tenantId);

            if (currentUsers >= tenant.UserLimit)
                throw new InvalidOperationException("User limit reached. Please upgrade your plan.");
        }

        // Check if user already exists
        var existingUser = await _context.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (existingUser != null)
            return false; // User already exists

        // Create new user
        var user = new User
        {
            Email = email,
            IsActive = false,
            Role = "Staff" // Default role for invited users
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        // Generate token
        var rawToken = _tokenService.GenerateSecureToken();
        var tokenHash = _tokenService.HashToken(rawToken);

        // Create user token
        var userToken = new UserToken
        {
            UserId = user.Id,
            TenantId = tenantId,
            TokenHash = tokenHash,
            Type = "Invite",
            ExpiryDate = DateTime.UtcNow.AddHours(24),
            IsUsed = false
        };

        _context.UserTokens.Add(userToken);
        await _context.SaveChangesAsync();

        // Send invite email
        var baseUrl = _config["App:BaseUrl"] ?? "https://yourdomain.com";
        var inviteLink = $"{baseUrl}/account/accept-invite?token={rawToken}";

        await _emailService.SendInviteEmailAsync(email, inviteLink);

        return true;
    }

    public async Task<bool> AcceptInviteAsync(string token, string password, string fullName, Guid tenantId)
    {
        var tokenHash = _tokenService.HashToken(token);

        var userToken = await _context.UserTokens
            .Include(ut => ut.User)
            .FirstOrDefaultAsync(ut => ut.TokenHash == tokenHash && ut.TenantId == tenantId && ut.Type == "Invite" && !ut.IsUsed);

        if (userToken == null || userToken.ExpiryDate < DateTime.UtcNow)
            return false; // Invalid or expired token

        var user = userToken.User;
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(password);
        user.FullName = fullName;
        user.IsActive = true;

        userToken.IsUsed = true;

        await _context.SaveChangesAsync();

        return true;
    }
}
