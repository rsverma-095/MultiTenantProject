using System.ComponentModel.DataAnnotations;
using Jigsby.Api.Auth;
using Jigsby.Core.Entities;
using Jigsby.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Jigsby.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[EnableRateLimiting("auth")]
public sealed class AuthController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly AppDbContext                 _db;
    private readonly JwtTokenService             _tokens;
    private readonly RefreshTokenService         _refresh;

    public AuthController(
        UserManager<ApplicationUser> userManager,
        AppDbContext db,
        JwtTokenService tokens,
        RefreshTokenService refresh)
    {
        _userManager = userManager;
        _db          = db;
        _tokens      = tokens;
        _refresh     = refresh;
    }

    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register(RegisterRequest req)
    {
        var tenant = await _db.Tenants.FindAsync(req.TenantId);
        if (tenant is null)
            return BadRequest($"Tenant '{req.TenantId}' not found.");
        if (!tenant.IsActive)
            return BadRequest("This tenant account is deactivated.");

        var user = new ApplicationUser
        {
            Id          = Guid.NewGuid(),
            UserName    = req.UserName,
            Email       = req.Email,
            DisplayName = req.DisplayName,
            TenantId    = req.TenantId
        };

        var result = await _userManager.CreateAsync(user, req.Password);
        if (!result.Succeeded)
            return BadRequest(result.Errors.Select(e => e.Description));

        var roles   = await _userManager.GetRolesAsync(user);
        var access  = _tokens.Generate(user, roles);
        var refresh = await _refresh.CreateAsync(user.Id);

        return Ok(BuildResponse(user, tenant.Name, access, refresh));
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest req)
    {
        var user = await _userManager.FindByEmailAsync(req.Email);
        if (user is null || !await _userManager.CheckPasswordAsync(user, req.Password))
            return Unauthorized("Invalid email or password.");

        var tenant = await _db.Tenants.FindAsync(user.TenantId);
        if (tenant is null || !tenant.IsActive)
            return Unauthorized("This account cannot log in.");

        var roles   = await _userManager.GetRolesAsync(user);
        var access  = _tokens.Generate(user, roles);
        var refresh = await _refresh.CreateAsync(user.Id);

        return Ok(BuildResponse(user, tenant.Name, access, refresh));
    }

    [HttpPost("refresh")]
    public async Task<ActionResult<AuthResponse>> Refresh(RefreshRequest req)
    {
        var existing = await _refresh.GetValidAsync(req.RefreshToken);
        if (existing is null)
            return Unauthorized("Refresh token is invalid or expired.");

        var user   = existing.User;
        var tenant = await _db.Tenants.FindAsync(user.TenantId);
        if (tenant is null || !tenant.IsActive)
            return Unauthorized("This account cannot log in.");

        var roles  = await _userManager.GetRolesAsync(user);
        var access = _tokens.Generate(user, roles);
        var next   = await _refresh.CreateAsync(user.Id);

        await _refresh.RevokeAsync(existing, next.Token);

        return Ok(BuildResponse(user, tenant.Name, access, next));
    }

    [Authorize]
    [HttpPost("revoke")]
    public async Task<IActionResult> Revoke(RevokeRequest req)
    {
        var token = await _refresh.GetValidAsync(req.RefreshToken);
        if (token is null)
            return BadRequest("Token not found or already revoked.");

        await _refresh.RevokeAsync(token);
        return NoContent();
    }

    private static AuthResponse BuildResponse(
        ApplicationUser user,
        string tenantName,
        TokenResult access,
        RefreshToken refresh)
        => new(
            user.Id,
            user.UserName!,
            user.Email!,
            user.DisplayName,
            user.TenantId,
            tenantName,
            access.Token,
            access.ExpiresAt,
            refresh.Token,
            refresh.ExpiresAt);
}

public record RegisterRequest(
    [Required] Guid TenantId,
    [Required, StringLength(50, MinimumLength = 2)] string UserName,
    [Required, EmailAddress, StringLength(256)] string Email,
    [Required, StringLength(100, MinimumLength = 12)] string Password,
    [StringLength(100)] string? DisplayName);

public record LoginRequest(
    [Required, EmailAddress] string Email,
    [Required] string Password);

public record RefreshRequest(
    [Required] string RefreshToken);

public record RevokeRequest(
    [Required] string RefreshToken);

public record AuthResponse(
    Guid     UserId,
    string   UserName,
    string   Email,
    string?  DisplayName,
    Guid     TenantId,
    string   TenantName,
    string   AccessToken,
    DateTime AccessTokenExpiresAt,
    string   RefreshToken,
    DateTime RefreshTokenExpiresAt);
