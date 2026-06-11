using Jigsby.Core.Tenancy;

namespace Jigsby.Api.Middleware;

/// <summary>
/// Establishes the tenant context for each request from the authenticated principal's
/// "tenant_id" claim, which is issued at sign-in.
///
/// Must run AFTER authentication (so the claim is available) and BEFORE anything that
/// touches tenant-owned data. See Program.cs for ordering.
///
/// If there is no valid tenant claim (anonymous request, or an auth/account endpoint
/// that legitimately runs outside a tenant), no scope is opened. Any tenant-owned data
/// access in that state then fails closed: reads return nothing, writes throw, and the
/// database RLS layer matches no rows. There is intentionally no path here that grants
/// access to all tenants.
/// </summary>
public sealed class TenantResolutionMiddleware
{
    public const string TenantClaimType = "tenant_id";

    private readonly RequestDelegate _next;

    public TenantResolutionMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, ITenantContext tenantContext)
    {
        var claim = context.User?.FindFirst(TenantClaimType)?.Value;

        if (Guid.TryParse(claim, out var tenantId) && tenantId != Guid.Empty)
        {
            using (tenantContext.BeginScope(tenantId))
            {
                await _next(context);
            }
        }
        else
        {
            await _next(context);
        }
    }
}
