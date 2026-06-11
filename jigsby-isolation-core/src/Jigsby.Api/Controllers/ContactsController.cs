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
public sealed class ContactsController : ControllerBase
{
    private readonly AppDbContext _db;
    public ContactsController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<IEnumerable<Contact>>> List()
        => await _db.Contacts.AsNoTracking().OrderBy(c => c.LastName).ToListAsync();

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<Contact>> Get(Guid id)
    {
        var contact = await _db.Contacts.FirstOrDefaultAsync(c => c.Id == id);
        return contact is null ? NotFound() : contact;
    }

    [HttpPost]
    public async Task<ActionResult<Contact>> Create(CreateContactRequest req)
    {
        if (req.CompanyId.HasValue && !await _db.Companies.AnyAsync(c => c.Id == req.CompanyId))
            return BadRequest("Company not found in the current tenant.");

        var contact = new Contact
        {
            Id        = Guid.NewGuid(),
            FirstName = req.FirstName,
            LastName  = req.LastName,
            Email     = req.Email,
            CompanyId = req.CompanyId
        };
        _db.Contacts.Add(contact);
        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(Get), new { id = contact.Id }, contact);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<Contact>> Update(Guid id, UpdateContactRequest req)
    {
        var existing = await _db.Contacts.FirstOrDefaultAsync(c => c.Id == id);
        if (existing is null) return NotFound();

        if (req.CompanyId.HasValue && !await _db.Companies.AnyAsync(c => c.Id == req.CompanyId))
            return BadRequest("Company not found in the current tenant.");

        existing.FirstName = req.FirstName;
        existing.LastName  = req.LastName;
        existing.Email     = req.Email;
        existing.CompanyId = req.CompanyId;

        await _db.SaveChangesAsync();
        return existing;
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var existing = await _db.Contacts.FirstOrDefaultAsync(c => c.Id == id);
        if (existing is null) return NotFound();
        _db.Contacts.Remove(existing);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}

public record CreateContactRequest(
    [Required, StringLength(100)] string FirstName,
    [Required, StringLength(100)] string LastName,
    [EmailAddress, StringLength(256)] string? Email,
    Guid? CompanyId);

public record UpdateContactRequest(
    [Required, StringLength(100)] string FirstName,
    [Required, StringLength(100)] string LastName,
    [EmailAddress, StringLength(256)] string? Email,
    Guid? CompanyId);
