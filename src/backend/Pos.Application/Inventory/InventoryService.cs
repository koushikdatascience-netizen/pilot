using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pos.Application.Common;
using Pos.Infrastructure.Persistence;
using InventoryTransactionType = Pos.Domain.Inventory.InventoryTransactionType;
using InventoryTransactionEntity = Pos.Infrastructure.Persistence.InventoryTransaction;

namespace Pos.Application.Inventory;

public sealed class InventoryService
{
    private readonly CommerceDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly ILogger<InventoryService> _logger;

    public InventoryService(CommerceDbContext db, ITenantContext tenant, ILogger<InventoryService> logger)
    {
        _db = db;
        _tenant = tenant;
        _logger = logger;
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
                (balance, product) => new { balance.WarehouseId, balance.ProductId, ProductName = product.Name, product.Sku, balance.QuantityOnHand })
            .OrderBy(x => x.ProductName)
            .Select(x => new StockBalanceResponse(x.WarehouseId, x.ProductId, x.ProductName, x.Sku, x.QuantityOnHand))
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

    public async Task<InventoryTransactionResponse> AdjustAsync(StockAdjustmentRequest request, CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            try { return await AdjustOnceAsync(request, cancellationToken); }
            catch (Exception ex) when (DatabaseConcurrency.IsSerializationFailure(ex))
            {
                _logger.LogWarning(ex, "Inventory adjustment concurrency failure. Attempt {Attempt}. TenantId={TenantId} ProductId={ProductId}", attempt, _tenant.TenantId, request.ProductId);
                if (attempt == 5) throw new ConflictException("Stock changed concurrently. Please retry the adjustment.");
                _db.ChangeTracker.Clear();
                await Task.Delay(TimeSpan.FromMilliseconds(Random.Shared.Next(150, 350) * (1 << (attempt - 1))), cancellationToken);
            }
        }
        throw new ConflictException("Stock changed concurrently. Please retry the adjustment.");
    }

    private async Task<InventoryTransactionResponse> AdjustOnceAsync(StockAdjustmentRequest request, CancellationToken cancellationToken)
    {
        Validate(request);
        // All stock-decreasing writers must participate in PostgreSQL's serializable conflict detection.
        await using var transaction = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
        await TransactionLocks.StockAsync(_db, _tenant.TenantId, request.WarehouseId, request.ProductId, cancellationToken);
        var location = await _db.Locations.SingleOrDefaultAsync(l => l.TenantId == _tenant.TenantId && l.Id == request.LocationId, cancellationToken)
            ?? throw new NotFoundException("Location was not found for this tenant.");
        var warehouse = await _db.Warehouses.SingleOrDefaultAsync(w => w.TenantId == _tenant.TenantId && w.Id == request.WarehouseId, cancellationToken)
            ?? throw new NotFoundException("Warehouse was not found for this tenant.");
        var product = await _db.Products.SingleOrDefaultAsync(p => p.TenantId == _tenant.TenantId && p.Id == request.ProductId, cancellationToken)
            ?? throw new NotFoundException("Product was not found for this tenant.");

        if (warehouse.LocationId != location.Id) throw new ForbiddenException("Warehouse does not belong to the requested location.");

        var delta = request.Type == InventoryTransactionType.AdjustmentIncrease ? request.Quantity : -request.Quantity;
        if (delta < 0)
        {
            var available = await _db.InventoryTransactions
                .Where(t => t.TenantId == _tenant.TenantId && t.WarehouseId == warehouse.Id && t.ProductId == product.Id)
                .SumAsync(t => t.QuantityDelta, cancellationToken);
            if (available < request.Quantity) throw new ConflictException("Insufficient stock for inventory adjustment.");
        }

        var movement = new InventoryTransactionEntity
        {
            Id = Guid.NewGuid(),
            TenantId = _tenant.TenantId,
            LocationId = location.Id,
            WarehouseId = warehouse.Id,
            ProductId = product.Id,
            Type = request.Type,
            QuantityDelta = delta,
            UnitCost = request.UnitCost,
            SourceDocumentType = string.IsNullOrWhiteSpace(request.Reason) ? "ManualAdjustment" : request.Reason.Trim(),
            SourceDocumentId = Guid.NewGuid(),
            OccurredAtUtc = DateTimeOffset.UtcNow,
            CreatedByUserId = _tenant.UserId
        };

        _db.InventoryTransactions.Add(movement);
        _db.AuditLogs.Add(Audit("StockAdjusted", "InventoryTransaction", movement.Id, movement));
        _db.OutboxEvents.Add(Outbox("StockAdjusted", movement));
        await _db.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return new InventoryTransactionResponse(movement.Id, movement.LocationId, movement.WarehouseId, movement.ProductId, movement.Type, movement.QuantityDelta, movement.UnitCost);
    }

    private static void Validate(OpeningStockRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.Quantity <= 0) errors["quantity"] = ["Opening quantity must be greater than zero."];
        if (request.Quantity != decimal.Round(request.Quantity, 3)) errors["quantity"] = ["Quantities support at most three decimal places."];
        if (request.UnitCost < 0) errors["unitCost"] = ["Unit cost cannot be negative."];
        if (errors.Count > 0) throw new AppValidationException(errors);
    }

    private static void Validate(StockAdjustmentRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.Quantity <= 0) errors["quantity"] = ["Adjustment quantity must be greater than zero."];
        if (request.Quantity != decimal.Round(request.Quantity, 3)) errors["quantity"] = ["Quantities support at most three decimal places."];
        if (request.UnitCost < 0) errors["unitCost"] = ["Unit cost cannot be negative."];
        if (request.Type is not (InventoryTransactionType.AdjustmentIncrease or InventoryTransactionType.AdjustmentDecrease or InventoryTransactionType.Damage))
        {
            errors["type"] = ["Only AdjustmentIncrease, AdjustmentDecrease, and Damage are supported for manual adjustments."];
        }
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
