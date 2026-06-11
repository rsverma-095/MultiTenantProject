using System.Data.Common;
using Jigsby.Core.Tenancy;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Jigsby.Infrastructure.Data;

/// <summary>
/// On every connection open, writes the current tenant id into SQL Server's
/// SESSION_CONTEXT under the key 'TenantId'. The Row-Level Security predicate
/// (see Migrations/Sql/001_RowLevelSecurity.sql) reads that value. This is what makes
/// the database itself refuse cross-tenant rows even if a query bypasses EF Core
/// (raw SQL, a future bug, a misused IgnoreQueryFilters()).
///
/// THE SHARP EDGE (please verify under your real pooling configuration):
/// Connection pooling reuses physical connections. ADO.NET issues sp_reset_connection
/// when a pooled connection is handed back out, which clears SESSION_CONTEXT, and this
/// interceptor's ConnectionOpened(Async) fires again on the next open and re-sets it.
/// We set @read_only = 1 so application code cannot tamper with the value mid-connection.
/// If no tenant is established, we write NULL, and the RLS predicate then matches no rows
/// (fail-closed). Confirm this reset-then-reset behaviour holds for your driver version
/// and pooling settings; it is the load-bearing assumption of the database layer.
/// </summary>
public sealed class TenantSessionConnectionInterceptor : DbConnectionInterceptor
{
    private const string SetSessionContextSql =
        "EXEC sp_set_session_context @key = N'TenantId', @value = @tenant, @read_only = 1;";

    private readonly ITenantContext _tenantContext;

    public TenantSessionConnectionInterceptor(ITenantContext tenantContext)
        => _tenantContext = tenantContext;

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        using var command = CreateCommand(connection);
        command.ExecuteNonQuery();
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        await using var command = CreateCommand(connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private DbCommand CreateCommand(DbConnection connection)
    {
        var command = connection.CreateCommand();
        command.CommandText = SetSessionContextSql;

        var parameter = new SqlParameter("@tenant", System.Data.SqlDbType.UniqueIdentifier)
        {
            Value = (object?)_tenantContext.TenantId ?? DBNull.Value
        };
        command.Parameters.Add(parameter);
        return command;
    }
}
