using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pos.Application.Common;
using Pos.Domain.Common;
using Pos.Domain.Sales;
using Pos.Infrastructure.Persistence;
using InventoryTransactionType = Pos.Domain.Inventory.InventoryTransactionType;
using InventoryTransactionEntity = Pos.Infrastructure.Persistence.InventoryTransaction;
using PaymentEntity = Pos.Infrastructure.Persistence.Payment;
using SaleEntity = Pos.Infrastructure.Persistence.Sale;
using SaleLineEntity = Pos.Infrastructure.Persistence.SaleLine;

namespace Pos.Application.Sales;

public sealed class CompleteSaleService
{
    private readonly CommerceDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly SaleCalculator _calculator;

    public CompleteSaleService(CommerceDbContext db, ITenantContext tenant, SaleCalculator calculator)
    {
        _db = db;
        _tenant = tenant;
        _calculator = calculator;
    }

    public async Task<SaleResponse> CompleteAsync(CompleteSaleRequest request, CancellationToken cancellationToken)
    {
        ValidateRequest(request);
        var existing = await FindExistingAsync(request.TerminalId, request.ClientGeneratedId, cancellationToken);
        if (existing is not null) return existing with { IdempotentReplay = true };

        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        existing = await FindExistingAsync(request.TerminalId, request.ClientGeneratedId, cancellationToken);
        if (existing is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return existing with { IdempotentReplay = true };
        }

        var location = await _db.Locations.SingleOrDefaultAsync(l => l.TenantId == _tenant.TenantId && l.Id == request.LocationId, cancellationToken)
            ?? throw new NotFoundException("Location was not found for this tenant.");
        var terminal = await _db.Terminals.SingleOrDefaultAsync(t => t.TenantId == _tenant.TenantId && t.Id == request.TerminalId, cancellationToken)
            ?? throw new NotFoundException("Terminal was not found for this tenant.");
        if (terminal.LocationId != location.Id) throw new ForbiddenException("Terminal does not belong to the requested location.");

        var warehouse = await _db.Warehouses.SingleOrDefaultAsync(w => w.TenantId == _tenant.TenantId && w.Id == location.DefaultSalesWarehouseId, cancellationToken)
            ?? throw new NotFoundException("Default sales warehouse was not found for this location.");
        if (warehouse.LocationId != location.Id) throw new ForbiddenException("Default sales warehouse does not belong to the requested location.");

        Customer? customer = null;
        if (request.CustomerId is { } customerId)
        {
            customer = await _db.Customers.SingleOrDefaultAsync(c => c.TenantId == _tenant.TenantId && c.Id == customerId, cancellationToken)
                ?? throw new NotFoundException("Customer was not found for this tenant.");
        }

        var productIds = request.Lines.Select(l => l.ProductId).Distinct().ToArray();
        var products = await _db.Products.Where(p => p.TenantId == _tenant.TenantId && productIds.Contains(p.Id) && p.IsActive).ToDictionaryAsync(p => p.Id, cancellationToken);
        if (products.Count != productIds.Length) throw new NotFoundException("One or more products were not found for this tenant.");

        var taxIds = products.Values.Select(p => p.TaxCategoryId).Distinct().ToArray();
        var taxes = await _db.TaxCategories.Where(t => t.TenantId == _tenant.TenantId && taxIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, cancellationToken);
        if (taxes.Count != taxIds.Length) throw new NotFoundException("One or more tax categories were not found for this tenant.");

        var saleId = Guid.NewGuid();
        var intraState = IsIntraState(location, customer);
        var lineInputs = request.Lines.Select(line =>
        {
            var product = products[line.ProductId];
            var tax = taxes[product.TaxCategoryId];
            return new SaleLineInput(product.Id, product.Name, product.Sku, product.Barcode, line.Quantity, product.PriceAmount, line.DiscountAmount, tax.Rate, tax.TaxBehavior);
        }).ToArray();

        var (domainLines, totals) = _calculator.CalculateLines(_tenant.TenantId, saleId, lineInputs, intraState);
        SaleCalculator.EnsurePaymentsCoverTotal(totals.GrandTotal, request.Payments.Select(p => new SalePaymentInput(p.Method, p.Amount, p.Reference)).ToArray());

        await ValidateStockAsync(warehouse.Id, request.Lines, products, cancellationToken);

        var invoiceNumber = await AllocateInvoiceNumberAsync(location, cancellationToken);
        var completedAt = DateTimeOffset.UtcNow;
        var sale = new SaleEntity
        {
            Id = saleId,
            TenantId = _tenant.TenantId,
            LocationId = location.Id,
            TerminalId = terminal.Id,
            CustomerId = customer?.Id,
            InvoiceNumber = invoiceNumber,
            Status = SaleStatus.Completed,
            Currency = "INR",
            Subtotal = totals.Subtotal,
            DiscountTotal = totals.DiscountTotal,
            TaxTotal = totals.TaxTotal,
            GrandTotal = totals.GrandTotal,
            ClientGeneratedId = request.ClientGeneratedId,
            CompletedAtUtc = completedAt,
            SyncStatus = SyncStatus.Synced
        };
        _db.Sales.Add(sale);

        var saleLines = domainLines.Select(l => new SaleLineEntity
        {
            Id = l.Id,
            TenantId = _tenant.TenantId,
            SaleId = saleId,
            ProductId = l.ProductId,
            ProductNameSnapshot = l.ProductNameSnapshot,
            SkuSnapshot = l.SkuSnapshot,
            BarcodeSnapshot = l.BarcodeSnapshot,
            Quantity = l.Quantity,
            UnitPrice = l.UnitPrice,
            DiscountAmount = l.DiscountAmount,
            TaxableAmount = l.TaxableAmount,
            TaxRate = l.TaxRate,
            TaxAmount = l.TaxAmount,
            CgstAmount = l.CgstAmount,
            SgstAmount = l.SgstAmount,
            IgstAmount = l.IgstAmount,
            LineTotal = l.LineTotal
        }).ToArray();
        _db.SaleLines.AddRange(saleLines);

        var payments = request.Payments.Select(p => new PaymentEntity
        {
            Id = Guid.NewGuid(),
            TenantId = _tenant.TenantId,
            SaleId = saleId,
            Method = p.Method,
            Amount = Money.Round(p.Amount),
            Currency = "INR",
            Status = PaymentStatus.Captured,
            Reference = string.IsNullOrWhiteSpace(p.Reference) ? null : p.Reference.Trim(),
            CapturedAtUtc = completedAt
        }).ToArray();
        _db.Payments.AddRange(payments);

        foreach (var line in saleLines.Where(l => products[l.ProductId].IsStockTracked))
        {
            _db.InventoryTransactions.Add(new InventoryTransactionEntity
            {
                Id = Guid.NewGuid(),
                TenantId = _tenant.TenantId,
                LocationId = location.Id,
                WarehouseId = warehouse.Id,
                ProductId = line.ProductId,
                Type = InventoryTransactionType.Sale,
                QuantityDelta = -line.Quantity,
                SourceDocumentType = "Sale",
                SourceDocumentId = saleId,
                OccurredAtUtc = completedAt,
                CreatedByUserId = _tenant.UserId
            });
        }

        _db.AuditLogs.Add(Audit("SaleCompleted", "Sale", saleId, terminal.Id, sale));
        _db.OutboxEvents.Add(Outbox("SaleCompleted", sale));
        foreach (var payment in payments) _db.OutboxEvents.Add(Outbox("PaymentRecorded", payment));
        foreach (var line in saleLines.Where(l => products[l.ProductId].IsStockTracked)) _db.OutboxEvents.Add(Outbox("InventoryReduced", line));

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await LoadSaleAsync(saleId, idempotentReplay: false, cancellationToken);
    }

