namespace Clienta.Api.Entities;

public class PlatformSettings
{
    public Guid Id { get; set; }
    public string SupportEmail { get; set; } = "support@clienta.digitalpenpro.com";
    public string? SupportPhone { get; set; }
    public string? WebsiteUrl { get; set; } = "https://clienta.digitalpenpro.com";
    public int DefaultTrialDays { get; set; } = 21;
    public int TrialReminderDays { get; set; } = 3;
    public bool AllowRegistrations { get; set; } = true;
    public bool RequireManualApproval { get; set; } = true;
    public bool EnableBilling { get; set; } = true;
    public bool EnableHelpCenter { get; set; } = true;
    public bool WhatsAppEnabled { get; set; }

    public decimal ProMonthlyPrice { get; set; } = 46m;
    public decimal ProAnnualPrice { get; set; } = 460m;
    public string ProDescription { get; set; } = "Clienta Pro for growing teams";
    public bool ProEnabled { get; set; } = true;
    public int ProDisplayOrder { get; set; } = 1;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
