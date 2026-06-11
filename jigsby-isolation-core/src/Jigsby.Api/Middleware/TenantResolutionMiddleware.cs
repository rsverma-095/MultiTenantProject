using Jigsby.Core.Tenancy;

namespace Jigsby.Api.Middleware;

/// <summary>
/// Establishes the tenant context for each request.
///
/// Tenant resolution order:
///   1. X-Tenant-Id request header  (development / direct API calls)
///   2. "tenant_id" JWT claim        (production, once auth is added)
///
/// If neither is present the request proceeds with no tenant scope:
/// reads return nothing and writes throw (fail-closed).
/// </summary>
public sealed class TenantResolutionMiddleware
{
    public const string TenantClaimType  = "tenant_id";
    public const string TenantHeaderName = "X-Tenant-Id";

    private readonly RequestDelegate _next;

    public TenantResolutionMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, ITenantContext tenantContext)
    {
        var tenantId = ResolveFromHeader(context) ?? ResolveFromClaim(context);

        if (tenantId.HasValue)
        {
            using (tenantContext.BeginScope(tenantId.Value))
            {
                await _next(context);
            }
        }
        else
        {
            await _next(context);
        }
    }

    private static Guid? ResolveFromHeader(HttpContext context)
    {
        var value = context.Request.Headers[TenantHeaderName].FirstOrDefault();
        return Guid.TryParse(value, out var id) && id != Guid.Empty ? id : null;
    }

    private static Guid? ResolveFromClaim(HttpContext context)
    {
        var value = context.User?.FindFirst(TenantClaimType)?.Value;
        return Guid.TryParse(value, out var id) && id != Guid.Empty ? id : null;
    }
}
