
namespace Clienta.Api.Entities;

public class User
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public Guid TenantId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string Role { get; set; } = string.Empty;

    public string? FullName { get; set; }
    public string? Phone { get; set; }
    public string? LicenseNumber { get; set; }
    public string? RoleLabel { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public string? StampUrl { get; set; }
    public bool UseStamp { get; set; } = false;
    public List<UserPermission> Permissions { get; set; } = new();
}