    public async Task<IReadOnlyList<SaleResponse>> ListAsync(CancellationToken cancellationToken)
    {
        var saleIds = await _db.Sales.AsNoTracking()
            .Where(s => s.TenantId == _tenant.TenantId)
            .OrderByDescending(s => s.CompletedAtUtc)
            .Select(s => s.Id)
            .ToArrayAsync(cancellationToken);

        var sales = new List<SaleResponse>();
        foreach (var id in saleIds)
        {
            sales.Add(await LoadSaleAsync(id, false, cancellationToken));
        }

        return sales;
    }

    private async Task<SaleResponse?> FindExistingAsync(Guid terminalId, Guid clientGeneratedId, CancellationToken cancellationToken)
    {
        var saleId = await _db.Sales.AsNoTracking()
            .Where(s => s.TenantId == _tenant.TenantId && s.TerminalId == terminalId && s.ClientGeneratedId == clientGeneratedId)
            .Select(s => (Guid?)s.Id)
            .SingleOrDefaultAsync(cancellationToken);
        return saleId is null ? null : await LoadSaleAsync(saleId.Value, true, cancellationToken);
    }

    private async Task<SaleResponse> LoadSaleAsync(Guid saleId, bool idempotentReplay, CancellationToken cancellationToken)
    {
        var sale = await _db.Sales.AsNoTracking().SingleAsync(s => s.TenantId == _tenant.TenantId && s.Id == saleId, cancellationToken);
        var lines = await _db.SaleLines.AsNoTracking()
            .Where(l => l.TenantId == _tenant.TenantId && l.SaleId == saleId)
            .OrderBy(l => l.ProductNameSnapshot)
            .Select(l => new SaleLineResponse(l.Id, l.ProductId, l.ProductNameSnapshot, l.SkuSnapshot, l.BarcodeSnapshot, l.Quantity, l.UnitPrice, l.DiscountAmount, l.TaxableAmount, l.TaxRate, l.TaxAmount, l.CgstAmount, l.SgstAmount, l.IgstAmount, l.LineTotal))
            .ToArrayAsync(cancellationToken);
        var payments = await _db.Payments.AsNoTracking()
            .Where(p => p.TenantId == _tenant.TenantId && p.SaleId == saleId)
            .Select(p => new PaymentResponse(p.Id, p.Method, p.Amount, p.Currency, p.Status, p.Reference))
            .ToArrayAsync(cancellationToken);

        return new SaleResponse(sale.Id, sale.LocationId, sale.TerminalId, sale.CustomerId, sale.InvoiceNumber, sale.Status, sale.Currency, sale.Subtotal, sale.DiscountTotal, sale.TaxTotal, sale.GrandTotal, sale.ClientGeneratedId, sale.CompletedAtUtc, sale.SyncStatus, lines, payments, idempotentReplay);
    }

