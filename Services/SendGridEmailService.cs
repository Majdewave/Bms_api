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

    public Task SendTrialReminderAsync(string email, string name, string subdomain, int days)
        => Task.CompletedTask;

    public Task SendTrialExpiredAsync(string email, string name, string subdomain)
        => Task.CompletedTask;


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
