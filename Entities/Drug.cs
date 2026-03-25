namespace Clienta.Api.Entities;

public class Drug
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string? Dosage { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
