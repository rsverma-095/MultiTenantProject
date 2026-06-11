using Jigsby.Core.Tenancy;

namespace Jigsby.Core.Entities;

/// <summary>
/// Minimal stand-in for the CRM's contacts table.
/// </summary>
public class Contact : ITenantOwned
{
    public Guid Id { get; set; }

    // Stamped automatically on insert, frozen on update.
    public Guid TenantId { get; set; }

    public string FirstName { get; set; } = string.Empty;

    public string LastName { get; set; } = string.Empty;

    public string? Email { get; set; }

    // Nullable FK to Company. Note: a correct implementation must ensure a contact
    // can only ever link to a company in the SAME tenant. The RLS policy guarantees
    // a cross-tenant CompanyId cannot be READ back, but your developer should also
    // decide whether to add a composite (TenantId, Id) FK to prevent the bad write
    // in the first place. Flagged in the certification notes.
    public Guid? CompanyId { get; set; }

    public Company? Company { get; set; }
}
