namespace Clienta.Api.Services;

public interface IAuthService
{
    Task<bool> InviteUserAsync(string email, Guid tenantId);
    Task<bool> SendInviteToExistingUserAsync(Guid userId, Guid tenantId);

    Task<bool> AcceptInviteAsync(
        string token,
        string password,
        string? fullName = null);

    Task<bool> RequestPasswordResetAsync(string email);

    Task<bool> ResetPasswordAsync(
        string token,
        string newPassword);
}