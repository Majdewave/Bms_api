using Microsoft.EntityFrameworkCore;
using Clienta.Api.Data;
using Clienta.Api.Entities;
using BCrypt.Net;

namespace Clienta.Api.Services;

public class AuthService : IAuthService
{
    private readonly AppDbContext _context;
    private readonly TokenService _tokenService;
    private readonly IEmailService _emailService;
    private readonly IConfiguration _config;
    private readonly ITenantContext? _tenant;
    private readonly IPlanEnforcementService _planEnforcement;

    public AuthService(
        AppDbContext context,
        TokenService tokenService,
        IEmailService emailService,
        IConfiguration config,
        IPlanEnforcementService planEnforcement,
        ITenantContext? tenant = null)
    {
        _context = context;
        _tokenService = tokenService;
        _emailService = emailService;
        _config = config;
        _tenant = tenant;
        _planEnforcement = planEnforcement;
    }

    // =====================================================
    // INVITE USER
    // =====================================================
    public async Task<bool> InviteUserAsync(string email, Guid tenantId)
    {
        // Check user limit via central enforcement
        await _planEnforcement.EnsureUserLimitAsync(tenantId);

        // Check if user already exists
        var existingUser = await _context.Users
            .FirstOrDefaultAsync(u => u.Email == email);
        
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

        // Generate secure token
        var rawToken = _tokenService.GenerateSecureToken();
        var tokenHash = _tokenService.HashToken(rawToken);

        // Create user token
        var userToken = new UserToken
        {
            TenantId = tenantId,
            UserId = user.Id,
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

    // =====================================================
    // ACCEPT INVITE
    // =====================================================
    public async Task<bool> AcceptInviteAsync(string token, string password, string fullName, Guid tenantId)
    {
        var tokenHash = _tokenService.HashToken(token);

        var userToken = await _context.UserTokens
            .Include(ut => ut.User)
            .FirstOrDefaultAsync(ut => ut.TokenHash == tokenHash
                                   && ut.TenantId == tenantId
                                   && ut.Type == "Invite"
                                   && !ut.IsUsed);

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

    // =====================================================
    // REQUEST PASSWORD RESET
    // =====================================================
    public async Task<bool> RequestPasswordResetAsync(string email)
    {
        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Email == email);

        // Don't reveal if user exists or not (security best practice)
        if (user == null)
            return true;

        // Check if there's an active reset token already
        var existingToken = await _context.UserTokens
            .FirstOrDefaultAsync(ut => ut.UserId == user.Id
                                   && ut.Type == "ResetPassword"
                                   && !ut.IsUsed
                                   && ut.ExpiryDate > DateTime.UtcNow);

        if (existingToken != null)
            return true; // Token already exists, don't send duplicate

        // Generate secure token
        var rawToken = _tokenService.GenerateSecureToken();
        var tokenHash = _tokenService.HashToken(rawToken);

        var userToken = new UserToken
        {
            TenantId = _tenant?.TenantId ?? Guid.Empty,
            UserId = user.Id,
            TokenHash = tokenHash,
            Type = "ResetPassword",
            ExpiryDate = DateTime.UtcNow.AddMinutes(30), // 30-minute expiry
            IsUsed = false
        };

        _context.UserTokens.Add(userToken);
        await _context.SaveChangesAsync();

        // Send reset email
        var baseUrl = _config["App:BaseUrl"] ?? "https://yourdomain.com";
        var resetLink = $"{baseUrl}/account/reset-password?token={rawToken}";

        await _emailService.SendPasswordResetEmailAsync(email, resetLink);

        return true;
    }

    // =====================================================
    // CONFIRM PASSWORD RESET
    // =====================================================
    public async Task<bool> ResetPasswordAsync(string token, string newPassword, Guid tenantId)
    {
        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 8)
            return false; // Password validation

        var tokenHash = _tokenService.HashToken(token);

        var userToken = await _context.UserTokens
            .Include(ut => ut.User)
            .FirstOrDefaultAsync(ut => ut.TokenHash == tokenHash
                                   && ut.TenantId == tenantId
                                   && ut.Type == "ResetPassword"
                                   && !ut.IsUsed);

        if (userToken == null || userToken.ExpiryDate < DateTime.UtcNow)
            return false; // Invalid or expired token

        var user = userToken.User;
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);

        userToken.IsUsed = true;

        await _context.SaveChangesAsync();

        return true;
    }
}
