using DocAssistant.Api.DTOs;

namespace DocAssistant.Api.Services.Auth;

/// <summary>Result of register/login: either a token, or an error with the HTTP status to return.</summary>
public record AuthResult(AuthResponse? Response, string? Error, int StatusCode)
{
    public static AuthResult Ok(AuthResponse response) => new(response, null, StatusCodes.Status200OK);
    public static AuthResult Fail(string error, int statusCode) => new(null, error, statusCode);
}

public interface IAuthService
{
    Task<AuthResult> RegisterAsync(RegisterRequest request, CancellationToken ct);
    Task<AuthResult> LoginAsync(LoginRequest request, CancellationToken ct);
}
