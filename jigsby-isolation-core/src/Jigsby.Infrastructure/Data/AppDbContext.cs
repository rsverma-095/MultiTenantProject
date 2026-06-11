using System.Linq.Expressions;
using Jigsby.Core.Entities;
using Jigsby.Core.Tenancy;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Jigsby.Infrastructure.Data;

/// <summary>
/// The application-layer half of tenant isolation.
///
/// Two mechanisms live here:
///   1. A global query filter on every ITenantOwned entity, so reads only return rows
///      for the current tenant. The filter reads ITenantContext.TenantId at query time;
///      when it is null the generated SQL compares against NULL and returns no rows
///      (fail-closed).
///   2. TenantId stamping in SaveChanges: inserts get the current tenant id, and the
///      column is frozen on update so a tenant can never be reassigned.
///
/// The database-layer half (Row-Level Security) is independent and lives in SQL. Both
/// layers must agree; the leak tests prove they do.
/// </summary>
public class AppDbContext : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>
{
    private readonly ITenantContext _tenantContext;

    public AppDbContext(DbContextOptions<AppDbContext> options, ITenantContext tenantContext)
        : base(options)
    {
        _tenantContext = tenantContext;
    }

    public DbSet<Tenant>       Tenants       => Set<Tenant>();
    public DbSet<Company>      Companies     => Set<Company>();
    public DbSet<Contact>      Contacts      => Set<Contact>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Apply a tenant query filter to every entity that implements ITenantOwned.
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(ITenantOwned).IsAssignableFrom(entityType.ClrType))
            {
                ApplyTenantFilterMethod
                    .MakeGenericMethod(entityType.ClrType)
                    .Invoke(this, new object[] { modelBuilder });
            }
        }

        modelBuilder.Entity<Contact>().HasIndex(c => new { c.TenantId, c.Email });
        modelBuilder.Entity<Company>().HasIndex(c => new { c.TenantId, c.Name });

        modelBuilder.Entity<RefreshToken>(rt =>
        {
            rt.HasIndex(r => r.Token).IsUnique();
            rt.HasOne(r => r.User)
              .WithMany()
              .HasForeignKey(r => r.UserId)
              .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static readonly System.Reflection.MethodInfo ApplyTenantFilterMethod =
        typeof(AppDbContext).GetMethod(nameof(ApplyTenantFilter),
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;

    private void ApplyTenantFilter<TEntity>(ModelBuilder modelBuilder)
        where TEntity : class, ITenantOwned
    {
        // Reads _tenantContext.TenantId at query execution time (per query), not at
        // model-build time. A null tenant id yields zero rows.
        Expression<Func<TEntity, bool>> filter =
            e => e.TenantId == _tenantContext.TenantId;
        modelBuilder.Entity<TEntity>().HasQueryFilter(filter);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampTenant();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        StampTenant();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void StampTenant()
    {
        foreach (var entry in ChangeTracker.Entries<ITenantOwned>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    if (!_tenantContext.HasTenant)
                        throw new InvalidOperationException(
                            "Refusing to insert a tenant-owned entity with no active tenant context.");
                    entry.Entity.TenantId = _tenantContext.TenantId!.Value;
                    break;

                case EntityState.Modified:
                    // TenantId can never be changed once set.
                    entry.Property(nameof(ITenantOwned.TenantId)).IsModified = false;
                    break;
            }
        }
    }
}
