namespace Clienta.Api.Services;

public interface IAuthService
{
    Task<bool> InviteUserAsync(string email, Guid tenantId);
    Task<bool> AcceptInviteAsync(string token, string password, string fullName, Guid tenantId);
    Task<bool> RequestPasswordResetAsync(string email);
    Task<bool> ResetPasswordAsync(string token, string newPassword, Guid tenantId);
}
