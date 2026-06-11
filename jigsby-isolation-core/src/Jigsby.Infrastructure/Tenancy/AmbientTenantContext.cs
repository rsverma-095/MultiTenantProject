using Jigsby.Core.Tenancy;

namespace Jigsby.Infrastructure.Tenancy;

/// <summary>
/// Singleton implementation of <see cref="ITenantContext"/> backed by an AsyncLocal.
///
/// WHY SINGLETON + ASYNCLOCAL (please review carefully):
/// AsyncLocal flows with the async execution context, so a value set at the top of a
/// request (or a background-job scope) is visible to every awaited continuation below
/// it, and is isolated between concurrent requests. Making the holder a singleton avoids
/// DI-scope-lifetime mismatches between the (scoped) DbContext and the (singleton)
/// connection interceptor: both depend on this one instance and both observe the correct
/// per-execution-path value. The alternative (scoped ITenantContext) forces the
/// interceptor to reach into the current DI scope, which is the classic place this
/// pattern goes wrong. This design is deliberate; it is also exactly the kind of
/// concurrency assumption a reviewer should pressure-test under load.
/// </summary>
public sealed class AmbientTenantContext : ITenantContext
{
    private static readonly AsyncLocal<Guid?> Current = new();

    public Guid? TenantId => Current.Value;

    public bool HasTenant => Current.Value.HasValue;

    public IDisposable BeginScope(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("Tenant id cannot be empty.", nameof(tenantId));

        var previous = Current.Value;
        Current.Value = tenantId;
        return new ScopeHandle(previous);
    }

    private sealed class ScopeHandle : IDisposable
    {
        private readonly Guid? _previous;
        private bool _disposed;

        public ScopeHandle(Guid? previous) => _previous = previous;

        public void Dispose()
        {
            if (_disposed) return;
            Current.Value = _previous;
            _disposed = true;
        }
    }
}
