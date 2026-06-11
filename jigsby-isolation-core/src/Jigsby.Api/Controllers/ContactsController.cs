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
    public async Task<ActionResult<Contact>> Create(Contact contact)
    {
        contact.Id = Guid.NewGuid();
        _db.Contacts.Add(contact);
        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(Get), new { id = contact.Id }, contact);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<Contact>> Update(Guid id, Contact contact)
    {
        var existing = await _db.Contacts.FirstOrDefaultAsync(c => c.Id == id);
        if (existing is null) return NotFound();

        existing.FirstName = contact.FirstName;
        existing.LastName  = contact.LastName;
        existing.Email     = contact.Email;
        existing.CompanyId = contact.CompanyId;

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
