namespace Clienta.Api.Services;

public interface IOnboardingService
{
    Task<string> CreateTenantAsync(
        string companyName,
        string subdomain,
        string adminEmail,
        string password);

    Task<string> VerifyEmailAsync(string token);
}
