using Jigsby.Core.Tenancy;

namespace Jigsby.Core.Entities;

/// <summary>
/// Minimal stand-in for the CRM's companies table, just enough to demonstrate a
/// tenant-owned entity and a cross-table relationship with Contact.
/// </summary>
public class Company : ITenantOwned
{
    public Guid Id { get; set; }

    // Stamped automatically on insert, frozen on update. Never set this by hand
    // in application code.
    public Guid TenantId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? City { get; set; }

    public string? State { get; set; }
}
