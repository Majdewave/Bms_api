using System;

namespace Clienta.Api.Entities
{
    public abstract class BaseEntity
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
    }
}