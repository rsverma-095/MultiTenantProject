-- ============================================================================
-- Row-Level Security: the database-layer half of tenant isolation.
--
-- This is independent of the application. Even a query that completely bypasses
-- EF Core (raw ADO.NET, a stored proc, a future bug, a misused IgnoreQueryFilters)
-- is filtered by the database engine itself, keyed off the SESSION_CONTEXT value
-- that TenantSessionConnectionInterceptor sets on every connection open.
--
-- Apply this AFTER the EF Core schema migration has created the tables. In a real
-- migration you run it via migrationBuilder.Sql(...) so it is version-controlled
-- alongside the schema.
--
-- OPERATIONAL REQUIREMENT (do not skip): the application must connect with a
-- least-privilege SQL login. Members of db_owner / sysadmin and the table owner can
-- bypass filter predicates. The app login should own none of these tables and hold
-- only CRUD rights. Verify this in your Azure SQL setup.
-- ============================================================================

IF SCHEMA_ID(N'sec') IS NULL
    EXEC(N'CREATE SCHEMA sec;');
GO

-- Drop the policy first so we can replace the predicate function below.
-- The function cannot be altered while referenced by a security policy.
IF EXISTS (SELECT 1 FROM sys.security_policies WHERE name = N'TenantSecurityPolicy' AND schema_id = SCHEMA_ID(N'sec'))
    DROP SECURITY POLICY sec.TenantSecurityPolicy;
GO

CREATE OR ALTER FUNCTION sec.fn_tenant_predicate(@TenantId uniqueidentifier)
    RETURNS TABLE
    WITH SCHEMABINDING
AS
    RETURN
        SELECT 1 AS is_accessible
        WHERE @TenantId = CAST(SESSION_CONTEXT(N'TenantId') AS uniqueidentifier);
GO

-- If no tenant is set in SESSION_CONTEXT, CAST(...) is NULL, the equality is unknown,
-- and zero rows pass the predicate. That is the fail-closed behaviour we want.

CREATE SECURITY POLICY sec.TenantSecurityPolicy
    ADD FILTER PREDICATE sec.fn_tenant_predicate(TenantId) ON dbo.Contacts,
    ADD BLOCK PREDICATE  sec.fn_tenant_predicate(TenantId) ON dbo.Contacts AFTER INSERT,
    ADD FILTER PREDICATE sec.fn_tenant_predicate(TenantId) ON dbo.Companies,
    ADD BLOCK PREDICATE  sec.fn_tenant_predicate(TenantId) ON dbo.Companies AFTER INSERT
    WITH (STATE = ON);
GO

-- FILTER predicates silently hide rows from SELECT/UPDATE/DELETE.
-- BLOCK ... AFTER INSERT stops a row being written with a TenantId that does not match
-- the session, so even a forged TenantId on insert is rejected by the database.
--
-- When you add a new tenant-owned table, you MUST add two more lines here for it.
-- The leak tests include a guard that fails if a tenant-owned table has no policy.
