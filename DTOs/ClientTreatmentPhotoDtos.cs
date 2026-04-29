using Microsoft.AspNetCore.Http;

namespace Clienta.Api.DTOs;

public class CreateClientTreatmentPhotoRequest
{
    public Guid ClientId { get; set; }
    public IFormFile? BeforeImage { get; set; }
    public IFormFile? AfterImage { get; set; }
}

public record ClientTreatmentPhotoResponse(
    Guid Id,
    Guid ClientId,
    string? BeforeImageUrl,
    string? AfterImageUrl,
    DateTime CreatedAt
);
