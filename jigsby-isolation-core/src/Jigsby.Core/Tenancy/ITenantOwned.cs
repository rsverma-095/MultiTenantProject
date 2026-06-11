namespace Jigsby.Core.Tenancy;

/// <summary>
/// Marks an entity as belonging to a single tenant.
///
/// Every type that implements this gets, automatically:
///   - a global query filter (so reads only see the current tenant's rows)
///   - TenantId stamped on insert from the current tenant context
///   - TenantId frozen on update (it can never be reassigned)
///   - a Row-Level Security policy applied in SQL (see Migrations/Sql/001_RowLevelSecurity.sql)
///
/// To make a new table tenant-scoped, implement this interface and add the table
/// to the RLS policy script. Both steps are required. The leak tests will fail loudly
/// if a tenant-owned table is missing from the RLS policy.
/// </summary>
public interface ITenantOwned
{
    Guid TenantId { get; set; }
}
