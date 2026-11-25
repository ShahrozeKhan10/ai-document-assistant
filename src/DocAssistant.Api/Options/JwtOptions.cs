namespace DocAssistant.Api.Options;

public class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "DocAssistant";
    public string Audience { get; set; } = "DocAssistant";

    /// <summary>
    /// Secret used to sign tokens (HMAC-SHA256), at least 32 characters.
    /// Set with user-secrets locally (setup-phase4.bat) and with JWT_KEY in Docker. Never commit it.
    /// </summary>
    public string Key { get; set; } = string.Empty;

    public int ExpiresMinutes { get; set; } = 120;
}
