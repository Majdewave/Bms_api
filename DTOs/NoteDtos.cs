using System.ComponentModel.DataAnnotations;

namespace Clienta.Api.DTOs;

public class CreateNoteRequest
{
    [Required]
    public Guid ClientId { get; set; }
    [Required]
    public string Content { get; set; } = string.Empty;
}

public record UpdateNoteRequest(
    string Content
);

public record NoteResponse(
    Guid Id,
    Guid ClientId,
    string Content,
    Guid CreatedByUserId,
    string CreatedBy,
    DateTime CreatedAt
);
