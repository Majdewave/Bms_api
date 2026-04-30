using System;

namespace Clienta.Api.Entities
{
    public class VisitSummary
    {
        public Guid Id { get; set; }

        public Guid TenantId { get; set; }

        public Guid ClientId { get; set; }

        public Guid? StaffId { get; set; }

        public string Examination { get; set; } = string.Empty;   // בדיקה

        public string Diagnosis { get; set; } = string.Empty;     // אבחנה

        public string Recommendations { get; set; } = string.Empty; // המלצות

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
