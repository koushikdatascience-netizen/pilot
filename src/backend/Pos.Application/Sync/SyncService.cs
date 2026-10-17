using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pos.Application.Common;
using Pos.Application.Sales;
using Pos.Domain.Tenancy;
using Pos.Infrastructure.Persistence;
using Pos.Sync.Contracts;

namespace Pos.Application.Sync;

public sealed class SyncService(CommerceDbContext db, ITenantContext tenant, CompleteSaleService sales)
{
    public async Task<DeviceResponse> RegisterAsync(RegisterDeviceRequest request, CancellationToken ct)
    {
        if (request.DeviceId == Guid.Empty) throw Invalid("deviceId", "A permanent device UUID is required.");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        await TransactionLocks.LockAsync(db, $"register:{tenant.TenantId}", ct);
        if (!await db.Terminals.AnyAsync(t => t.TenantId == tenant.TenantId && t.Id == request.TerminalId && t.Status == TerminalStatus.Active, ct))
            throw new NotFoundException("Active terminal was not found.");
        var matches = await db.TerminalDevices.Where(d => d.TenantId == tenant.TenantId &&
            (d.DeviceId == request.DeviceId || d.TerminalId == request.TerminalId)).ToArrayAsync(ct);
        if (matches.Length > 1) throw new ConflictException("This device and terminal already have different registrations.");
        var existing = matches.SingleOrDefault();
        if (existing is not null && (existing.DeviceId != request.DeviceId || existing.TerminalId != request.TerminalId))
            throw new ConflictException("This device or terminal is already registered to another context.");
        var device = existing ?? new TerminalDevice
        {
            Id = Guid.NewGuid(), TenantId = tenant.TenantId, DeviceId = request.DeviceId, TerminalId = request.TerminalId
        };
        device.LastSeenAtUtc = DateTimeOffset.UtcNow;
        if (existing is null)
        {
            db.TerminalDevices.Add(device);
            db.AuditLogs.Add(new AuditLog { Id = Guid.NewGuid(), TenantId = tenant.TenantId, ActorUserId = tenant.UserId,
                Action = "TerminalRegistered", EntityName = "TerminalDevice", EntityId = device.Id, TerminalId = device.TerminalId,
                OccurredAtUtc = DateTimeOffset.UtcNow });
            db.OutboxEvents.Add(new OutboxEvent { Id = Guid.NewGuid(), TenantId = tenant.TenantId, EventType = "TerminalRegistered",
                PayloadJson = JsonSerializer.Serialize(new { device.DeviceId, device.TerminalId }), OccurredAtUtc = DateTimeOffset.UtcNow });
        }
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Response(device);
    }

    public async Task<SyncSnapshot> SnapshotAsync(Guid deviceId, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        var device = await DeviceAsync(deviceId, ct);
        var cursor = await db.SyncChanges.Where(e => e.TenantId == tenant.TenantId).MaxAsync(e => (long?)e.Sequence, ct) ?? 0;
        var products = await db.Products.AsNoTracking().Where(p => p.TenantId == tenant.TenantId).OrderBy(p => p.Id)
            .Select(p => new CatalogProduct(p.Id, p.Name, p.Sku, p.Barcode, p.CategoryId, p.TaxCategoryId, p.HsnSac,
                p.UnitOfMeasure, p.PriceAmount, p.Currency, p.IsStockTracked, p.IsActive, p.Version)).ToArrayAsync(ct);
        var taxes = await db.TaxCategories.AsNoTracking().Where(t => t.TenantId == tenant.TenantId)
            .Select(t => new CatalogTax(t.Id, t.Name, t.Rate, t.TaxBehavior.ToString())).ToArrayAsync(ct);
        await transaction.CommitAsync(ct);
        // Offer the captured checkpoint after the read snapshot, under the same lock as acknowledgement.
        await using var offering = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        await TransactionLocks.LockAsync(db, $"device:{tenant.TenantId}:{deviceId}", ct);
        await db.Entry(device).ReloadAsync(ct);
        device.OfferedSequence = Math.Max(device.OfferedSequence, cursor);
        device.LastSeenAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await offering.CommitAsync(ct);
        return new SyncSnapshot(1, tenant.TenantId, device.TerminalId, device.DeviceId, cursor, products, taxes);
    }

    public async Task<ChangePage> ChangesAsync(Guid deviceId, long after, int limit, CancellationToken ct)
    {
        if (after < 0) throw Invalid("after", "Cursor cannot be negative.");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        await TransactionLocks.LockAsync(db, $"device:{tenant.TenantId}:{deviceId}", ct);
        var device = await DeviceAsync(deviceId, ct);
        var high = await db.SyncChanges.Where(e => e.TenantId == tenant.TenantId).MaxAsync(e => (long?)e.Sequence, ct) ?? 0;
        if (after > high) throw Invalid("after", "Cursor exceeds this tenant's published feed.");
        limit = Math.Clamp(limit, 1, 500);
        var events = await db.SyncChanges.AsNoTracking().Where(e => e.TenantId == tenant.TenantId && e.Sequence > after)
            .OrderBy(e => e.Sequence).Take(limit + 1).ToArrayAsync(ct);
        var items = events.Take(limit).Select(e => new Pos.Sync.Contracts.SyncChange(e.Id, e.Sequence, e.EventId, e.SchemaVersion,
            e.EventType, JsonSerializer.Deserialize<JsonElement>(e.PayloadJson))).ToArray();
        var cursor = items.LastOrDefault()?.Sequence ?? after;
        device.OfferedSequence = Math.Max(device.OfferedSequence, cursor);
        device.LastSeenAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new ChangePage(1, cursor, events.Length > limit, items);
    }

    public async Task<DeviceResponse> AcknowledgeAsync(Guid deviceId, AcknowledgeRequest request, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        await TransactionLocks.LockAsync(db, $"device:{tenant.TenantId}:{deviceId}", ct);
        var device = await DeviceAsync(deviceId, ct);
        if (request.Sequence < 0 || request.Sequence > device.OfferedSequence)
            throw Invalid("sequence", "Only a delivered checkpoint can be acknowledged.");
        device.AcknowledgedSequence = Math.Max(device.AcknowledgedSequence, request.Sequence);
        device.LastSeenAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Response(device);
    }

    public async Task<SaleResponse> UploadAsync(Guid deviceId, CompleteSaleRequest request, CancellationToken ct)
    {
        var device = await DeviceAsync(deviceId, ct);
        if (request.TerminalId != device.TerminalId) throw new ForbiddenException("Sale terminal does not match the registered device.");
        return await sales.CompleteAsync(request, ct);
    }

    private async Task<TerminalDevice> DeviceAsync(Guid id, CancellationToken ct)
    {
        var device = await db.TerminalDevices.SingleOrDefaultAsync(d => d.TenantId == tenant.TenantId && d.DeviceId == id, ct)
            ?? throw new NotFoundException("Device was not registered for this tenant.");
        if (!await db.Terminals.AnyAsync(t => t.TenantId == tenant.TenantId && t.Id == device.TerminalId && t.Status == TerminalStatus.Active, ct))
            throw new ForbiddenException("Registered terminal is disabled.");
        return device;
    }

    private static DeviceResponse Response(TerminalDevice d) => new(d.DeviceId, d.TerminalId, d.AcknowledgedSequence, d.LastSeenAtUtc);
    private static AppValidationException Invalid(string field, string message) => new(new Dictionary<string, string[]> { [field] = [message] });
}
