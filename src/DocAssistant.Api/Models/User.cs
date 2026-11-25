namespace DocAssistant.Api.Models;

public class User
{
    public Guid Id { get; set; }

    /// <summary>Stored lower-case so logins are case-insensitive.</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>PBKDF2 hash from ASP.NET Core's PasswordHasher. The password itself is never stored.</summary>
    public string PasswordHash { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
