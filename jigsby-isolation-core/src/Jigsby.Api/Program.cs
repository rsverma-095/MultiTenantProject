using Jigsby.Api.Middleware;
using Jigsby.Core.Entities;
using Jigsby.Core.Tenancy;
using Jigsby.Infrastructure.Data;
using Jigsby.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// --- Tenancy wiring -------------------------------------------------------------
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

builder.Services.AddControllers();
builder.Services.AddOpenApi();

var app = builder.Build();

// --- Database initialization ---------------------------------------------------
await InitializeDatabaseAsync(app);

// --- API docs UI ---------------------------------------------------------------
app.MapOpenApi();
app.MapScalarApiReference(options =>
{
    options.Title = "Jigsby API";
    options.Theme = ScalarTheme.Purple;
});

// Redirect root to the API browser
app.MapGet("/", () => Results.Redirect("/scalar/v1"));

// --- Middleware ----------------------------------------------------------------
app.UseMiddleware<TenantResolutionMiddleware>();
app.MapControllers();

// Health check
app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }));

// List all tenants so you can copy a tenant ID to use as X-Tenant-Id header
app.MapGet("/dev/tenants", async (AppDbContext db) =>
{
    var tenants = await db.Tenants
        .OrderBy(t => t.Name)
        .Select(t => new { t.Id, t.Name })
        .ToListAsync();
    return Results.Ok(tenants);
});

app.Run();

// ---------------------------------------------------------------------------
// Database initialization: creates schema + applies Row-Level Security policy
// ---------------------------------------------------------------------------
static async Task InitializeDatabaseAsync(WebApplication app)
{
    var logger = app.Services.GetRequiredService<ILogger<Program>>();

    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    await db.Database.EnsureCreatedAsync();
    await SeedAsync(db, logger);

    var connectionString = app.Configuration.GetConnectionString("Default")!;
    var rlsPath = Path.Combine(AppContext.BaseDirectory, "Sql", "001_RowLevelSecurity.sql");
    if (!File.Exists(rlsPath))
    {
        logger.LogWarning("RLS script not found at {Path} — Row-Level Security was NOT applied.", rlsPath);
        return;
    }

    try
    {
        var script = await File.ReadAllTextAsync(rlsPath);
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync();
        foreach (var batch in script.Split(["\nGO", "\r\nGO"], StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = batch.Trim();
            if (string.IsNullOrWhiteSpace(trimmed)) continue;
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = trimmed;
            await cmd.ExecuteNonQueryAsync();
        }
        logger.LogInformation("Row-Level Security policy applied successfully.");
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "RLS script failed — policy may already be applied. Continuing startup.");
    }
}

// ---------------------------------------------------------------------------
// Seed two tenants with sample contacts so the API returns real data on first run.
// Uses fixed GUIDs so the IDs are stable across restarts.
// ---------------------------------------------------------------------------
static async Task SeedAsync(AppDbContext db, ILogger logger)
{
    if (await db.Tenants.AnyAsync()) return; // already seeded

    var tenantAId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    var tenantBId = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");

    // Insert tenants directly — no tenant scope needed for this table
    db.Tenants.AddRange(
        new Jigsby.Core.Entities.Tenant { Id = tenantAId, Name = "Acme Corp",    CreatedAtUtc = DateTime.UtcNow, IsActive = true },
        new Jigsby.Core.Entities.Tenant { Id = tenantBId, Name = "Globex Ltd",   CreatedAtUtc = DateTime.UtcNow, IsActive = true }
    );
    await db.SaveChangesAsync();

    // Seed contacts per tenant using a scoped tenant context
    var tenantCtx = db.GetService<Jigsby.Core.Tenancy.ITenantContext>();

    using (tenantCtx.BeginScope(tenantAId))
    {
        db.Contacts.AddRange(
            new Jigsby.Core.Entities.Contact { Id = Guid.NewGuid(), FirstName = "Alice",   LastName = "Anderson", Email = "alice@acme.com" },
            new Jigsby.Core.Entities.Contact { Id = Guid.NewGuid(), FirstName = "Bob",     LastName = "Baker",    Email = "bob@acme.com"   },
            new Jigsby.Core.Entities.Contact { Id = Guid.NewGuid(), FirstName = "Charlie", LastName = "Clark",    Email = "charlie@acme.com" }
        );
        await db.SaveChangesAsync();
    }

    using (tenantCtx.BeginScope(tenantBId))
    {
        db.Contacts.AddRange(
            new Jigsby.Core.Entities.Contact { Id = Guid.NewGuid(), FirstName = "Diana", LastName = "Davis",  Email = "diana@globex.com" },
            new Jigsby.Core.Entities.Contact { Id = Guid.NewGuid(), FirstName = "Eve",   LastName = "Evans",  Email = "eve@globex.com"   }
        );
        await db.SaveChangesAsync();
    }

    logger.LogInformation("Seeded 2 tenants (Acme Corp, Globex Ltd) with 5 contacts.");
}

// Exposed so the integration/leak test project can reference the composition root.
public partial class Program { }
