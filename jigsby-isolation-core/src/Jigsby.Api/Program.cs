using System.Text;
using System.Threading.RateLimiting;
using Jigsby.Api.Auth;
using Jigsby.Api.Health;
using Jigsby.Api.Middleware;
using Jigsby.Core.Entities;
using Jigsby.Core.Tenancy;
using Jigsby.Infrastructure.Data;
using Jigsby.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// --- Tenancy wiring -----------------------------------------------------------
builder.Services.AddSingleton<ITenantContext, AmbientTenantContext>();
builder.Services.AddSingleton<TenantSessionConnectionInterceptor>();

// --- Data ---------------------------------------------------------------------
builder.Services.AddDbContext<AppDbContext>((sp, options) =>
    options
        .UseSqlServer(builder.Configuration.GetConnectionString("Default"))
        .AddInterceptors(sp.GetRequiredService<TenantSessionConnectionInterceptor>()));

// --- Identity -----------------------------------------------------------------
builder.Services
    .AddIdentityCore<ApplicationUser>(o =>
    {
        o.Password.RequiredLength = 12;
        o.User.RequireUniqueEmail = true;
    })
    .AddRoles<IdentityRole<Guid>>()
    .AddEntityFrameworkStores<AppDbContext>();

// --- JWT authentication -------------------------------------------------------
var jwtKey = builder.Configuration["Jwt:Key"]
    ?? throw new InvalidOperationException("Jwt:Key is not configured.");
if (jwtKey.Length < 32)
    throw new InvalidOperationException("Jwt:Key must be at least 32 characters.");

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer           = true,
            ValidateAudience         = true,
            ValidateLifetime         = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer              = builder.Configuration["Jwt:Issuer"],
            ValidAudience            = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey         = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ClockSkew                = TimeSpan.Zero
        };
    });

// --- Authorization ------------------------------------------------------------
builder.Services.AddAuthorization(o =>
    o.AddPolicy("AdminOnly", p => p.RequireRole("admin")));

// --- Application services -----------------------------------------------------
builder.Services.AddSingleton<JwtTokenService>();
builder.Services.AddScoped<RefreshTokenService>();

// --- Health checks ------------------------------------------------------------
var connectionString = builder.Configuration.GetConnectionString("Default")!;
builder.Services.AddSingleton(new SqlConnectionHealthCheck(connectionString));
builder.Services.AddHealthChecks()
    .AddCheck<SqlConnectionHealthCheck>("database", failureStatus: HealthStatus.Unhealthy, tags: ["db"]);

// --- CORS ---------------------------------------------------------------------
builder.Services.AddCors(options =>
    options.AddPolicy("Default", policy =>
        policy
            .WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [])
            .AllowAnyHeader()
            .AllowAnyMethod()));

// --- Rate limiting ------------------------------------------------------------
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("auth", _ =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: "global-auth",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit      = 20,
                Window           = TimeSpan.FromMinutes(1),
                QueueLimit       = 0,
                AutoReplenishment = true
            }));
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

// --- Problem details (global exception handler) --------------------------------
builder.Services.AddProblemDetails();

// --- API / Docs ---------------------------------------------------------------
builder.Services.AddControllers();

if (builder.Environment.IsDevelopment())
    builder.Services.AddOpenApi();

// ==============================================================================
var app = builder.Build();

await InitializeDatabaseAsync(app);

// --- Middleware pipeline (order matters) --------------------------------------
app.UseExceptionHandler();

if (!app.Environment.IsDevelopment())
    app.UseHttpsRedirection();

app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseCors("Default");
app.UseRateLimiter();
app.UseAuthentication();
app.UseMiddleware<TenantResolutionMiddleware>();
app.UseAuthorization();
app.MapControllers();

// --- Health check (always public, minimal info in production) -----------------
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = async (ctx, report) =>
    {
        ctx.Response.ContentType = "application/json";
        await ctx.Response.WriteAsJsonAsync(new
        {
            status = report.Status.ToString(),
            checks = report.Entries.ToDictionary(e => e.Key, e => e.Value.Status.ToString())
        });
    }
}).AllowAnonymous();

// --- API docs (development only) ----------------------------------------------
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(o =>
    {
        o.Title = "Jigsby API";
        o.Theme = ScalarTheme.Purple;
        o.AddServer(new ScalarServer("http://localhost:5000"));
        o.Authentication = new ScalarAuthenticationOptions
        {
            PreferredSecuritySchemes = ["Bearer"]
        };
    });
    app.MapGet("/", () => Results.Redirect("/scalar/v1")).AllowAnonymous();
}

app.Run();

// ==============================================================================

