using System.Security.Claims;
using FluentValidation;
using FluentValidation.AspNetCore;
using FutureKawaSiege.Business.Helpers;
using FutureKawaSiege.Business.Services.Abstraction;
using FutureKawaSiege.Business.Validators;
using FutureKawaSiege.Commons.Exceptions.Services;
using FutureKawaSiege.Commons.Models.API;
using FutureKawaSiege.Commons.Models.API.Requests;
using FutureKawaSiege.Commons.Models.API.Responses;
using FutureKawaSiege.Data.Repositories;
using FutureKawaSiege.API.Helpers;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FutureKawaSiege.API.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly IRefreshTokenService _refreshTokenService;
    private readonly ILogger<AuthController> _logger;

    private const string RefreshTokenCookie = "refresh_token";

    public AuthController(IAuthService authService, IRefreshTokenService refreshTokenService, ILogger<AuthController> logger)
    {
        _authService = authService;
        _refreshTokenService = refreshTokenService;
        _logger = logger;
    }

    /// <summary>
    /// Authenticate with email/password. Returns JWT in body + refresh token in httpOnly cookie.
    /// </summary>
    [HttpPost("login")]
    [EnableRateLimiting("login")]
    public async Task<ActionResult<ApiResponse<LoginResponse>>> Login(
        [FromBody] LoginRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await _authService.LoginAsync(request, cancellationToken);

            SetRefreshTokenCookie(response.RefreshToken);

            return Ok(ApiResponse<LoginResponse>.Ok(response));
        }
        catch (AuthenticationException ex)
        {
            return Unauthorized(ApiResponse<LoginResponse>.Fail(ex.Message));
        }
    }

    /// <summary>
    /// Exchange refresh token cookie for a new JWT + new refresh token cookie.
    /// </summary>
    [HttpPost("refresh")]
    [EnableRateLimiting("refresh")]
    public async Task<ActionResult<ApiResponse<LoginResponse>>> Refresh(
        CancellationToken cancellationToken)
    {
        var cookie = Request.Cookies[RefreshTokenCookie];
        if (string.IsNullOrWhiteSpace(cookie))
            return Unauthorized(ApiResponse<LoginResponse>.Fail("No refresh token provided."));

        try
        {
            var response = await _authService.RefreshAsync(cookie, cancellationToken);

            SetRefreshTokenCookie(response.RefreshToken);

            return Ok(ApiResponse<LoginResponse>.Ok(response));
        }
        catch (AuthenticationException ex)
        {
            return Unauthorized(ApiResponse<LoginResponse>.Fail(ex.Message));
        }
    }

    /// <summary>
    /// Revoke refresh token and remove cookie. Always returns 200 even if cookie absent.
    /// </summary>
    [HttpPost("logout")]
    public async Task<ActionResult<ApiResponse<object>>> Logout(
        CancellationToken cancellationToken)
    {
        var cookie = Request.Cookies[RefreshTokenCookie];
        if (!string.IsNullOrWhiteSpace(cookie))
        {
            try
            {
                await _authService.LogoutAsync(cookie, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to revoke refresh token during logout");
            }
        }

        Response.Cookies.Delete(RefreshTokenCookie);

        return Ok(ApiResponse<object>.Ok(new { }, "Logged out successfully."));
    }

    /// <summary>
    /// Return current authenticated user info from JWT claims.
    /// </summary>
    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<ApiResponse<UserResponse>>> Me()
    {
        var user = await _authService.GetCurrentUserAsync(User);
        return Ok(ApiResponse<UserResponse>.Ok(user));
    }

    // ── Helpers ──

    private void SetRefreshTokenCookie(string rawToken)
    {
        var cookieOptions = new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Expires = DateTimeOffset.UtcNow.AddDays(7),
            Path = "/api/auth",
        };

        Response.Cookies.Append(RefreshTokenCookie, rawToken, cookieOptions);
    }
}
