using Jigsby.Core.Tenancy;

namespace Jigsby.Api.Middleware;

/// <summary>
/// Resolves the current tenant from the X-Tenant-Id request header and opens
/// a tenant scope for the duration of the request.
///
/// If the header is absent or invalid the request continues with no scope:
/// reads return nothing and writes throw (fail-closed by design).
///
/// When authentication is added later, resolve the tenant from the "tenant_id"
/// JWT claim here instead of (or in addition to) the header.
/// </summary>
public sealed class TenantResolutionMiddleware
{
    public const string TenantHeaderName = "X-Tenant-Id";

    private readonly RequestDelegate _next;

    public TenantResolutionMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, ITenantContext tenantContext)
    {
        var tenantId = Resolve(context);

        if (tenantId.HasValue)
        {
            using (tenantContext.BeginScope(tenantId.Value))
                await _next(context);
        }
        else
        {
            await _next(context);
        }
    }

    private static Guid? Resolve(HttpContext context)
    {
        var value = context.Request.Headers[TenantHeaderName].FirstOrDefault();
        return Guid.TryParse(value, out var id) && id != Guid.Empty ? id : null;
    }
}
