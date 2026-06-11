using Microsoft.AspNetCore.Identity;

namespace Jigsby.Core.Entities;

/// <summary>
/// A user belongs to exactly one tenant.
///
/// DELIBERATE DESIGN DECISION FOR YOUR DEVELOPER TO REVIEW:
/// ApplicationUser does NOT implement ITenantOwned and is NOT covered by the global
/// query filter. Login and identity lookups have to happen before a tenant context
/// exists (you cannot scope the lookup that establishes the scope). Tenant binding
/// for users is therefore enforced at the auth layer: on sign-in we resolve the
/// tenant, issue a "tenant_id" claim, and every subsequent request is scoped from
/// that claim. Cross-tenant user enumeration must be prevented in the auth/account
/// code, not by the data filter. This is the single most important non-obvious
/// decision in the whole core. Please scrutinise it.
/// </summary>
public class ApplicationUser : IdentityUser<Guid>
{
    public Guid TenantId { get; set; }

    public string? DisplayName { get; set; }
}
