using Clienta.Api.Data;
using Clienta.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace Clienta.Api.Services;

public class PasswordResetService
{
    private readonly AppDbContext _context;
    private readonly TokenService _tokenService;
    private readonly IEmailService _emailService;
    private readonly IConfiguration _config;

    public PasswordResetService(
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

        // Generate new token
        var rawToken = _tokenService.GenerateSecureToken();
        var tokenHash = _tokenService.HashToken(rawToken);

        var userToken = new UserToken
        {
            UserId = user.Id,
            TenantId = user.TenantId,
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
