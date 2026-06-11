namespace Jigsby.Core.Tenancy;

/// <summary>
/// The single source of truth for "which tenant is the current execution path acting as".
///
/// Both isolation layers read from this:
///   - the EF Core global query filter (application layer)
///   - the SESSION_CONTEXT value used by SQL Row-Level Security (database layer)
///
/// It is deliberately fail-closed: if no tenant has been established, TenantId is null,
/// which causes reads to return nothing and writes to throw. There is no "all tenants"
/// value reachable from normal application code.
/// </summary>
public interface ITenantContext
{
    Guid? TenantId { get; }

    bool HasTenant { get; }

    /// <summary>
    /// Establishes the current tenant for the duration of the returned scope.
    ///
    /// Request handling: the tenant-resolution middleware opens a scope per request
    /// from the authenticated principal.
    ///
    /// Non-request paths (background jobs, the IMAP poller, data seeding, tests):
    /// these have no HTTP request and MUST open an explicit scope before touching
    /// tenant-owned data. This is the deliberate replacement for the old single-user
    /// CRM's cron poller, which had no tenant concept at all.
    /// </summary>
    IDisposable BeginScope(Guid tenantId);
}
