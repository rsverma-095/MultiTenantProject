using Jigsby.Api.Middleware;
using Jigsby.Core.Entities;
using Jigsby.Core.Tenancy;
using Jigsby.Infrastructure.Data;
using Jigsby.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
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

// Exposed so the integration/leak test project can reference the composition root.
public partial class Program { }
