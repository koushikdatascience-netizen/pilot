using Pos.Domain.Inventory;

namespace Pos.Application.Inventory;

public sealed record StockBalanceResponse(Guid WarehouseId, Guid ProductId, string ProductName, string Sku, decimal QuantityOnHand);
public sealed record OpeningStockRequest(Guid LocationId, Guid WarehouseId, Guid ProductId, decimal Quantity, decimal? UnitCost);
public sealed record InventoryTransactionResponse(Guid Id, Guid LocationId, Guid WarehouseId, Guid ProductId, InventoryTransactionType Type, decimal QuantityDelta, decimal? UnitCost);
