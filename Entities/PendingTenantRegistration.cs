namespace Clienta.Api.Entities;

public class PendingTenantRegistration
{
    public Guid Id { get; set; }

    public string CompanyName { get; set; } = default!;
    public string Subdomain { get; set; } = default!;
    public string AdminEmail { get; set; } = default!;
    public string PasswordHash { get; set; } = default!;

    public string VerificationTokenHash { get; set; } = default!;
    public DateTime ExpiryDate { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
