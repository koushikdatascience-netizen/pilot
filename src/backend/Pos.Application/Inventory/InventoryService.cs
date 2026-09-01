using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pos.Application.Common;
using Pos.Infrastructure.Persistence;
using InventoryTransactionType = Pos.Domain.Inventory.InventoryTransactionType;
using InventoryTransactionEntity = Pos.Infrastructure.Persistence.InventoryTransaction;

namespace Pos.Application.Inventory;

public sealed class InventoryService
{
    private readonly CommerceDbContext _db;
    private readonly ITenantContext _tenant;

    public InventoryService(CommerceDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task<IReadOnlyList<StockBalanceResponse>> BalancesAsync(CancellationToken cancellationToken)
    {
        return await _db.InventoryTransactions
            .AsNoTracking()
            .Where(t => t.TenantId == _tenant.TenantId)
            .GroupBy(t => new { t.WarehouseId, t.ProductId })
            .Select(g => new
            {
                g.Key.WarehouseId,
                g.Key.ProductId,
                QuantityOnHand = g.Sum(x => x.QuantityDelta)
            })
            .Join(_db.Products.AsNoTracking().Where(p => p.TenantId == _tenant.TenantId),
                balance => balance.ProductId,
                product => product.Id,
                (balance, product) => new StockBalanceResponse(balance.WarehouseId, balance.ProductId, product.Name, product.Sku, balance.QuantityOnHand))
            .OrderBy(x => x.ProductName)
            .ToArrayAsync(cancellationToken);
    }

    public async Task<InventoryTransactionResponse> AddOpeningStockAsync(OpeningStockRequest request, CancellationToken cancellationToken)
    {
        Validate(request);
        var location = await _db.Locations.SingleOrDefaultAsync(l => l.TenantId == _tenant.TenantId && l.Id == request.LocationId, cancellationToken)
            ?? throw new NotFoundException("Location was not found for this tenant.");
        var warehouse = await _db.Warehouses.SingleOrDefaultAsync(w => w.TenantId == _tenant.TenantId && w.Id == request.WarehouseId, cancellationToken)
            ?? throw new NotFoundException("Warehouse was not found for this tenant.");
        var product = await _db.Products.SingleOrDefaultAsync(p => p.TenantId == _tenant.TenantId && p.Id == request.ProductId, cancellationToken)
            ?? throw new NotFoundException("Product was not found for this tenant.");

        if (warehouse.LocationId != location.Id) throw new ForbiddenException("Warehouse does not belong to the requested location.");

        var movement = new InventoryTransactionEntity
        {
            Id = Guid.NewGuid(),
            TenantId = _tenant.TenantId,
            LocationId = location.Id,
            WarehouseId = warehouse.Id,
            ProductId = product.Id,
            Type = InventoryTransactionType.OpeningStock,
            QuantityDelta = request.Quantity,
            UnitCost = request.UnitCost,
            SourceDocumentType = "OpeningStock",
            SourceDocumentId = Guid.NewGuid(),
            OccurredAtUtc = DateTimeOffset.UtcNow,
            CreatedByUserId = _tenant.UserId
        };

        _db.InventoryTransactions.Add(movement);
        _db.AuditLogs.Add(Audit("OpeningStockCreated", "InventoryTransaction", movement.Id, movement));
        _db.OutboxEvents.Add(Outbox("StockAdjusted", movement));
        await _db.SaveChangesAsync(cancellationToken);

        return new InventoryTransactionResponse(movement.Id, movement.LocationId, movement.WarehouseId, movement.ProductId, movement.Type, movement.QuantityDelta, movement.UnitCost);
    }

    private static void Validate(OpeningStockRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.Quantity <= 0) errors["quantity"] = ["Opening quantity must be greater than zero."];
        if (request.UnitCost < 0) errors["unitCost"] = ["Unit cost cannot be negative."];
        if (errors.Count > 0) throw new AppValidationException(errors);
    }

    private AuditLog Audit(string action, string entityName, Guid entityId, object after) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = _tenant.TenantId,
        ActorUserId = _tenant.UserId,
        Action = action,
        EntityName = entityName,
        EntityId = entityId,
        OccurredAtUtc = DateTimeOffset.UtcNow,
        AfterJson = JsonSerializer.Serialize(after)
    };

    private OutboxEvent Outbox(string eventType, object payload) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = _tenant.TenantId,
        EventType = eventType,
        PayloadJson = JsonSerializer.Serialize(payload),
        OccurredAtUtc = DateTimeOffset.UtcNow
    };
}
