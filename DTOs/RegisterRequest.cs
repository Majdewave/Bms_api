namespace Clienta.Api.DTOs;

public class RegisterRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string BusinessName { get; set; } = string.Empty;
    public string FullName { get;set; } = string.Empty;
}
