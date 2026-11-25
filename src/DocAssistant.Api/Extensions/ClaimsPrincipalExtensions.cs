using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace DocAssistant.Api.Extensions;

public static class ClaimsPrincipalExtensions
{
    /// <summary>The logged-in user's id, read from the JWT "sub" claim.</summary>
    public static Guid GetUserId(this ClaimsPrincipal user)
    {
        var sub = user.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                  ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        return Guid.TryParse(sub, out var id)
            ? id
            : throw new UnauthorizedAccessException("The token has no valid user id.");
    }

    public static string GetEmail(this ClaimsPrincipal user) =>
        user.FindFirst(JwtRegisteredClaimNames.Email)?.Value
        ?? user.FindFirst(ClaimTypes.Email)?.Value
        ?? string.Empty;
}
