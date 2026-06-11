using Jigsby.Core.Entities;
using Jigsby.Core.Tenancy;
using Jigsby.Infrastructure.Data;
using Jigsby.Infrastructure.Tenancy;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Jigsby.Tenancy.Tests;

/// <summary>
/// The adversarial isolation suite. These tests ARE the definition of "isolation holds".
/// Certification means: a qualified reviewer has read the core, AND every one of these
/// passes against a real SQL Server (Azure SQL recommended, LocalDB acceptable for speed).
///
/// They require a connection string in the JIGSBY_TEST_SQL environment variable, pointing
/// at a database the test run can create, migrate, apply RLS to, and tear down.
///
/// Coverage:
///   1. App layer: tenant A cannot list tenant B's rows.
///   2. App layer: tenant A cannot fetch tenant B's row by its exact id.
///   3. Write stamping: inserts are stamped with the active tenant automatically.
///   4. Tenant freeze: a row's TenantId cannot be reassigned by an update.
///   5. Fail-closed: with no tenant scope, a write throws.
///   6. Database layer (independent of EF): with SESSION_CONTEXT set to A, raw SQL sees
///      only A; with SESSION_CONTEXT unset, raw SQL sees zero rows.
///   7. Database layer: a forged cross-tenant INSERT is blocked by the RLS block predicate.
/// </summary>
public sealed class TenantIsolationTests : IClassFixture<SqlDatabaseFixture>
{
    private readonly SqlDatabaseFixture _fixture;
    private readonly Guid _tenantA;
    private readonly Guid _tenantB;

    public TenantIsolationTests(SqlDatabaseFixture fixture)
    {
        _fixture = fixture;
        _tenantA = fixture.TenantA;
        _tenantB = fixture.TenantB;
    }

    private AppDbContext NewContext(ITenantContext tenant)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(
                _fixture.ConnectionString,
                sql => { })
            .AddInterceptors(new TenantSessionConnectionInterceptor(tenant))
            .Options;
        return new AppDbContext(options, tenant);
    }

    [Fact]
    public async Task Tenant_A_cannot_list_tenant_B_contacts()
    {
        var tenant = new AmbientTenantContext();
        using (tenant.BeginScope(_tenantA))
        {
            await using var db = NewContext(tenant);
            var contacts = await db.Contacts.ToListAsync();
            Assert.All(contacts, c => Assert.Equal(_tenantA, c.TenantId));
            Assert.DoesNotContain(contacts, c => c.TenantId == _tenantB);
        }
    }

    [Fact]
    public async Task Tenant_A_cannot_fetch_tenant_B_contact_by_id()
    {
        var tenant = new AmbientTenantContext();
        using (tenant.BeginScope(_tenantA))
        {
            await using var db = NewContext(tenant);
            var foreign = await db.Contacts.FirstOrDefaultAsync(c => c.Id == _fixture.TenantBContactId);
            Assert.Null(foreign);
        }
    }

    [Fact]
    public async Task Insert_is_stamped_with_active_tenant()
    {
        var tenant = new AmbientTenantContext();
        using (tenant.BeginScope(_tenantA))
        {
            await using var db = NewContext(tenant);
            var contact = new Contact { FirstName = "Stamp", LastName = "Test" };
            db.Contacts.Add(contact);
            await db.SaveChangesAsync();
            Assert.Equal(_tenantA, contact.TenantId);
        }
    }

    [Fact]
    public async Task TenantId_cannot_be_reassigned_on_update()
    {
        var tenant = new AmbientTenantContext();
        Guid id;
        using (tenant.BeginScope(_tenantA))
        {
            await using var db = NewContext(tenant);
            var contact = new Contact { FirstName = "Freeze", LastName = "Test" };
            db.Contacts.Add(contact);
            await db.SaveChangesAsync();
            id = contact.Id;

            contact.LastName = "Changed";
            contact.TenantId = _tenantB; // attempt to move it to another tenant
            await db.SaveChangesAsync();
        }

        using (tenant.BeginScope(_tenantA))
        {
            await using var db = NewContext(tenant);
            var reloaded = await db.Contacts.FirstOrDefaultAsync(c => c.Id == id);
            Assert.NotNull(reloaded);
            Assert.Equal(_tenantA, reloaded!.TenantId); // still tenant A
        }
    }

    [Fact]
    public async Task Write_without_tenant_scope_throws()
    {
        var tenant = new AmbientTenantContext(); // no scope opened
        await using var db = NewContext(tenant);
        db.Contacts.Add(new Contact { FirstName = "No", LastName = "Tenant" });
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Database_RLS_blocks_cross_tenant_even_via_raw_sql()
    {
        // Bypass EF entirely. Set the session context to tenant A and read raw.
        await using var connection = new SqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync();

        await using (var setCtx = connection.CreateCommand())
        {
            setCtx.CommandText =
                "EXEC sp_set_session_context @key = N'TenantId', @value = @t;";
            setCtx.Parameters.Add(new SqlParameter("@t", _tenantA));
            await setCtx.ExecuteNonQueryAsync();
        }

        await using (var read = connection.CreateCommand())
        {
            read.CommandText = "SELECT TenantId FROM dbo.Contacts;";
            await using var reader = await read.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                Assert.Equal(_tenantA, reader.GetGuid(0));
        }
    }

    [Fact]
    public async Task Database_RLS_returns_nothing_when_session_context_unset()
    {
        await using var connection = new SqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync();
        // No sp_set_session_context call: SESSION_CONTEXT('TenantId') is NULL.

        await using var read = connection.CreateCommand();
        read.CommandText = "SELECT COUNT(*) FROM dbo.Contacts;";
        var count = (int)(await read.ExecuteScalarAsync())!;
        Assert.Equal(0, count); // fail-closed at the database
    }

    [Fact]
    public async Task Database_RLS_blocks_forged_cross_tenant_insert()
    {
        await using var connection = new SqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync();

        await using (var setCtx = connection.CreateCommand())
        {
            setCtx.CommandText =
                "EXEC sp_set_session_context @key = N'TenantId', @value = @t;";
            setCtx.Parameters.Add(new SqlParameter("@t", _tenantA));
            await setCtx.ExecuteNonQueryAsync();
        }

        // Try to insert a row tagged for tenant B while acting as tenant A.
        await using var insert = connection.CreateCommand();
        insert.CommandText =
            "INSERT INTO dbo.Contacts (Id, TenantId, FirstName, LastName) " +
            "VALUES (NEWID(), @b, 'Forged', 'Row');";
        insert.Parameters.Add(new SqlParameter("@b", _tenantB));

        // The block predicate must reject this.
        await Assert.ThrowsAsync<SqlException>(() => insert.ExecuteNonQueryAsync());
    }
}
