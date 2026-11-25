using System.IdentityModel.Tokens.Jwt;
using System.Net.Mail;
using System.Security.Claims;
using System.Text;
using DocAssistant.Api.Data;
using DocAssistant.Api.DTOs;
using DocAssistant.Api.Models;
using DocAssistant.Api.Options;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace DocAssistant.Api.Services.Auth;

public class AuthService : IAuthService
{
    private const int MinPasswordLength = 8;

    private readonly AppDbContext _db;
    private readonly IPasswordHasher<User> _hasher;
    private readonly JwtOptions _jwt;
    private readonly ILogger<AuthService> _logger;

    public AuthService(AppDbContext db, IPasswordHasher<User> hasher, IOptions<JwtOptions> jwt, ILogger<AuthService> logger)
    {
        _db = db;
        _hasher = hasher;
        _jwt = jwt.Value;
        _logger = logger;
    }

    public async Task<AuthResult> RegisterAsync(RegisterRequest request, CancellationToken ct)
    {
        var email = Normalize(request.Email);
        if (!IsValidEmail(email))
            return AuthResult.Fail("Please enter a valid email address.", StatusCodes.Status400BadRequest);
        if (string.IsNullOrEmpty(request.Password) || request.Password.Length < MinPasswordLength)
            return AuthResult.Fail($"Password must be at least {MinPasswordLength} characters.", StatusCodes.Status400BadRequest);

        if (await _db.Users.AnyAsync(u => u.Email == email, ct))
            return AuthResult.Fail("An account with this email already exists.", StatusCodes.Status409Conflict);

        var isFirstUser = !await _db.Users.AnyAsync(ct);

        var user = new User { Id = Guid.NewGuid(), Email = email };
        user.PasswordHash = _hasher.HashPassword(user, request.Password);

        _db.Users.Add(user);
        await _db.SaveChangesAsync(ct);

        // Documents uploaded before login existed have no owner; give them to the first account.
        if (isFirstUser)
        {
            var claimed = await _db.Documents
                .Where(d => d.OwnerId == null)
                .ExecuteUpdateAsync(s => s.SetProperty(d => d.OwnerId, (Guid?)user.Id), ct);
            if (claimed > 0) _logger.LogInformation("Assigned {Count} existing documents to {Email}", claimed, email);
        }

        _logger.LogInformation("Registered {Email}", email);
        return AuthResult.Ok(CreateToken(user));
    }

    public async Task<AuthResult> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        var email = Normalize(request.Email);
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);

        // Same message for "no such user" and "wrong password", so attackers can't discover which emails exist.
        const string invalid = "Invalid email or password.";
        if (user is null || string.IsNullOrEmpty(request.Password))
            return AuthResult.Fail(invalid, StatusCodes.Status401Unauthorized);

        var result = _hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (result == PasswordVerificationResult.Failed)
            return AuthResult.Fail(invalid, StatusCodes.Status401Unauthorized);

        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = _hasher.HashPassword(user, request.Password);
            await _db.SaveChangesAsync(ct);
        }

        return AuthResult.Ok(CreateToken(user));
    }

    private AuthResponse CreateToken(User user)
    {
        var expires = DateTime.UtcNow.AddMinutes(_jwt.ExpiresMinutes);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwt.Key));
        var token = new JwtSecurityToken(
            issuer: _jwt.Issuer,
            audience: _jwt.Audience,
            claims: claims,
            expires: expires,
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        return new AuthResponse(new JwtSecurityTokenHandler().WriteToken(token), user.Email, expires);
    }

    private static string Normalize(string? email) => (email ?? string.Empty).Trim().ToLowerInvariant();

    private static bool IsValidEmail(string email) =>
        email.Length is > 3 and <= 256 && MailAddress.TryCreate(email, out var parsed) && parsed.Address == email;
}
