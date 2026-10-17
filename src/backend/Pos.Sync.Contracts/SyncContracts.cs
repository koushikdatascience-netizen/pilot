using System.Text.Json;

namespace Pos.Sync.Contracts;

public sealed record RegisterDeviceRequest(Guid TerminalId, Guid DeviceId);
public sealed record DeviceResponse(Guid DeviceId, Guid TerminalId, long AcknowledgedSequence, DateTimeOffset LastSeenAtUtc);
public sealed record AcknowledgeRequest(long Sequence);
public sealed record CatalogProduct(Guid Id, string Name, string Sku, string? Barcode, Guid CategoryId, Guid TaxCategoryId,
    string? HsnSac, string UnitOfMeasure, decimal PriceAmount, string Currency, bool IsStockTracked, bool IsActive, long Version);
public sealed record CatalogTax(Guid Id, string Name, decimal Rate, string TaxBehavior);
public sealed record SyncSnapshot(int SchemaVersion, Guid TenantId, Guid TerminalId, Guid DeviceId, long Cursor,
    IReadOnlyList<CatalogProduct> Products, IReadOnlyList<CatalogTax> Taxes);
public sealed record SyncChange(Guid Id, long Sequence, Guid EventId, int SchemaVersion, string EventType, JsonElement Payload);
public sealed record ChangePage(int SchemaVersion, long NextCursor, bool HasMore, IReadOnlyList<SyncChange> Changes);
