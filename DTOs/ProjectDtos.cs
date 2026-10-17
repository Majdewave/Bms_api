using Clienta.Api.Models;

namespace Clienta.Api.DTOs;

public record CreateProjectRequest(
    Guid ClientId,
    string Name,
    ProjectStatus Status,
    decimal Value,
    DateTime? StartDate,
    DateTime? EndDate,
    string? Description
);

public record UpdateProjectRequest(
    string Name,
    ProjectStatus Status,
    decimal Value,
    DateTime? StartDate,
    DateTime? EndDate,
    string? Description
);

public record ProjectResponse(
    Guid Id,
    Guid ClientId,
    string ClientName,
    string Name,
    ProjectStatus Status,
    decimal Value,
    DateTime? StartDate,
    DateTime? EndDate,
    string? Description,
    DateTime CreatedAt,
    DateTime UpdatedAt
);
