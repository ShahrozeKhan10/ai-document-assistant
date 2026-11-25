namespace DocAssistant.Api.DTOs;

public record RegisterRequest(string Email, string Password);

public record LoginRequest(string Email, string Password);

public record AuthResponse(string Token, string Email, DateTime ExpiresAtUtc);

public record MeResponse(Guid Id, string Email);
