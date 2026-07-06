namespace Clienta.Api.Entities;

public class DepartmentFeature
{
    public Guid Id { get; set; }
    public Guid DepartmentId { get; set; }
    public Department Department { get; set; } = default!;

    public string FeatureKey { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
}