static async Task InitializeDatabaseAsync(WebApplication app)
{
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    using var scope = app.Services.CreateScope();
    var sp          = scope.ServiceProvider;
    var db          = sp.GetRequiredService<AppDbContext>();
    var tenantCtx   = sp.GetRequiredService<ITenantContext>();

    await db.Database.EnsureCreatedAsync();
    await EnsureRefreshTokensTableAsync(connectionString: app.Configuration.GetConnectionString("Default")!, logger);

    if (app.Environment.IsDevelopment())
    {
        await SeedAsync(db, tenantCtx, logger);
        await SeedAdminAsync(sp, logger);
    }

    await ApplyRlsAsync(app.Configuration.GetConnectionString("Default")!, logger);
}

static async Task SeedAsync(AppDbContext db, ITenantContext tenantCtx, ILogger logger)
{
    if (await db.Tenants.AnyAsync()) return;

    var tenantAId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    var tenantBId = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");

    db.Tenants.AddRange(
        new Tenant { Id = tenantAId, Name = "Acme Corp" },
        new Tenant { Id = tenantBId, Name = "Globex Ltd" }
    );
    await db.SaveChangesAsync();

    using (tenantCtx.BeginScope(tenantAId))
    {
        db.Contacts.AddRange(
            new Contact { Id = Guid.NewGuid(), FirstName = "Alice",   LastName = "Anderson", Email = "alice@acme.com"   },
            new Contact { Id = Guid.NewGuid(), FirstName = "Bob",     LastName = "Baker",    Email = "bob@acme.com"     },
            new Contact { Id = Guid.NewGuid(), FirstName = "Charlie", LastName = "Clark",    Email = "charlie@acme.com" }
        );
        await db.SaveChangesAsync();
    }

    using (tenantCtx.BeginScope(tenantBId))
    {
        db.Contacts.AddRange(
            new Contact { Id = Guid.NewGuid(), FirstName = "Diana", LastName = "Davis", Email = "diana@globex.com" },
            new Contact { Id = Guid.NewGuid(), FirstName = "Eve",   LastName = "Evans", Email = "eve@globex.com"   }
        );
        await db.SaveChangesAsync();
    }

    logger.LogInformation("Seeded 2 tenants with 5 contacts.");
}

static async Task SeedAdminAsync(IServiceProvider sp, ILogger logger)
{
    var roleManager = sp.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
    var userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
    var db          = sp.GetRequiredService<AppDbContext>();

    if (!await roleManager.RoleExistsAsync("admin"))
        await roleManager.CreateAsync(new IdentityRole<Guid>("admin"));

    const string adminEmail = "admin@jigsby.dev";
    if (await userManager.FindByEmailAsync(adminEmail) is not null) return;

    var tenant = await db.Tenants.FirstOrDefaultAsync();
    if (tenant is null) return;

    var admin = new ApplicationUser
    {
        Id          = Guid.NewGuid(),
        UserName    = "admin",
        Email       = adminEmail,
        DisplayName = "System Admin",
        TenantId    = tenant.Id
    };

    var result = await userManager.CreateAsync(admin, "Admin@Password123!!");
    if (result.Succeeded)
    {
        await userManager.AddToRoleAsync(admin, "admin");
        logger.LogInformation("Dev admin created — email: {Email}, password: Admin@Password123!!", adminEmail);
    }
}

static async Task EnsureRefreshTokensTableAsync(string connectionString, ILogger logger)
{
    const string sql = """
        IF NOT EXISTS (
            SELECT 1 FROM sys.tables
            WHERE name = 'RefreshTokens' AND schema_id = SCHEMA_ID('dbo'))
        BEGIN
            CREATE TABLE dbo.RefreshTokens (
                Id              UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
                UserId          UNIQUEIDENTIFIER NOT NULL,
                Token           NVARCHAR(200)    NOT NULL,
                CreatedAt       DATETIME2        NOT NULL,
                ExpiresAt       DATETIME2        NOT NULL,
                RevokedAt       DATETIME2        NULL,
                ReplacedByToken NVARCHAR(200)    NULL,
                CONSTRAINT FK_RefreshTokens_Users
                    FOREIGN KEY (UserId) REFERENCES dbo.AspNetUsers(Id) ON DELETE CASCADE,
                CONSTRAINT UQ_RefreshTokens_Token UNIQUE (Token)
            );
            CREATE INDEX IX_RefreshTokens_UserId ON dbo.RefreshTokens(UserId);
        END
        """;

    try
    {
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
        logger.LogInformation("RefreshTokens table ready.");
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Failed to create RefreshTokens table.");
    }
}

static async Task ApplyRlsAsync(string connectionString, ILogger logger)
{
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

public partial class Program { }
