using System;

namespace Clienta.Api.Entities
{
    public class Service : ITenantEntity
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public string Name { get; set; } = string.Empty;
        public int DefaultDurationMinutes { get; set; } = 60;
        public string? ImagingModality { get; set; }
        public Guid? DepartmentId { get; set; }
        public Department? Department { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
    }
    
}