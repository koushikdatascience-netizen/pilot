using Microsoft.EntityFrameworkCore;

namespace Pos.Infrastructure.Persistence;

public static class TransactionLocks
{
    public static Task LockAsync(CommerceDbContext db, string key, CancellationToken cancellationToken) =>
        db.Database.IsNpgsql()
            ? db.Database.ExecuteSqlInterpolatedAsync($"select pg_advisory_xact_lock(hashtextextended({key}, 0))", cancellationToken)
            : Task.CompletedTask;

    public static Task StockAsync(CommerceDbContext db, Guid tenantId, Guid warehouseId, Guid productId, CancellationToken cancellationToken) =>
        LockAsync(db, $"stock:{tenantId}:{warehouseId}:{productId}", cancellationToken);
}
