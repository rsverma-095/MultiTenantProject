# Jigsby Tenant Isolation Core

This is the **isolation core only**: an empty-database slice with a handful of tables,
built to demonstrate exactly how multi-tenant data isolation works before any features
are built on top of it. It is written to be read, attacked, and certified by a qualified
.NET developer. It is **not** production-ready and has not been compiled or run in the
environment it was written in. Treat the leak tests as the definition of "it works".

## What this is for

The whole Jigsby plan rests on one bet: that tenant isolation is enforced at a small,
central foundation so that all the feature code built later **cannot leak data even if it
forgets to**. This core is that foundation. Once it is certified, feature modules get
built on top of it and inherit isolation automatically.

## The design in one paragraph

Single shared database, `TenantId` discriminator on every tenant-owned table, with **two
independent isolation layers** that must both agree:

1. **Application layer (EF Core).** A global query filter is applied to every entity that
   implements `ITenantOwned`, so reads only return the current tenant's rows. `SaveChanges`
   stamps `TenantId` on insert and freezes it on update. Feature code never writes a
   `WHERE TenantId = ...` clause, so it cannot forget one.
2. **Database layer (SQL Server Row-Level Security).** A security policy filters and blocks
   rows by `TenantId` against a value held in `SESSION_CONTEXT`, set on every connection by
   a connection interceptor. This catches anything that bypasses EF Core: raw SQL, a future
   bug, a misused `IgnoreQueryFilters()`. Two layers must fail before data leaks.

Both layers read the current tenant from one source of truth, `ITenantContext`, which is
**fail-closed**: no tenant set means reads return nothing and writes throw. There is no
"all tenants" value reachable from normal code.

## Project layout

```
src/Jigsby.Core            entities + the ITenantContext / ITenantOwned abstractions
src/Jigsby.Infrastructure  DbContext, query filters, tenant stamping,
                           the SESSION_CONTEXT interceptor, and the RLS SQL
src/Jigsby.Api             composition root, tenant-resolution middleware, a sample controller
tests/Jigsby.Tenancy.Tests the adversarial leak suite (the proof of correctness)
```

## How to run the leak tests

1. Have a SQL Server the test run can fully manage (create/drop). **LocalDB** on your
   Windows box is fine for speed; an **Azure SQL** dev database is the one that matters for
   production parity, because RLS + `SESSION_CONTEXT` + connection-pooling behaviour is
   exactly where local and cloud can differ.
2. Set the connection string:
   ```
   setx JIGSBY_TEST_SQL "Server=(localdb)\MSSQLLocalDB;Database=JigsbyCoreTests;Trusted_Connection=True;TrustServerCertificate=True"
   ```
3. `dotnet test`

Certification means a reviewer has read the core **and** all of these pass, against Azure
SQL, not just LocalDB.

## The sharp edges — please attack these specifically

These are the assumptions the whole thing balances on. They are the reason a human reviews
this before anything is built on it.

1. **`SESSION_CONTEXT` survival across pooled connections.**
   `TenantSessionConnectionInterceptor` re-sets the tenant on every connection open, relying
   on `sp_reset_connection` clearing session context on pooled reuse. Verify this holds for
   your driver and pooling settings under concurrency. If a pooled connection ever carries a
   previous tenant's context, that is a cross-tenant leak. This is the single highest-risk
   item.

2. **Singleton + `AsyncLocal` tenant context.**
   `AmbientTenantContext` is a singleton holding an `AsyncLocal`. Confirm the value flows
   correctly through every await path and is correctly isolated between concurrent requests,
   and that the middleware scope disposes on every exit path.

3. **The non-request paths.**
   Background work has no HTTP request and must call `BeginScope` explicitly. The old CRM's
   cron-driven IMAP poller becomes the obvious trap here: any job that touches tenant-owned
   data without opening a scope will fail closed (good) but silently do nothing (a bug you
   want surfaced, not swallowed). Review how jobs establish tenant context.

4. **Identity is deliberately outside the query filter.**
   `ApplicationUser` is not `ITenantOwned`. Login happens before a tenant context exists.
   Cross-tenant user enumeration must be prevented in the auth/account code. Review that path.

5. **Least-privilege SQL login.**
   RLS is bypassable by `db_owner`/`sysadmin`/table owners. The application's SQL login must
   hold CRUD only and own none of these tables. Verify in Azure SQL.

6. **Cross-tenant foreign keys.**
   `Contact.CompanyId` could in principle point at another tenant's company. RLS stops it
   being read back, but consider a composite `(TenantId, Id)` FK to stop the bad write.

7. **`EnsureCreated` in the test fixture.**
   Used for speed in this demo. Replace with the EF Core migration pipeline plus a
   `migrationBuilder.Sql()` step that runs `001_RowLevelSecurity.sql`, so schema and policy
   are versioned together and the policy is never forgotten on a new environment.

## What is intentionally NOT here

Auth endpoints and token/claim issuance, tenant provisioning / super-admin, billing, the
front end, and every CRM feature. Those come after certification. The point of this slice is
to make the isolation foundation small enough to review in full.
