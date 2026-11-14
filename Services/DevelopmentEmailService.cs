namespace Clienta.Api.Services;

/// <summary>
/// Suppresses outbound email in local Development environments.
/// </summary>
public sealed class DevelopmentEmailService : IEmailService
{
    private readonly ILogger<DevelopmentEmailService> _logger;

    public DevelopmentEmailService(ILogger<DevelopmentEmailService> logger)
    {
        _logger = logger;
    }

    public Task SendInviteEmailAsync(string email, string inviteLink) => SuppressAsync("invite");

    public Task SendPasswordResetEmailAsync(string email, string resetLink) => SuppressAsync("password reset");

    public Task SendEmailAsync(string email, string subject, string body) => SuppressAsync("email");

    public Task SendTrialReminderAsync(string email, string companyName, string subdomain, int daysLeft, Guid tenantId) =>
        SuppressAsync("trial reminder");

    public Task SendTrialExpiredAsync(string email, string companyName, string subdomain, Guid tenantId) =>
        SuppressAsync("trial expiry");

    private Task SuppressAsync(string operation)
    {
        _logger.LogInformation("Suppressed {Operation} email in Development; no external message was sent.", operation);
        return Task.CompletedTask;
    }
}