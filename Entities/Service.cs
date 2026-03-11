using System;

namespace Clienta.Api.Entities
{
    public class Service
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public string Name { get; set; } = string.Empty;
        public int DefaultDurationMinutes { get; set; } = 60;
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
    }
    
}