namespace Clienta.Api.DTOs;

public record InviteUserRequest(string Email);

public record AcceptInviteRequest(string Token,string Password,string? FullName = null);
public record AcceptInviteResponse(bool Success, string Message);

public record RequestPasswordResetRequest(string Email);

public record ResetPasswordRequest(string Token, string NewPassword);

public record ResetPasswordResponse(bool Success, string Message);
