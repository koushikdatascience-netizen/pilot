using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Pos.Api;
using Pos.Domain.Catalog;
using Pos.Domain.Common;
using Pos.Domain.Customers;
using Pos.Domain.Inventory;
using Pos.Domain.Operations;
using Pos.Domain.Sales;
using Pos.Domain.Tenancy;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole();
builder.Services.AddProblemDetails();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});
builder.Services.AddSingleton(new SaleCalculator(new IndiaGstTaxCalculator()));
builder.Services.AddSingleton<CommerceStore>();

var app = builder.Build();

app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        var feature = context.Features.Get<IExceptionHandlerFeature>();
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Request could not be completed",
            Detail = feature?.Error.Message
        };
        context.Response.StatusCode = problem.Status.Value;
        await context.Response.WriteAsJsonAsync(problem);
    });
});

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));
app.MapGet("/health/ready", () => Results.Ok(new { status = "ready", database = "postgres-configured" }));
app.MapGet("/openapi/v1.json", () => Results.Json(OpenApiDocumentFactory.Create()));

var api = app.MapGroup("/api/v1");

api.MapGet("/tenancy/bootstrap", (CommerceStore store) => Results.Ok(new
{
    store.Tenant,
    store.Organization,
    store.Business,
    store.Location,
    store.Terminal,
    store.Warehouse
}));

api.MapGet("/categories", (CommerceStore store, HttpRequest request) =>
{
    var tenantId = TenantHeader.GetTenantId(request, store.Tenant.Id);
    return Results.Ok(store.Categories.WhereTenant(tenantId));
});

api.MapGet("/products", (CommerceStore store, HttpRequest request, string? q) =>
{
    var tenantId = TenantHeader.GetTenantId(request, store.Tenant.Id);
    var products = store.Products.WhereTenant(tenantId);
    if (!string.IsNullOrWhiteSpace(q))
    {
        products = products.Where(p =>
            p.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
            p.Sku.Contains(q, StringComparison.OrdinalIgnoreCase) ||
            (p.Barcode?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false));
    }

    return Results.Ok(products.OrderBy(p => p.Name));
});

api.MapPost("/products", (CommerceStore store, HttpRequest request, CreateProductRequest body) =>
{
    var tenantId = TenantHeader.GetTenantId(request, store.Tenant.Id);
    if (body.Price < 0) return Results.ValidationProblem(new Dictionary<string, string[]> { ["price"] = ["Price cannot be negative."] });
    if (store.Products.Any(p => p.TenantId == tenantId && p.Sku.Equals(body.Sku, StringComparison.OrdinalIgnoreCase)))
    {
        return Results.Conflict(new ProblemDetails { Title = "Duplicate SKU", Detail = "SKU must be unique inside a tenant." });
    }

    var product = new Product
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        CategoryId = body.CategoryId,
        TaxCategoryId = body.TaxCategoryId,
        Name = body.Name.Trim(),
        Sku = body.Sku.Trim(),
        Barcode = string.IsNullOrWhiteSpace(body.Barcode) ? null : body.Barcode.Trim(),
        HsnSac = string.IsNullOrWhiteSpace(body.HsnSac) ? null : body.HsnSac.Trim(),
        UnitOfMeasure = body.UnitOfMeasure.Trim(),
        Price = Money.Inr(body.Price),
        IsStockTracked = body.IsStockTracked,
        IsActive = true
    };

    store.Products.Add(product);
    store.OutboxEvents.Add(Outbox.For(tenantId, "ProductCreated", product));
    return Results.Created($"/api/v1/products/{product.Id}", product);
});

api.MapGet("/tax-categories", (CommerceStore store, HttpRequest request) =>
{
    var tenantId = TenantHeader.GetTenantId(request, store.Tenant.Id);
    return Results.Ok(store.TaxCategories.WhereTenant(tenantId));
});

api.MapGet("/customers", (CommerceStore store, HttpRequest request, string? q) =>
{
    var tenantId = TenantHeader.GetTenantId(request, store.Tenant.Id);
    var customers = store.Customers.WhereTenant(tenantId);
    if (!string.IsNullOrWhiteSpace(q))
    {
        customers = customers.Where(c =>
            c.DisplayName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
            (c.Phone?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false));
    }

    return Results.Ok(customers.OrderBy(c => c.DisplayName));
});

