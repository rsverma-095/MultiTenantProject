using System.ComponentModel.DataAnnotations;
using Jigsby.Core.Entities;
using Jigsby.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Jigsby.Api.Controllers;

[Authorize(Policy = "AdminOnly")]
[ApiController]
[Route("api/[controller]")]
public sealed class TenantsController : ControllerBase
{
    private readonly AppDbContext _db;
    public TenantsController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<IEnumerable<TenantResponse>>> List()
        => await _db.Tenants.AsNoTracking().OrderBy(t => t.Name)
            .Select(t => new TenantResponse(t.Id, t.Name, t.IsActive, t.CreatedAtUtc))
            .ToListAsync();

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<TenantResponse>> Get(Guid id)
    {
        var t = await _db.Tenants.FindAsync(id);
        return t is null ? NotFound() : new TenantResponse(t.Id, t.Name, t.IsActive, t.CreatedAtUtc);
    }

    [HttpPost]
    public async Task<ActionResult<TenantResponse>> Create(CreateTenantRequest req)
    {
        var tenant = new Tenant
        {
            Id           = Guid.NewGuid(),
            Name         = req.Name,
            CreatedAtUtc = DateTime.UtcNow
        };
        _db.Tenants.Add(tenant);
        await _db.SaveChangesAsync();
        var response = new TenantResponse(tenant.Id, tenant.Name, tenant.IsActive, tenant.CreatedAtUtc);
        return CreatedAtAction(nameof(Get), new { id = tenant.Id }, response);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<TenantResponse>> Update(Guid id, UpdateTenantRequest req)
    {
        var existing = await _db.Tenants.FindAsync(id);
        if (existing is null) return NotFound();

        existing.Name     = req.Name;
        existing.IsActive = req.IsActive;
        await _db.SaveChangesAsync();
        return new TenantResponse(existing.Id, existing.Name, existing.IsActive, existing.CreatedAtUtc);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var existing = await _db.Tenants.FindAsync(id);
        if (existing is null) return NotFound();
        _db.Tenants.Remove(existing);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}

public record TenantResponse(Guid Id, string Name, bool IsActive, DateTime CreatedAtUtc);
public record CreateTenantRequest([Required, StringLength(200, MinimumLength = 2)] string Name);
public record UpdateTenantRequest(
    [Required, StringLength(200, MinimumLength = 2)] string Name,
    bool IsActive);
