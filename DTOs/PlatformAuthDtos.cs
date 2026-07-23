namespace Clienta.Api.DTOs;

public record PlatformLoginRequest(string Email, string Password);

public record PlatformBootstrapRequest(string FullName, string Email, string Password);