api.MapPost("/customers", (CommerceStore store, HttpRequest request, CreateCustomerRequest body) =>
{
    var tenantId = TenantHeader.GetTenantId(request, store.Tenant.Id);
    var customer = new Customer
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        DisplayName = body.DisplayName.Trim(),
        Phone = body.Phone,
        Email = body.Email,
        Gstin = body.Gstin,
        CreditLimit = body.CreditLimit,
        IsWalkIn = false
    };

    store.Customers.Add(customer);
    store.OutboxEvents.Add(Outbox.For(tenantId, "CustomerCreated", customer));
    return Results.Created($"/api/v1/customers/{customer.Id}", customer);
});

api.MapGet("/inventory/balances", (CommerceStore store, HttpRequest request) =>
{
    var tenantId = TenantHeader.GetTenantId(request, store.Tenant.Id);
    var balances = InventoryLedger.BuildBalances(store.InventoryTransactions.WhereTenant(tenantId));
    var response = balances.Select(b => new
    {
        b.TenantId,
        b.WarehouseId,
        b.ProductId,
        ProductName = store.Products.Single(p => p.Id == b.ProductId).Name,
        b.QuantityOnHand
    });
    return Results.Ok(response);
});

api.MapPost("/inventory/opening-stock", (CommerceStore store, HttpRequest request, OpeningStockRequest body) =>
{
    var tenantId = TenantHeader.GetTenantId(request, store.Tenant.Id);
    var movement = new InventoryTransaction
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        LocationId = body.LocationId,
        WarehouseId = body.WarehouseId,
        ProductId = body.ProductId,
        Type = InventoryTransactionType.OpeningStock,
        QuantityDelta = body.Quantity,
        UnitCost = body.UnitCost,
        SourceDocumentType = "OpeningStock",
        SourceDocumentId = Guid.NewGuid(),
        OccurredAtUtc = DateTimeOffset.UtcNow
    };

    store.InventoryTransactions.Add(movement);
    store.OutboxEvents.Add(Outbox.For(tenantId, "StockAdjusted", movement));
    return Results.Created($"/api/v1/inventory/transactions/{movement.Id}", movement);
});

api.MapPost("/sales/complete", (CommerceStore store, SaleCalculator calculator, HttpRequest request, CompleteSaleRequest body) =>
{
    var tenantId = TenantHeader.GetTenantId(request, store.Tenant.Id);
    var saleId = Guid.NewGuid();
    var inputs = body.Lines.Select(line =>
    {
        var product = store.Products.Single(p => p.TenantId == tenantId && p.Id == line.ProductId && p.IsActive);
        var tax = store.TaxCategories.Single(t => t.TenantId == tenantId && t.Id == product.TaxCategoryId);
        return new SaleLineInput(product.Id, product.Name, product.Sku, product.Barcode, line.Quantity, product.Price.Amount, line.DiscountAmount, tax.Rate, tax.TaxBehavior);
    }).ToArray();

    var (lines, totals) = calculator.CalculateLines(tenantId, saleId, inputs, body.IntraState);
    var payments = body.Payments.Select(p => new SalePaymentInput(p.Method, p.Amount, p.Reference)).ToArray();
    SaleCalculator.EnsurePaymentsCoverTotal(totals.GrandTotal, payments);

    foreach (var line in lines)
    {
        var product = store.Products.Single(p => p.Id == line.ProductId);
        if (product.IsStockTracked)
        {
            InventoryLedger.EnsureSufficientStock(tenantId, store.Warehouse.Id, product.Id, line.Quantity, store.InventoryTransactions);
        }
    }

    var paymentEntities = payments.Select(p => new Payment
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        SaleId = saleId,
        Method = p.Method,
        Amount = Money.Round(p.Amount),
        Currency = "INR",
        Status = PaymentStatus.Captured,
        Reference = p.Reference,
        CapturedAtUtc = DateTimeOffset.UtcNow
    }).ToArray();

    var sale = new Sale
    {
        Id = saleId,
        TenantId = tenantId,
        LocationId = body.LocationId,
        TerminalId = body.TerminalId,
        CustomerId = body.CustomerId,
        InvoiceNumber = store.NextInvoiceNumber(),
        Status = SaleStatus.Completed,
        Currency = "INR",
        Subtotal = totals.Subtotal,
        DiscountTotal = totals.DiscountTotal,
        TaxTotal = totals.TaxTotal,
        GrandTotal = totals.GrandTotal,
        ClientGeneratedId = body.ClientGeneratedId == Guid.Empty ? Guid.NewGuid() : body.ClientGeneratedId,
        CompletedAtUtc = DateTimeOffset.UtcNow,
        SyncStatus = SyncStatus.Synced,
        Lines = lines,
        Payments = paymentEntities
    };

    store.Sales.Add(sale);
    foreach (var line in lines)
    {
        store.InventoryTransactions.Add(new InventoryTransaction
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            LocationId = body.LocationId,
            WarehouseId = store.Warehouse.Id,
            ProductId = line.ProductId,
            Type = InventoryTransactionType.Sale,
            QuantityDelta = -line.Quantity,
            SourceDocumentType = "Sale",
            SourceDocumentId = sale.Id,
            OccurredAtUtc = DateTimeOffset.UtcNow
        });
    }

    store.OutboxEvents.Add(Outbox.For(tenantId, "SaleCompleted", sale));
    return Results.Created($"/api/v1/sales/{sale.Id}", sale);
});

