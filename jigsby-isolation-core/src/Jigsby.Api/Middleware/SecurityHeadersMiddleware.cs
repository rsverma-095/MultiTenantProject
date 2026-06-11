namespace Jigsby.Api.Middleware;

public sealed class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;
    public SecurityHeadersMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        var h = context.Response.Headers;
        h["X-Content-Type-Options"] = "nosniff";
        h["X-Frame-Options"]        = "DENY";
        h["Referrer-Policy"]        = "strict-origin-when-cross-origin";
        h["X-XSS-Protection"]      = "0";
        h["Permissions-Policy"]     = "camera=(), microphone=(), geolocation=()";
        await _next(context);
    }
}
