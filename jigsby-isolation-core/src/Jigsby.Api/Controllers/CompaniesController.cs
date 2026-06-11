using System.ComponentModel.DataAnnotations;
using Jigsby.Core.Entities;
using Jigsby.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Jigsby.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public sealed class CompaniesController : ControllerBase
{
    private readonly AppDbContext _db;
    public CompaniesController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<IEnumerable<Company>>> List()
        => await _db.Companies.AsNoTracking().OrderBy(c => c.Name).ToListAsync();

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<Company>> Get(Guid id)
    {
        var company = await _db.Companies.FirstOrDefaultAsync(c => c.Id == id);
        return company is null ? NotFound() : company;
    }

    [HttpPost]
    public async Task<ActionResult<Company>> Create(CreateCompanyRequest req)
    {
        var company = new Company
        {
            Id    = Guid.NewGuid(),
            Name  = req.Name,
            City  = req.City,
            State = req.State
        };
        _db.Companies.Add(company);
        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(Get), new { id = company.Id }, company);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<Company>> Update(Guid id, UpdateCompanyRequest req)
    {
        var existing = await _db.Companies.FirstOrDefaultAsync(c => c.Id == id);
        if (existing is null) return NotFound();

        existing.Name  = req.Name;
        existing.City  = req.City;
        existing.State = req.State;

        await _db.SaveChangesAsync();
        return existing;
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var existing = await _db.Companies.FirstOrDefaultAsync(c => c.Id == id);
        if (existing is null) return NotFound();
        _db.Companies.Remove(existing);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}

public record CreateCompanyRequest(
    [Required, StringLength(200, MinimumLength = 1)] string Name,
    [StringLength(100)] string? City,
    [StringLength(100)] string? State);

public record UpdateCompanyRequest(
    [Required, StringLength(200, MinimumLength = 1)] string Name,
    [StringLength(100)] string? City,
    [StringLength(100)] string? State);
