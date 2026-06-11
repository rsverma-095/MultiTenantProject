using System.Security.Claims;
using Jigsby.Core.Tenancy;

namespace Jigsby.Api.Middleware;

/// <summary>
/// Resolves the current tenant and opens a scope for the duration of the request.
///
/// Resolution order (first match wins):
///   1. "tenant_id" JWT claim  — set by the token issued at login (production path)
///   2. X-Tenant-Id header     — fallback for tools / dev testing without a token
///
/// Must run AFTER UseAuthentication() so context.User is already populated.
/// If neither source yields a valid tenant the request continues without a scope:
/// reads return nothing and writes throw (fail-closed by design).
/// </summary>
public sealed class TenantResolutionMiddleware
{
    public const string TenantClaimType  = "tenant_id";
    public const string TenantHeaderName = "X-Tenant-Id";

    private readonly RequestDelegate _next;

    public TenantResolutionMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, ITenantContext tenantContext)
    {
        var tenantId = ResolveFromClaim(context) ?? ResolveFromHeader(context);

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

    private static Guid? ResolveFromClaim(HttpContext context)
    {
        var value = context.User?.FindFirstValue(TenantClaimType);
        return Guid.TryParse(value, out var id) && id != Guid.Empty ? id : null;
    }

    private static Guid? ResolveFromHeader(HttpContext context)
    {
        var value = context.Request.Headers[TenantHeaderName].FirstOrDefault();
        return Guid.TryParse(value, out var id) && id != Guid.Empty ? id : null;
    }
}
