namespace Pos.Application.Catalog;

public sealed record ProductResponse(Guid Id, Guid CategoryId, Guid TaxCategoryId, string Name, string Sku, string? Barcode, string? HsnSac, string UnitOfMeasure, decimal Price, string Currency, bool IsStockTracked, bool IsActive);
public sealed record CreateProductRequest(Guid CategoryId, Guid TaxCategoryId, string Name, string Sku, string? Barcode, string? HsnSac, string UnitOfMeasure, decimal Price, string Currency, bool IsStockTracked);
public sealed record UpdateProductRequest(Guid CategoryId, Guid TaxCategoryId, string Name, string Sku, string? Barcode, string? HsnSac, string UnitOfMeasure, decimal Price, string Currency, bool IsStockTracked, bool IsActive);
