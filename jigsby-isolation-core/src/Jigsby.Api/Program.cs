using Jigsby.Api.Middleware;
using Jigsby.Core.Entities;
using Jigsby.Core.Tenancy;
using Jigsby.Infrastructure.Data;
using Jigsby.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// --- Tenancy wiring -------------------------------------------------------------
// Singleton tenant context (AsyncLocal-backed) shared by the scoped DbContext and the
// singleton connection interceptor. See AmbientTenantContext for why this is a singleton.
builder.Services.AddSingleton<ITenantContext, AmbientTenantContext>();
builder.Services.AddSingleton<TenantSessionConnectionInterceptor>();

// --- Data ----------------------------------------------------------------------
builder.Services.AddDbContext<AppDbContext>((serviceProvider, options) =>
{
    options
        .UseSqlServer(builder.Configuration.GetConnectionString("Default"))
        .AddInterceptors(serviceProvider.GetRequiredService<TenantSessionConnectionInterceptor>());
});

// --- Identity ------------------------------------------------------------------
builder.Services
    .AddIdentityCore<ApplicationUser>(options =>
    {
        options.Password.RequiredLength = 12;
        options.User.RequireUniqueEmail = true;
    })
    .AddRoles<IdentityRole<Guid>>()
    .AddEntityFrameworkStores<AppDbContext>();

builder.Services.AddAuthentication();
builder.Services.AddAuthorization();
builder.Services.AddControllers();

var app = builder.Build();

// --- Middleware order matters --------------------------------------------------
app.UseAuthentication();              // populate context.User (and its tenant_id claim)
app.UseMiddleware<TenantResolutionMiddleware>();  // open the tenant scope from the claim
app.UseAuthorization();
app.MapControllers();

app.Run();

// Exposed so the integration/leak test project can reference the composition root.
public partial class Program { }
