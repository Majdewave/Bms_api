namespace Clienta.Api.Models;

public class PlanLimits
{
    public static readonly Dictionary<string, PlanFeatures> Plans = new()
    {
        ["Trial"] = new PlanFeatures
        {
            MaxUsers = 10,
            MaxMessages = 100,
            CustomBranding = false,
            EmailAutomation = true,
            Priority = "Standard",
            Support = "Email only"
        },
        ["Pro"] = new PlanFeatures
        {
            MaxUsers = -1, // Unlimited
            MaxMessages = -1, // Unlimited
            CustomBranding = true,
            EmailAutomation = true,
            Priority = "High",
            Support = "24/7 Priority"
        }
    };
}

public class PlanFeatures
{
    public int MaxUsers { get; set; }
    public int MaxMessages { get; set; }
    public bool CustomBranding { get; set; }
    public bool EmailAutomation { get; set; }
    public string Priority { get; set; } = default!;
    public string Support { get; set; } = default!;
}
