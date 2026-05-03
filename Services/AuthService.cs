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
    private readonly IPlanEnforcementService _planEnforcement;

    public AuthService(
        AppDbContext context,
        TokenService tokenService,
        IEmailService emailService,
        IConfiguration config,
        IPlanEnforcementService planEnforcement)
    {
        _context = context;
        _tokenService = tokenService;
        _emailService = emailService;
        _config = config;
        _planEnforcement = planEnforcement;
    }

    // =========================================
    // INVITE USER
    // =========================================

    public async Task<bool> InviteUserAsync(string email, Guid tenantId)
    {
        await _planEnforcement.EnsureUserLimitAsync(tenantId);

        var existingUser = await _context.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Email == email);

        if (existingUser != null)
            return false;

        var user = new User
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Email = email,
            IsActive = false,
            Role = "Staff",
            CreatedAt = DateTime.UtcNow
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        var rawToken = _tokenService.GenerateSecureToken();
        var tokenHash = _tokenService.HashToken(rawToken);

        var userToken = new UserToken
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = user.Id,
            TokenHash = tokenHash,
            Type = "Invite",
            ExpiryDate = DateTime.UtcNow.AddHours(24),
            IsUsed = false
        };

        _context.UserTokens.Add(userToken);
        await _context.SaveChangesAsync();

        var baseUrl = _config["App:BaseUrl"] ?? "http://localhost:5173";

        var inviteLink = $"{baseUrl}/account/accept-invite?token={rawToken}";

        await _emailService.SendInviteEmailAsync(email, inviteLink);

        return true;
    }

    // =========================================
    // ACCEPT INVITE
    // =========================================

    public async Task<bool> AcceptInviteAsync(string token, string password, string fullName)
    {
        var tokenHash = _tokenService.HashToken(token);

        var userToken = await _context.UserTokens
            .IgnoreQueryFilters()
            .Include(ut => ut.User)
            .FirstOrDefaultAsync(ut =>
                ut.TokenHash == tokenHash &&
                ut.Type == "Invite" &&
                !ut.IsUsed);

        if (userToken == null || userToken.ExpiryDate < DateTime.UtcNow)
            return false;

        var user = userToken.User;

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(password);
        user.FullName = fullName;
        user.IsActive = true;

        userToken.IsUsed = true;

        await _context.SaveChangesAsync();

        return true;
    }

    // =========================================
    // REQUEST PASSWORD RESET
    // =========================================

    public async Task<bool> RequestPasswordResetAsync(string email)
    {
        var user = await _context.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Email == email);

        if (user == null)
            return true;

        var rawToken = _tokenService.GenerateSecureToken();
        var tokenHash = _tokenService.HashToken(rawToken);

        var userToken = new UserToken
        {
            Id = Guid.NewGuid(),
            TenantId = user.TenantId,
            UserId = user.Id,
            TokenHash = tokenHash,
            Type = "ResetPassword",
            ExpiryDate = DateTime.UtcNow.AddMinutes(30),
            IsUsed = false
        };

        _context.UserTokens.Add(userToken);
        await _context.SaveChangesAsync();

        var baseUrl = _config["App:BaseUrl"] ?? "https://clienta.digitalpenpro.com";

        var encodedToken = Uri.EscapeDataString(rawToken);
        var resetLink = $"{baseUrl}/reset-password?token={encodedToken}";

        try
        {
            await _emailService.SendPasswordResetEmailAsync(email, resetLink);
        }
        catch (Exception ex)
        {
            Console.WriteLine("EMAIL ERROR: " + ex.Message);
            Console.WriteLine("RESET LINK: " + resetLink);
        }
        return true;
    }

    // =========================================
    // RESET PASSWORD
    // =========================================

    public async Task<bool> ResetPasswordAsync(string token, string newPassword)
    {
        var tokenHash = _tokenService.HashToken(token);

        Console.WriteLine("TOKEN HASH: " + tokenHash);
        var userToken = await _context.UserTokens
            //.IgnoreQueryFilters()
            .Include(ut => ut.User)
            .FirstOrDefaultAsync(ut =>
                ut.TokenHash == tokenHash &&
                ut.Type == "ResetPassword" &&
                !ut.IsUsed);

        if (userToken == null || userToken.ExpiryDate < DateTime.UtcNow)
            return false;

        userToken.User.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);

        userToken.IsUsed = true;

        await _context.SaveChangesAsync();

        return true;
    }
}