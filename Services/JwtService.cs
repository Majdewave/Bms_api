using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Clienta.Api.Data;
using Clienta.Api.Entities;

namespace Clienta.Api.Services;

public class JwtService
{
    private readonly IConfiguration _config;
    private readonly ILogger<JwtService> _logger;
    private readonly AppDbContext _db;

    public JwtService(IConfiguration config, ILogger<JwtService> logger, AppDbContext db)
    {
        _config = config;
        _logger = logger;
        _db = db;
    }

    public string GenerateToken(User user, Guid tenantId)
    {
        var jwtSettings = _config.GetSection("JwtSettings");

        // Log user details for debugging
        _logger.LogInformation("=== JWT Generation Debug ===");
        _logger.LogInformation("User ID: {UserId}", user.Id);
        _logger.LogInformation("User Email: {Email}", user.Email);
        _logger.LogInformation("User Role from Database: '{Role}'", user.Role);

        // Role claim MUST match exactly what [Authorize(Roles="...")] expects
        // Normalize admin role to "Admin" (capital A)
        var roleValue = string.Equals(user.Role, "admin", StringComparison.OrdinalIgnoreCase)
            ? "Admin"
            : user.Role.Trim();

        var claims = new List<Claim>
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Role, roleValue),
            new Claim("tenant_id", tenantId.ToString())
        };

        // Add permission claims for staff
        if (roleValue == "Staff")
        {
            var permissions = _db.UserPermissions
                .Where(up => up.UserId == user.Id)
                .Select(up => up.Permission.Key)
                .ToList();
            foreach (var permission in permissions)
            {
                claims.Add(new Claim("permission", permission));
            }
        }

        // Log all claims being added to token
        _logger.LogInformation("Claims added to JWT:");
        foreach (var claim in claims)
        {
            _logger.LogInformation("  - Type: '{Type}', Value: '{Value}'", claim.Type, claim.Value);
        }

        var key = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(jwtSettings["Key"]!)
        );

        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: jwtSettings["Issuer"],
            audience: jwtSettings["Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(
                int.Parse(jwtSettings["ExpiresInMinutes"]!)
            ),
            signingCredentials: creds
        );

        var tokenString = new JwtSecurityTokenHandler().WriteToken(token);
        _logger.LogInformation("JWT Generated Successfully");
        _logger.LogInformation("============================");
        
        return tokenString;
    }
}
