namespace Clienta.Api.DTOs;

public class RegisterRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string ConfirmPassword { get; set; } = string.Empty;
    public string BusinessName { get; set; } = string.Empty;
    public string FullName { get;set; } = string.Empty;
    public string? Phone { get; set; }
    public string? BusinessType { get; set; }
    public string? Language { get; set; }
    public string? TimeZoneId { get; set; }
}
