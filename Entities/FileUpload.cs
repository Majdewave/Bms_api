using System.ComponentModel.DataAnnotations;

namespace Clienta.Api.Entities;

public class FileUpload
{
    public Guid Id { get; set; }

    public Guid BusinessId { get; set; }
    public Business Business { get; set; } = null!;

    [Required]
    public Guid ClientId { get; set; }
    public Client Client { get; set; } = null!;

    [Required]
    public string FileName { get; set; } = string.Empty;

    [Required]
    public string OriginalFileName { get; set; } = string.Empty;

    [Required]
    public long FileSize { get; set; }

    [Required]
    public string ContentType { get; set; } = string.Empty;

    public string? Description { get; set; }

    public Guid UploadedByUserId { get; set; }
    public User UploadedByUser { get; set; } = null!;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
