using DocAssistant.Api.DTOs;
using DocAssistant.Api.Extensions;
using DocAssistant.Api.Services.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DocAssistant.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _auth;

    public AuthController(IAuthService auth) => _auth = auth;

    /// <summary>Create an account and get a token.</summary>
    [AllowAnonymous]
    [HttpPost("register")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request, CancellationToken ct) =>
        ToActionResult(await _auth.RegisterAsync(request, ct));

    /// <summary>Log in and get a token. Send it as "Authorization: Bearer &lt;token&gt;".</summary>
    [AllowAnonymous]
    [HttpPost("login")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken ct) =>
        ToActionResult(await _auth.LoginAsync(request, ct));

    /// <summary>Who am I? Useful to check that a token is still valid.</summary>
    [Authorize]
    [HttpGet("me")]
    public ActionResult<MeResponse> Me() => Ok(new MeResponse(User.GetUserId(), User.GetEmail()));

    private IActionResult ToActionResult(AuthResult result) =>
        result.Response is not null
            ? Ok(result.Response)
            : Problem(result.Error, statusCode: result.StatusCode);
}
