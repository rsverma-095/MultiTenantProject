using Jigsby.Core.Entities;
using Jigsby.Core.Tenancy;
using Jigsby.Infrastructure.Data;
using Jigsby.Infrastructure.Tenancy;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Jigsby.Tenancy.Tests;

/// <summary>
/// Stands up a real SQL Server database for the leak tests: creates the schema, applies
/// the Row-Level Security script, and seeds two tenants each with one contact.
///
/// Set JIGSBY_TEST_SQL to a connection string the test run may fully manage, e.g.
///   Server=(localdb)\MSSQLLocalDB;Database=JigsbyCoreTests;Trusted_Connection=True;TrustServerCertificate=True
/// or an Azure SQL dev database connection string for production-parity verification.
///
/// NOTE: the database is created with EnsureCreated for speed in this demonstration.
/// In the real project, replace this with the EF Core migration pipeline plus a
/// migrationBuilder.Sql() step that runs 001_RowLevelSecurity.sql, so schema and policy
/// are versioned together. Flagged in the certification notes.
/// </summary>
public sealed class SqlDatabaseFixture : IAsyncLifetime
{
    public string ConnectionString { get; } =
        Environment.GetEnvironmentVariable("JIGSBY_TEST_SQL")
        ?? throw new InvalidOperationException(
            "Set JIGSBY_TEST_SQL to a SQL Server connection string the tests can manage.");

    public Guid TenantA { get; } = Guid.NewGuid();
    public Guid TenantB { get; } = Guid.NewGuid();
    public Guid TenantBContactId { get; private set; }

    private string RlsScriptPath =>
        Path.Combine(AppContext.BaseDirectory, "Sql", "001_RowLevelSecurity.sql");

    public async Task InitializeAsync()
    {
        var noTenant = new AmbientTenantContext();

        // Build the schema. (No tenant scope needed for EnsureCreated.)
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;

        await using (var db = new AppDbContext(options, noTenant))
        {
            await db.Database.EnsureDeletedAsync();
            await db.Database.EnsureCreatedAsync();
        }

        // Seed BEFORE enabling RLS, so we can insert rows for two different tenants
        // from one connection without the block predicate getting in the way.
        await SeedAsync();

        // Now apply Row-Level Security.
        await ApplyRlsAsync();
    }

    private async Task SeedAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();

        async Task InsertTenant(Guid id, string name)
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText =
                "INSERT INTO dbo.Tenants (Id, Name, CreatedAtUtc, IsActive) " +
                "VALUES (@id, @name, SYSUTCDATETIME(), 1);";
            cmd.Parameters.Add(new SqlParameter("@id", id));
            cmd.Parameters.Add(new SqlParameter("@name", name));
            await cmd.ExecuteNonQueryAsync();
        }

        async Task<Guid> InsertContact(Guid tenantId, string first, string last)
        {
            var id = Guid.NewGuid();
            await using var cmd = connection.CreateCommand();
            cmd.CommandText =
                "INSERT INTO dbo.Contacts (Id, TenantId, FirstName, LastName) " +
                "VALUES (@id, @tid, @f, @l);";
            cmd.Parameters.Add(new SqlParameter("@id", id));
            cmd.Parameters.Add(new SqlParameter("@tid", tenantId));
            cmd.Parameters.Add(new SqlParameter("@f", first));
            cmd.Parameters.Add(new SqlParameter("@l", last));
            await cmd.ExecuteNonQueryAsync();
            return id;
        }

        await InsertTenant(TenantA, "Tenant A");
        await InsertTenant(TenantB, "Tenant B");
        await InsertContact(TenantA, "Alice", "Anderson");
        TenantBContactId = await InsertContact(TenantB, "Bob", "Brown");
    }

    private async Task ApplyRlsAsync()
    {
        if (!File.Exists(RlsScriptPath))
            throw new FileNotFoundException(
                $"RLS script not found at {RlsScriptPath}. Ensure it is copied to output.",
                RlsScriptPath);

        var script = await File.ReadAllTextAsync(RlsScriptPath);

        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();

        // Split on GO batch separators (simple splitter sufficient for this script).
        foreach (var batch in script.Split(["\nGO", "\r\nGO"], StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = batch.Trim();
            if (trimmed.Length == 0) continue;
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = trimmed;
            await cmd.ExecuteNonQueryAsync();
        }
    }

    public async Task DisposeAsync()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;
        await using var db = new AppDbContext(options, new AmbientTenantContext());
        await db.Database.EnsureDeletedAsync();
    }
}
