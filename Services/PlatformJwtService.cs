using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Clienta.Api.Entities;
using Microsoft.IdentityModel.Tokens;

namespace Clienta.Api.Services;

public class PlatformJwtService
{
    private readonly IConfiguration _configuration;

    public PlatformJwtService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public string GenerateToken(PlatformUser user)
    {
        var settings = _configuration.GetSection("PlatformJwtSettings");
        var key = settings["Key"] ?? throw new InvalidOperationException("PlatformJwtSettings:Key is missing");
        var issuer = settings["Issuer"] ?? throw new InvalidOperationException("PlatformJwtSettings:Issuer is missing");
        var audience = settings["Audience"] ?? throw new InvalidOperationException("PlatformJwtSettings:Audience is missing");

        var expiresInMinutes = int.TryParse(settings["ExpiresInMinutes"], out var parsed) ? parsed : 60;

        var claims = new List<Claim>
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Role, user.Role.ToString()),
            new Claim("token_scope", "platform")
        };

        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));
        var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(expiresInMinutes),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