    private async Task ValidateStockAsync(Guid warehouseId, IReadOnlyList<CompleteSaleLineRequest> lines, IReadOnlyDictionary<Guid, Pos.Infrastructure.Persistence.Product> products, CancellationToken cancellationToken)
    {
        var required = lines
            .Where(l => products[l.ProductId].IsStockTracked)
            .GroupBy(l => l.ProductId)
            .Select(g => new { ProductId = g.Key, Quantity = g.Sum(x => x.Quantity) })
            .ToArray();

        foreach (var item in required)
        {
            var available = await _db.InventoryTransactions
                .Where(t => t.TenantId == _tenant.TenantId && t.WarehouseId == warehouseId && t.ProductId == item.ProductId)
                .SumAsync(t => t.QuantityDelta, cancellationToken);

            if (available < item.Quantity)
            {
                throw new ConflictException($"Insufficient stock for product {item.ProductId}.");
            }
        }
    }

    private async Task<string> AllocateInvoiceNumberAsync(Location location, CancellationToken cancellationToken)
    {
        var period = DateTimeOffset.UtcNow.ToString("yyyyMM");
        var prefix = $"{location.Code}-{period}";
        var sequence = await _db.InvoiceSequences.SingleOrDefaultAsync(s =>
            s.TenantId == _tenant.TenantId &&
            s.LocationId == location.Id &&
            s.FinancialPeriod == period &&
            s.Prefix == prefix, cancellationToken);

        if (sequence is null)
        {
            sequence = new InvoiceSequence
            {
                Id = Guid.NewGuid(),
                TenantId = _tenant.TenantId,
                LocationId = location.Id,
                FinancialPeriod = period,
                Prefix = prefix,
                NextNumber = 1
            };
            _db.InvoiceSequences.Add(sequence);
        }

        var number = sequence.NextNumber;
        sequence.NextNumber += 1;
        sequence.UpdatedAtUtc = DateTimeOffset.UtcNow;
        return $"{prefix}-{number:0000}";
    }

    private static bool IsIntraState(Location location, Customer? customer)
    {
        if (string.IsNullOrWhiteSpace(customer?.Gstin)) return true;
        var customerStateCode = customer.Gstin[..Math.Min(2, customer.Gstin.Length)];
        return string.Equals(customerStateCode, location.StateCode, StringComparison.OrdinalIgnoreCase);
    }

    private static void ValidateRequest(CompleteSaleRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.ClientGeneratedId == Guid.Empty) errors["clientGeneratedId"] = ["Client-generated ID is required."];
        if (request.Lines.Count == 0) errors["lines"] = ["At least one sale line is required."];
        if (request.Payments.Count == 0) errors["payments"] = ["At least one payment is required."];
        if (request.Lines.Any(l => l.Quantity <= 0)) errors["quantity"] = ["Line quantities must be greater than zero."];
        if (request.Lines.Any(l => l.DiscountAmount < 0)) errors["discount"] = ["Discounts cannot be negative."];
        if (request.Payments.Any(p => p.Amount <= 0)) errors["payments"] = ["Payment amounts must be greater than zero."];
        if (errors.Count > 0) throw new AppValidationException(errors);
    }

    private AuditLog Audit(string action, string entityName, Guid entityId, Guid? terminalId, object after) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = _tenant.TenantId,
        ActorUserId = _tenant.UserId,
        Action = action,
        EntityName = entityName,
        EntityId = entityId,
        TerminalId = terminalId,
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
