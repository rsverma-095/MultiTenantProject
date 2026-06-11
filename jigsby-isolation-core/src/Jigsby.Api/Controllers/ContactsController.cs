using Jigsby.Core.Entities;
using Jigsby.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Jigsby.Api.Controllers;

/// <summary>
/// Demonstrates that ordinary controller code needs no tenant-awareness at all.
/// There is not a single "WHERE TenantId = ..." in here. The global query filter
/// scopes the reads and SaveChanges stamps the writes, both from the ambient tenant
/// context that the middleware established. This is the whole point: feature code
/// built on top of this core cannot forget the tenant filter, because it never writes
/// one. (Raw SQL is the exception that bypasses this layer; the database RLS layer is
/// the backstop for that case.)
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
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
        // Even a direct id lookup cannot fetch another tenant's row: the query filter
        // adds the tenant predicate, so a foreign id simply returns null here.
        var contact = await _db.Contacts.FirstOrDefaultAsync(c => c.Id == id);
        return contact is null ? NotFound() : contact;
    }

    [HttpPost]
    public async Task<ActionResult<Contact>> Create(Contact contact)
    {
        // No TenantId is set here on purpose. SaveChanges stamps it from context.
        _db.Contacts.Add(contact);
        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(Get), new { id = contact.Id }, contact);
    }
}
