using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pos.Application.Common;
using Pos.Domain.Common;
using Pos.Infrastructure.Persistence;

namespace Pos.Application.Catalog;

public sealed class ProductService
{
    private readonly CommerceDbContext _db;
    private readonly ITenantContext _tenant;

    public ProductService(CommerceDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task<PagedResponse<ProductResponse>> ListAsync(string? q, string? barcode, string? sku, int page, int pageSize, bool includeInactive, CancellationToken cancellationToken)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var query = _db.Products.AsNoTracking().Where(p => p.TenantId == _tenant.TenantId && (includeInactive || p.IsActive));

        if (!string.IsNullOrWhiteSpace(barcode))
        {
            var exactBarcode = barcode.Trim();
            query = query.Where(p => p.Barcode == exactBarcode);
        }

        if (!string.IsNullOrWhiteSpace(sku))
        {
            var exactSku = sku.Trim();
            query = query.Where(p => p.Sku == exactSku);
        }

        if (!string.IsNullOrWhiteSpace(q))
        {
            var needle = q.Trim().ToLowerInvariant();
            query = query.Where(p => p.Name.ToLower().Contains(needle) || p.Sku.ToLower().Contains(needle) || (p.Barcode != null && p.Barcode.Contains(needle)));
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(p => p.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => ToResponse(p))
            .ToArrayAsync(cancellationToken);

        return new PagedResponse<ProductResponse>(items, page, pageSize, total);
    }

    public async Task<ProductResponse> CreateAsync(CreateProductRequest request, CancellationToken cancellationToken)
    {
        Validate(request);
        if (!string.IsNullOrWhiteSpace(request.Barcode) && await _db.Products.AnyAsync(p => p.TenantId == _tenant.TenantId && p.Barcode == request.Barcode.Trim(), cancellationToken))
            throw new ConflictException("Barcode already exists for this tenant.");
        var categoryExists = await _db.Categories.AnyAsync(c => c.TenantId == _tenant.TenantId && c.Id == request.CategoryId, cancellationToken);
        var taxExists = await _db.TaxCategories.AnyAsync(t => t.TenantId == _tenant.TenantId && t.Id == request.TaxCategoryId, cancellationToken);
        if (!categoryExists) throw new NotFoundException("Category was not found for this tenant.");
        if (!taxExists) throw new NotFoundException("Tax category was not found for this tenant.");
        if (await _db.Products.AnyAsync(p => p.TenantId == _tenant.TenantId && p.Sku == request.Sku.Trim(), cancellationToken))
        {
            throw new ConflictException("SKU already exists for this tenant.");
        }

        var product = new Product
        {
            Id = Guid.NewGuid(),
            TenantId = _tenant.TenantId,
            CategoryId = request.CategoryId,
            TaxCategoryId = request.TaxCategoryId,
            Name = request.Name.Trim(),
            Sku = request.Sku.Trim(),
            Barcode = string.IsNullOrWhiteSpace(request.Barcode) ? null : request.Barcode.Trim(),
            HsnSac = string.IsNullOrWhiteSpace(request.HsnSac) ? null : request.HsnSac.Trim(),
            UnitOfMeasure = request.UnitOfMeasure.Trim(),
            PriceAmount = Money.Round(request.Price),
            Currency = request.Currency.Trim().ToUpperInvariant(),
            IsStockTracked = request.IsStockTracked,
            IsActive = true
        };

        _db.Products.Add(product);
        _db.AuditLogs.Add(Audit("ProductCreated", "Product", product.Id, null, product));
        _db.OutboxEvents.Add(Outbox("ProductCreated", product));
        await _db.SaveChangesAsync(cancellationToken);
        return ToResponse(product);
    }

    public async Task<ProductResponse> UpdateAsync(Guid id, UpdateProductRequest request, CancellationToken cancellationToken)
    {
        Validate(request);
        var product = await _db.Products.SingleOrDefaultAsync(p => p.TenantId == _tenant.TenantId && p.Id == id, cancellationToken)
            ?? throw new NotFoundException("Product was not found for this tenant.");

        var categoryExists = await _db.Categories.AnyAsync(c => c.TenantId == _tenant.TenantId && c.Id == request.CategoryId, cancellationToken);
        var taxExists = await _db.TaxCategories.AnyAsync(t => t.TenantId == _tenant.TenantId && t.Id == request.TaxCategoryId, cancellationToken);
        if (!categoryExists) throw new NotFoundException("Category was not found for this tenant.");
        if (!taxExists) throw new NotFoundException("Tax category was not found for this tenant.");

        var sku = request.Sku.Trim();
        var barcode = string.IsNullOrWhiteSpace(request.Barcode) ? null : request.Barcode.Trim();
        if (await _db.Products.AnyAsync(p => p.TenantId == _tenant.TenantId && p.Id != id && p.Sku == sku, cancellationToken))
        {
            throw new ConflictException("SKU already exists for this tenant.");
        }

        if (barcode is not null && await _db.Products.AnyAsync(p => p.TenantId == _tenant.TenantId && p.Id != id && p.Barcode == barcode, cancellationToken))
        {
            throw new ConflictException("Barcode already exists for this tenant.");
        }

        var before = new { product.Name, product.Sku, product.Barcode, product.PriceAmount, product.IsActive };
        product.CategoryId = request.CategoryId;
        product.TaxCategoryId = request.TaxCategoryId;
        product.Name = request.Name.Trim();
        product.Sku = sku;
        product.Barcode = barcode;
        product.HsnSac = string.IsNullOrWhiteSpace(request.HsnSac) ? null : request.HsnSac.Trim();
        product.UnitOfMeasure = request.UnitOfMeasure.Trim();
        product.PriceAmount = Money.Round(request.Price);
        product.Currency = request.Currency.Trim().ToUpperInvariant();
        product.IsStockTracked = request.IsStockTracked;
        product.IsActive = request.IsActive;

        _db.AuditLogs.Add(Audit("ProductUpdated", "Product", product.Id, before, product));
        _db.OutboxEvents.Add(Outbox("ProductUpdated", product));
        await _db.SaveChangesAsync(cancellationToken);
        return ToResponse(product);
    }

    public async Task<ProductResponse> SetActiveAsync(Guid id, bool isActive, CancellationToken cancellationToken)
    {
        var product = await _db.Products.SingleOrDefaultAsync(p => p.TenantId == _tenant.TenantId && p.Id == id, cancellationToken)
            ?? throw new NotFoundException("Product was not found for this tenant.");
        var before = new { product.IsActive };
        product.IsActive = isActive;
        _db.AuditLogs.Add(Audit(isActive ? "ProductActivated" : "ProductDeactivated", "Product", product.Id, before, product));
        _db.OutboxEvents.Add(Outbox(isActive ? "ProductActivated" : "ProductDeactivated", product));
        await _db.SaveChangesAsync(cancellationToken);
        return ToResponse(product);
    }

    private static ProductResponse ToResponse(Product p) => new(p.Id, p.CategoryId, p.TaxCategoryId, p.Name, p.Sku, p.Barcode, p.HsnSac, p.UnitOfMeasure, p.PriceAmount, p.Currency, p.IsStockTracked, p.IsActive);

    private static void Validate(CreateProductRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.Name)) errors["name"] = ["Name is required."];
        if (string.IsNullOrWhiteSpace(request.Sku)) errors["sku"] = ["SKU is required."];
        if (string.IsNullOrWhiteSpace(request.UnitOfMeasure)) errors["unitOfMeasure"] = ["Unit of measure is required."];
        if (request.Price < 0) errors["price"] = ["Price cannot be negative."];
        if (!string.Equals(request.Currency, "INR", StringComparison.OrdinalIgnoreCase)) errors["currency"] = ["Only INR is supported in Phase 1."];
        if (errors.Count > 0) throw new AppValidationException(errors);
    }

    private static void Validate(UpdateProductRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.Name)) errors["name"] = ["Name is required."];
        if (string.IsNullOrWhiteSpace(request.Sku)) errors["sku"] = ["SKU is required."];
        if (string.IsNullOrWhiteSpace(request.UnitOfMeasure)) errors["unitOfMeasure"] = ["Unit of measure is required."];
        if (request.Price < 0) errors["price"] = ["Price cannot be negative."];
        if (!string.Equals(request.Currency, "INR", StringComparison.OrdinalIgnoreCase)) errors["currency"] = ["Only INR is supported in Phase 2."];
        if (errors.Count > 0) throw new AppValidationException(errors);
    }

    private AuditLog Audit(string action, string entityName, Guid entityId, object? before, object after) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = _tenant.TenantId,
        ActorUserId = _tenant.UserId,
        Action = action,
        EntityName = entityName,
        EntityId = entityId,
        OccurredAtUtc = DateTimeOffset.UtcNow,
        BeforeJson = before is null ? null : JsonSerializer.Serialize(before),
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
