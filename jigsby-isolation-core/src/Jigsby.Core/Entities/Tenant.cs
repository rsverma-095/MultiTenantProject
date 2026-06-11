namespace Jigsby.Core.Entities;

/// <summary>
/// A customer account. The root of the tenancy tree.
/// The Tenant row itself is NOT tenant-owned (it cannot scope to itself), so it is
/// not exposed through the normal tenant-scoped data access. Provisioning and
/// administration of tenants happens through a separate, explicitly cross-tenant
/// path that your developer should design as part of the super-admin surface.
/// </summary>
public class Tenant
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public bool IsActive { get; set; } = true;
}
