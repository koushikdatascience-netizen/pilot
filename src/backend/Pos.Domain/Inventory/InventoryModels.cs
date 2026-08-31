using Pos.Domain.Common;

namespace Pos.Domain.Inventory;

public enum InventoryTransactionType
{
    OpeningStock,
    PurchaseReceipt,
    Sale,
    SaleReturn,
    PurchaseReturn,
    TransferOut,
    TransferIn,
    AdjustmentIncrease,
    AdjustmentDecrease,
    Damage,
    Wastage
}

public sealed record InventoryTransaction : TenantEntity
{
    public required Guid LocationId { get; init; }
    public required Guid WarehouseId { get; init; }
    public required Guid ProductId { get; init; }
    public required InventoryTransactionType Type { get; init; }
    public required decimal QuantityDelta { get; init; }
    public decimal? UnitCost { get; init; }
    public string? SourceDocumentType { get; init; }
    public Guid? SourceDocumentId { get; init; }
    public required DateTimeOffset OccurredAtUtc { get; init; }
    public Guid? CreatedByUserId { get; init; }
}

public sealed record StockBalance(Guid TenantId, Guid WarehouseId, Guid ProductId, decimal QuantityOnHand);

public sealed class InventoryLedger
{
    public static IReadOnlyList<StockBalance> BuildBalances(IEnumerable<InventoryTransaction> transactions)
    {
        return transactions
            .GroupBy(t => new { t.TenantId, t.WarehouseId, t.ProductId })
            .Select(g => new StockBalance(g.Key.TenantId, g.Key.WarehouseId, g.Key.ProductId, g.Sum(x => x.QuantityDelta)))
            .OrderBy(b => b.ProductId)
            .ToArray();
    }

    public static void EnsureSufficientStock(Guid tenantId, Guid warehouseId, Guid productId, decimal requestedQuantity, IEnumerable<InventoryTransaction> transactions)
    {
        var available = BuildBalances(transactions)
            .SingleOrDefault(b => b.TenantId == tenantId && b.WarehouseId == warehouseId && b.ProductId == productId)
            ?.QuantityOnHand ?? 0;

        if (available < requestedQuantity)
        {
            throw new InvalidOperationException($"Insufficient stock for product {productId}. Available {available}, requested {requestedQuantity}.");
        }
    }
}
