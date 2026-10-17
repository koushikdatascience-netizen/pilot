using System.Data;
using Microsoft.EntityFrameworkCore;
using Pos.Infrastructure.Persistence;

namespace Pos.Application.Sync;

public sealed class OutboxPublisher(CommerceDbContext db)
{
    public async Task<int> PublishAsync(CancellationToken cancellationToken)
    {
        var tenants = await db.OutboxEvents.AsNoTracking().Where(e => e.ProcessedAtUtc == null)
            .Select(e => e.TenantId).Distinct().Take(20).ToArrayAsync(cancellationToken);
        var published = 0;
        foreach (var tenantId in tenants)
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
            // Serial publication per tenant makes sequence cursors safe across multiple publishers.
            await TransactionLocks.LockAsync(db, $"publish:{tenantId}", cancellationToken);
            var events = await db.OutboxEvents.Where(e => e.TenantId == tenantId && e.ProcessedAtUtc == null)
                .OrderBy(e => e.OccurredAtUtc).ThenBy(e => e.Id).Take(100).ToArrayAsync(cancellationToken);
            foreach (var item in events)
            {
                db.SyncChanges.Add(new Infrastructure.Persistence.SyncChange
                {
                    Id = Guid.NewGuid(), TenantId = tenantId, EventId = item.Id,
                    EventType = item.EventType, PayloadJson = item.PayloadJson
                });
                item.ProcessedAtUtc = DateTimeOffset.UtcNow;
                item.Attempts++;
            }
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            published += events.Length;
        }
        return published;
    }
}
