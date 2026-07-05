namespace Clienta.Api.Entities;

public class StaffDepartment : ITenantEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Tenant Tenant { get; set; } = default!;

    public Guid StaffId { get; set; }
    public BusinessUser Staff { get; set; } = default!;

    public Guid DepartmentId { get; set; }
    public Department Department { get; set; } = default!;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}