api.MapGet("/sales", (CommerceStore store, HttpRequest request) =>
{
    var tenantId = TenantHeader.GetTenantId(request, store.Tenant.Id);
    return Results.Ok(store.Sales.WhereTenant(tenantId).OrderByDescending(s => s.CompletedAtUtc));
});

api.MapGet("/reports/today", (CommerceStore store, HttpRequest request) =>
{
    var tenantId = TenantHeader.GetTenantId(request, store.Tenant.Id);
    var today = DateTimeOffset.UtcNow.Date;
    var sales = store.Sales.WhereTenant(tenantId).Where(s => s.CompletedAtUtc?.UtcDateTime.Date == today).ToArray();
    var lowStock = InventoryLedger.BuildBalances(store.InventoryTransactions.WhereTenant(tenantId))
        .Where(b => b.QuantityOnHand <= 10)
        .Select(b => new { b.ProductId, ProductName = store.Products.Single(p => p.Id == b.ProductId).Name, b.QuantityOnHand })
        .ToArray();

    return Results.Ok(new
    {
        Revenue = Money.Round(sales.Sum(s => s.GrandTotal)),
        TransactionCount = sales.Length,
        AverageOrderValue = sales.Length == 0 ? 0 : Money.Round(sales.Average(s => s.GrandTotal)),
        TopSellingProducts = sales.SelectMany(s => s.Lines)
            .GroupBy(l => new { l.ProductId, l.ProductNameSnapshot })
            .Select(g => new { g.Key.ProductId, ProductName = g.Key.ProductNameSnapshot, Quantity = g.Sum(x => x.Quantity), Revenue = g.Sum(x => x.LineTotal) })
            .OrderByDescending(x => x.Revenue)
            .Take(5),
        LowStockProducts = lowStock
    });
});

app.Run();

public sealed record CreateProductRequest(Guid CategoryId, Guid TaxCategoryId, string Name, string Sku, string? Barcode, string? HsnSac, string UnitOfMeasure, decimal Price, bool IsStockTracked);
public sealed record CreateCustomerRequest(string DisplayName, string? Phone, string? Email, string? Gstin, decimal CreditLimit);
public sealed record OpeningStockRequest(Guid LocationId, Guid WarehouseId, Guid ProductId, decimal Quantity, decimal? UnitCost);
public sealed record CompleteSaleLineRequest(Guid ProductId, decimal Quantity, decimal DiscountAmount);
public sealed record CompleteSalePaymentRequest(PaymentMethod Method, decimal Amount, string? Reference);
public sealed record CompleteSaleRequest(Guid LocationId, Guid TerminalId, Guid? CustomerId, Guid ClientGeneratedId, bool IntraState, IReadOnlyList<CompleteSaleLineRequest> Lines, IReadOnlyList<CompleteSalePaymentRequest> Payments);

static class TenantHeader
{
    public static Guid GetTenantId(HttpRequest request, Guid fallback)
    {
        return request.Headers.TryGetValue("X-Tenant-Id", out var header) && Guid.TryParse(header, out var parsed)
            ? parsed
            : fallback;
    }
}

static class TenantEnumerable
{
    public static IEnumerable<T> WhereTenant<T>(this IEnumerable<T> values, Guid tenantId)
        where T : Pos.Domain.Common.TenantEntity
    {
        return values.Where(v => v.TenantId == tenantId);
    }
}

static class Outbox
{
    public static OutboxEvent For(Guid tenantId, string eventType, object payload)
    {
        return new OutboxEvent
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EventType = eventType,
            PayloadJson = JsonSerializer.Serialize(payload),
            OccurredAtUtc = DateTimeOffset.UtcNow
        };
    }
}
