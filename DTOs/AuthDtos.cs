public record ForgotPasswordRequest(string Email);
public record ResetPasswordRequest(string Token, string NewPassword);
public record LoginRequest(string Email, string Password, bool RememberMe);
public record RegisterRequest(string Email, string FullName, string BusinessName, string Password);
