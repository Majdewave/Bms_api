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

         user = await _context.Users
            .FirstOrDefaultAsync(u => u.Email == email);

        if (user == null)
            return true;

        //  כאן להוסיף — לפני יצירת token
        var oldTokens = await _context.UserTokens
            .Where(ut => ut.UserId == user.Id &&
                         ut.Type == "ResetPassword" &&
                         !ut.IsUsed)
            .ToListAsync();

        foreach (var t in oldTokens)
        {
            t.IsUsed = true;
        }

        // עכשיו ממשיכים כרגיל
        var rawToken = _tokenService.GenerateSecureToken();
        var tokenHash = _tokenService.HashToken(rawToken);

        var userToken = new UserToken
        {
            UserId = user.Id,
            TenantId = user.TenantId,
            TokenHash = tokenHash,
            Type = "ResetPassword",
            ExpiryDate = DateTime.UtcNow.AddMinutes(30),
            IsUsed = false
        };

        _context.UserTokens.Add(userToken);
        await _context.SaveChangesAsync();

        // Generate new token
         rawToken = _tokenService.GenerateSecureToken();
         tokenHash = _tokenService.HashToken(rawToken);

         userToken = new UserToken
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

    public async Task<bool> ResetPasswordAsync(string token, string newPassword)
    {
        Console.WriteLine("FORGOT PASSWORD START");

        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 8)
            return false; // Password validation

        var tokenHash = _tokenService.HashToken(token);
        Console.WriteLine("HASH: " + tokenHash);

        var userToken = await _context.UserTokens
            .Include(ut => ut.User)
            .FirstOrDefaultAsync(ut =>
                ut.TokenHash == tokenHash &&
                ut.Type == "ResetPassword" &&
                !ut.IsUsed &&
                ut.ExpiryDate > DateTime.UtcNow);

        if (userToken == null)
            return false;

        var user = userToken.User;
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);

        userToken.IsUsed = true;

        await _context.SaveChangesAsync();

        return true;
    }
}
