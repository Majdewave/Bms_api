namespace Clienta.Api.Services;

public interface IEmailService
{
    Task SendInviteEmailAsync(string email, string inviteLink);
    Task SendPasswordResetEmailAsync(string email, string resetLink);
    Task SendEmailAsync(string email, string subject, string body);
    Task SendTrialReminderAsync(string email, string companyName, string subdomain, int daysLeft);
    Task SendTrialExpiredAsync(string email, string companyName, string subdomain);
}

public class SmtpEmailService : IEmailService
{
    private readonly string _smtpHost;
    private readonly int _smtpPort;
    private readonly string _smtpUsername;
    private readonly string _smtpPassword;
    private readonly string _fromEmail;
    private readonly string _fromName;

    public SmtpEmailService(IConfiguration config)
    {
        _smtpHost = config["Email:SmtpHost"] ?? throw new InvalidOperationException("Email:SmtpHost not configured");
        _smtpPort = int.Parse(config["Email:SmtpPort"] ?? "587");
        _smtpUsername = config["Email:SmtpUsername"] ?? "";
        _smtpPassword = config["Email:SmtpPassword"] ?? "";
        _fromEmail = config["Email:FromEmail"] ?? throw new InvalidOperationException("Email:FromEmail not configured");
        _fromName = config["Email:FromName"] ?? "BMS";
    }

    public async Task SendInviteEmailAsync(string email, string inviteLink)
    {
        var subject = "Welcome to BMS - Accept Your Invitation";
        var body = $@"
            <h2>Welcome to BMS!</h2>
            <p>You've been invited to join our Business Management System.</p>
            <p><a href='{inviteLink}'>Accept Invitation</a></p>
            <p>This link expires in 24 hours.</p>
        ";

        await SendEmailCoreAsync(email, subject, body);
    }

    public async Task SendPasswordResetEmailAsync(string email, string resetLink)
    {
        var subject = "Reset Your BMS Password";
        var body = $@"
            <h2>Password Reset</h2>
            <p>Click the link below to reset your password:</p>
            <p><a href='{resetLink}'>Reset Password</a></p>
            <p>This link expires in 1 hour.</p>
        ";

        await SendEmailCoreAsync(email, subject, body);
    }

    public async Task SendEmailAsync(string email, string subject, string body)
    {
        await SendEmailCoreAsync(email, subject, body);
    }

    public async Task SendTrialReminderAsync(string email, string companyName, string subdomain, int daysLeft)
    {
        var subject = $"Your Trial Expires in {daysLeft} Days";
        var upgradeUrl = $"https://{subdomain}.yourapp.com/billing/upgrade";
        
        var body = $@"
            <h2>Hello {companyName}!</h2>
            <p>Your trial period will end in <strong>{daysLeft} days</strong>.</p>
            <p>Don't lose access to your account! Upgrade now to continue using all features.</p>
            <p><a href='{upgradeUrl}' style='background-color: #4CAF50; color: white; padding: 10px 20px; text-decoration: none; border-radius: 5px;'>Upgrade Now</a></p>
            <p>What you'll get with Pro:</p>
            <ul>
                <li>✅ Unlimited users</li>
                <li>✅ Unlimited messages</li>
                <li>✅ Custom branding</li>
                <li>✅ 24/7 Priority support</li>
            </ul>
            <p>Questions? Reply to this email.</p>
        ";

        await SendEmailCoreAsync(email, subject, body);
    }

    public async Task SendTrialExpiredAsync(string email, string companyName, string subdomain)
    {
        var subject = "Your Trial Has Expired";
        var upgradeUrl = $"https://{subdomain}.yourapp.com/billing/upgrade";
        
        var body = $@"
            <h2>Hello {companyName}!</h2>
            <p>Your trial period has ended and your account has been suspended.</p>
            <p>To restore access to your account, please upgrade to our Pro plan.</p>
            <p><a href='{upgradeUrl}' style='background-color: #f44336; color: white; padding: 10px 20px; text-decoration: none; border-radius: 5px;'>Upgrade Now to Restore Access</a></p>
            <p>Pro Plan includes:</p>
            <ul>
                <li>✅ Unlimited users</li>
                <li>✅ Unlimited messages</li>
                <li>✅ Custom branding</li>
                <li>✅ 24/7 Priority support</li>
            </ul>
            <p>We'd love to have you back!</p>
        ";

        await SendEmailCoreAsync(email, subject, body);
    }

    private async Task SendEmailCoreAsync(string toEmail, string subject, string htmlBody)
    {
        using (var client = new System.Net.Mail.SmtpClient(_smtpHost, _smtpPort))
        {
            client.EnableSsl = true;
            client.Credentials = new System.Net.NetworkCredential(_smtpUsername, _smtpPassword);

            var mailMessage = new System.Net.Mail.MailMessage
            {
                From = new System.Net.Mail.MailAddress(_fromEmail, _fromName),
                Subject = subject,
                Body = htmlBody,
                IsBodyHtml = true
            };

            mailMessage.To.Add(toEmail);

            await client.SendMailAsync(mailMessage);
        }
    }
}
