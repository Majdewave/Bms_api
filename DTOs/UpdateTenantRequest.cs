namespace Clienta.Api.DTOs;

public class UpdateTenantRequest
{
    public string Name { get; set; } = default!;
    public string? LogoUrl { get; set; }
}
