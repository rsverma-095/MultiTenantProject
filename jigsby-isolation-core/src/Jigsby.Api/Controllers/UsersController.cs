using System.ComponentModel.DataAnnotations;
using Jigsby.Core.Entities;
using Jigsby.Core.Tenancy;
using Jigsby.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Jigsby.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public sealed class UsersController : ControllerBase
{
    private readonly AppDbContext                _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ITenantContext              _tenantContext;

    public UsersController(AppDbContext db, UserManager<ApplicationUser> userManager, ITenantContext tenantContext)
    {
        _db            = db;
        _userManager   = userManager;
        _tenantContext = tenantContext;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<UserResponse>>> List()
    {
        if (!_tenantContext.HasTenant) return Unauthorized();

        var users = await _db.Users
            .AsNoTracking()
            .Where(u => u.TenantId == _tenantContext.TenantId)
            .OrderBy(u => u.UserName)
            .Select(u => new UserResponse(u.Id, u.UserName!, u.Email!, u.DisplayName))
            .ToListAsync();

        return Ok(users);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<UserResponse>> Get(Guid id)
    {
        if (!_tenantContext.HasTenant) return Unauthorized();

        var user = await _db.Users
            .Where(u => u.TenantId == _tenantContext.TenantId && u.Id == id)
            .Select(u => new UserResponse(u.Id, u.UserName!, u.Email!, u.DisplayName))
            .FirstOrDefaultAsync();

        return user is null ? NotFound() : user;
    }

    [HttpPost]
    public async Task<ActionResult<UserResponse>> Create(CreateUserRequest req)
    {
        if (!_tenantContext.HasTenant) return Unauthorized();

        var user = new ApplicationUser
        {
            Id          = Guid.NewGuid(),
            UserName    = req.UserName,
            Email       = req.Email,
            DisplayName = req.DisplayName,
            TenantId    = _tenantContext.TenantId!.Value
        };

        var result = await _userManager.CreateAsync(user, req.Password);
        if (!result.Succeeded)
            return BadRequest(result.Errors.Select(e => e.Description));

        return CreatedAtAction(nameof(Get), new { id = user.Id },
            new UserResponse(user.Id, user.UserName!, user.Email!, user.DisplayName));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<UserResponse>> Update(Guid id, UpdateUserRequest req)
    {
        if (!_tenantContext.HasTenant) return Unauthorized();

        var user = await _db.Users
            .Where(u => u.TenantId == _tenantContext.TenantId && u.Id == id)
            .FirstOrDefaultAsync();

        if (user is null) return NotFound();

        user.DisplayName = req.DisplayName;
        user.Email       = req.Email;
        user.UserName    = req.UserName;

        var result = await _userManager.UpdateAsync(user);
        if (!result.Succeeded)
            return BadRequest(result.Errors.Select(e => e.Description));

        return new UserResponse(user.Id, user.UserName!, user.Email!, user.DisplayName);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        if (!_tenantContext.HasTenant) return Unauthorized();

        var user = await _db.Users
            .Where(u => u.TenantId == _tenantContext.TenantId && u.Id == id)
            .FirstOrDefaultAsync();

        if (user is null) return NotFound();

        var result = await _userManager.DeleteAsync(user);
        if (!result.Succeeded)
            return BadRequest(result.Errors.Select(e => e.Description));

        return NoContent();
    }
}

public record UserResponse(Guid Id, string UserName, string Email, string? DisplayName);

public record CreateUserRequest(
    [Required, StringLength(50, MinimumLength = 2)] string UserName,
    [Required, EmailAddress, StringLength(256)] string Email,
    [Required, StringLength(100, MinimumLength = 12)] string Password,
    [StringLength(100)] string? DisplayName);

public record UpdateUserRequest(
    [Required, StringLength(50, MinimumLength = 2)] string UserName,
    [Required, EmailAddress, StringLength(256)] string Email,
    [StringLength(100)] string? DisplayName);
