namespace Clienta.Api.Models;

public class PlanDefinition
{
    public PlanType Plan { get; set; }

    public int UserLimit { get; set; }
    public int MessageLimit { get; set; }

    public bool AllowCustomBranding { get; set; }
    public bool EmailAutomation { get; set; }
    public string Priority { get; set; } = default!;
    public string Support { get; set; } = default!;
}
