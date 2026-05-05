using SendGrid;
using SendGrid.Helpers.Mail;

namespace Clienta.Api.Services;

public class SendGridEmailService : IEmailService
{
    private readonly IConfiguration _config;

    public SendGridEmailService(IConfiguration config)
    {
        _config = config;
    }

    public async Task SendPasswordResetEmailAsync(string toEmail, string resetLink)
    {
        var apiKey = _config["SendGrid:ApiKey"];
        var client = new SendGridClient(apiKey);

        var from = new EmailAddress("mjd.salman@gmail.com", "Clienta");
        var to = new EmailAddress(toEmail);

        var subject = "איפוס סיסמה";

        var htmlContent = $@"
        <div style='font-family:Arial; direction:rtl'>
            <h2>איפוס סיסמה</h2>
            <p>לחץ על הקישור הבא כדי לאפס סיסמה:</p>
            <a href='{resetLink}' style='background:#2563eb;color:white;padding:10px 20px;border-radius:5px;text-decoration:none'>
                איפוס סיסמה
            </a>
        </div>";

        var msg = MailHelper.CreateSingleEmail(from, to, subject, "", htmlContent);

        var response = await client.SendEmailAsync(msg);
        Console.WriteLine("SENDGRID STATUS: " + response.StatusCode);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Body.ReadAsStringAsync();
            throw new Exception($"SendGrid error: {body}");
        }
    }

    public async Task SendEmailAsync(string to, string subject, string html)
    {
        var apiKey = _config["SendGrid:ApiKey"];
        var client = new SendGridClient(apiKey);

        var from = new EmailAddress("mjd.salman@gmail.com", "Clienta");
        var toEmail = new EmailAddress(to);

        var msg = MailHelper.CreateSingleEmail(from, toEmail, subject, "", html);

        var response = await client.SendEmailAsync(msg);

        Console.WriteLine("SENDGRID STATUS: " + response.StatusCode);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Body.ReadAsStringAsync();
            throw new Exception($"SendGrid error: {body}");
        }
    }


    public async Task SendTrialReminderAsync(string email, string companyName, string subdomain, int daysLeft, Guid tenantId)
    {
        var apiKey = _config["SendGrid:ApiKey"];
        var client = new SendGridClient(apiKey);

        var from = new EmailAddress("mjd.salman@gmail.com", "Clienta");
        var to = new EmailAddress(email);

        var subject = $"Your Trial Expires in {daysLeft} Days";
        var upgradeUrl = $"https://clienta.digitalpenpro.com/upgrade?tenantId={tenantId}";

        var htmlContent = $@"
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

        var msg = MailHelper.CreateSingleEmail(from, to, subject, "", htmlContent);
        var response = await client.SendEmailAsync(msg);
        Console.WriteLine("SENDGRID STATUS: " + response.StatusCode);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Body.ReadAsStringAsync();
            throw new Exception($"SendGrid error: {body}");
        }
    }

    public async Task SendTrialExpiredAsync(string email, string companyName, string subdomain, Guid tenantId)
    {
        var apiKey = _config["SendGrid:ApiKey"];
        var client = new SendGridClient(apiKey);

        var from = new EmailAddress("mjd.salman@gmail.com", "Clienta");
        var to = new EmailAddress(email);

        var subject = "Your Trial Has Expired";
        var upgradeUrl = $"https://clienta.digitalpenpro.com/upgrade?tenantId={tenantId}"; ;

        var htmlContent = $@"
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

        var msg = MailHelper.CreateSingleEmail(from, to, subject, "", htmlContent);
        var response = await client.SendEmailAsync(msg);
        Console.WriteLine("SENDGRID STATUS: " + response.StatusCode);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Body.ReadAsStringAsync();
            throw new Exception($"SendGrid error: {body}");
        }
    }


    public async Task SendInviteEmailAsync(string toEmail, string inviteLink)
    {
        Console.WriteLine("SendInviteEmailAsync by sendgrid");
        var apiKey = _config["SendGrid:ApiKey"];
        var client = new SendGridClient(apiKey);

        var from = new EmailAddress("mjd.salman@gmail.com", "Clienta");
        var to = new EmailAddress(toEmail);

        var subject = "הזמנה למערכת";

        var htmlContent = $@"
    <div style='font-family:Arial; direction:rtl'>
        <h2>הוזמנת למערכת Clienta</h2>
        <p>לחץ על הקישור הבא כדי להפעיל את החשבון שלך:</p>
        <a href='{inviteLink}' style='background:#16a34a;color:white;padding:10px 20px;border-radius:5px;text-decoration:none'>
            הפעל חשבון
        </a>
    </div>";

        var msg = MailHelper.CreateSingleEmail(from, to, subject, "", htmlContent);

        var response = await client.SendEmailAsync(msg);

        Console.WriteLine("SENDGRID STATUS: " + response.StatusCode);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Body.ReadAsStringAsync();
            throw new Exception($"SendGrid error: {body}");
        }
    }
}
