using Pos.Domain.Common;

namespace Pos.Domain.Catalog;

public enum TaxBehavior { Inclusive, Exclusive }

public sealed record Category : TenantEntity
{
    public required string Name { get; init; }
    public Guid? ParentCategoryId { get; init; }
}

public sealed record TaxCategory : TenantEntity
{
    public required string Name { get; init; }
    public required string CountryCode { get; init; }
    public required decimal Rate { get; init; }
    public required TaxBehavior TaxBehavior { get; init; }
}

public sealed record Product : TenantEntity
{
    public required Guid CategoryId { get; init; }
    public required Guid TaxCategoryId { get; init; }
    public required string Name { get; init; }
    public required string Sku { get; init; }
    public string? Barcode { get; init; }
    public string? HsnSac { get; init; }
    public required string UnitOfMeasure { get; init; }
    public required Money Price { get; init; }
    public bool IsStockTracked { get; init; } = true;
    public bool IsActive { get; init; } = true;
}
