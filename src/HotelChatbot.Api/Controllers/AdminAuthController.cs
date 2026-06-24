using HotelChatbot.Api.Admin;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Swashbuckle.AspNetCore.Annotations;

namespace HotelChatbot.Api.Controllers;

[ApiController]
[Route("api/admin/auth")]
[SwaggerTag("Admin-Authentifizierung")]
public class AdminAuthController : ControllerBase
{
    private readonly AdminAuthService _authService;
    private readonly AdminAuthOptions _options;

    public AdminAuthController(AdminAuthService authService, IOptions<AdminAuthOptions> options)
    {
        _authService = authService;
        _options = options.Value;
    }

    [HttpPost("login")]
    [ProducesResponseType(typeof(AdminLoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public IActionResult Login([FromBody] AdminLoginRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Password))
            return Unauthorized(new { message = "Passwort erforderlich." });

        var role = _authService.Authenticate(request.Password);
        if (role is null)
            return Unauthorized(new { message = "Ungültiges Passwort." });

        var token = _authService.CreateToken(role);
        return Ok(new AdminLoginResponse
        {
            Role = role,
            Token = token,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(_options.TokenLifetimeDays)
        });
    }

    [HttpGet("verify")]
    [ProducesResponseType(typeof(AdminVerifyResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public IActionResult Verify()
    {
        var token = ExtractBearerToken(Request.Headers.Authorization.ToString());
        if (!_authService.TryValidateToken(token, out var role))
            return Unauthorized(new { message = "Token ungültig oder abgelaufen." });

        return Ok(new AdminVerifyResponse { Role = role, Valid = true });
    }

    private static string? ExtractBearerToken(string? authorizationHeader)
    {
        if (string.IsNullOrWhiteSpace(authorizationHeader))
            return null;

        const string prefix = "Bearer ";
        if (!authorizationHeader.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return null;

        return authorizationHeader[prefix.Length..].Trim();
    }
}

public record AdminLoginRequest(string Password);

public record AdminLoginResponse
{
    public required string Role { get; init; }
    public required string Token { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
}

public record AdminVerifyResponse
{
    public required string Role { get; init; }
    public bool Valid { get; init; }
}
