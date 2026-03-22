namespace Clienta.Api.Entities;

public class Prescription
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ClientId { get; set; }
    public DateTime Date { get; set; }
    public string Instructions { get; set; } = string.Empty;
    public string DoctorName { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
