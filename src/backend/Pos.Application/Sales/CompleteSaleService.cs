using System.Data;
using System.Reflection;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pos.Application.Common;
using Pos.Domain.Common;
using Pos.Domain.Identity;
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
    private readonly ILogger<CompleteSaleService> _logger;
    private const int MaxRetries = 5;

    public CompleteSaleService(CommerceDbContext db, ITenantContext tenant, SaleCalculator calculator, ILogger<CompleteSaleService> logger)
    {
        _db = db;
        _tenant = tenant;
        _calculator = calculator;
        _logger = logger;
    }

    public async Task<SaleResponse> CompleteAsync(CompleteSaleRequest request, CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= MaxRetries; attempt++)
        {
            try
            {
                return await CompleteOnceAsync(request, cancellationToken);
            }
            catch (Exception ex) when (IsRetryableDatabaseConcurrency(ex) && attempt < MaxRetries)
            {
                _logger.LogWarning(ex, "Retrying sale completion after database concurrency failure. Attempt {Attempt} of {MaxRetries}. TenantId={TenantId} TerminalId={TerminalId} ClientGeneratedId={ClientGeneratedId}", attempt, MaxRetries, _tenant.TenantId, request.TerminalId, request.ClientGeneratedId);
                _db.ChangeTracker.Clear();
                await Task.Delay(TimeSpan.FromMilliseconds(Random.Shared.Next(150, 350) * (1 << (attempt - 1))), cancellationToken);
            }
            catch (Exception ex) when (IsRetryableDatabaseConcurrency(ex))
            {
                _logger.LogWarning(ex, "Sale completion exhausted database concurrency retries. TenantId={TenantId} TerminalId={TerminalId} ClientGeneratedId={ClientGeneratedId}", _tenant.TenantId, request.TerminalId, request.ClientGeneratedId);
                throw new ConflictException("The sale could not be completed because another checkout updated the same stock or invoice sequence. Please retry.");
            }
        }

        throw new ConflictException("The sale could not be completed. Please retry.");
    }

    private async Task<SaleResponse> CompleteOnceAsync(CompleteSaleRequest request, CancellationToken cancellationToken)
    {
        ValidateRequest(request);
        var existing = await FindExistingAsync(request.TerminalId, request.ClientGeneratedId, cancellationToken);
        if (existing is not null && existing.Status != SaleStatus.Completed) throw new ConflictException("Resume the existing open cart to complete this sale.");
        if (existing is not null) return existing with { IdempotentReplay = true };

        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        await TransactionLocks.LockAsync(_db, $"command:{_tenant.TenantId}:{request.TerminalId}:{request.ClientGeneratedId}", cancellationToken);

        existing = await FindExistingAsync(request.TerminalId, request.ClientGeneratedId, cancellationToken);
        if (existing is not null)
        {
            if (existing.Status != SaleStatus.Completed) throw new ConflictException("Resume the existing open cart to complete this sale.");
            await transaction.CommitAsync(cancellationToken);
            return existing with { IdempotentReplay = true };
        }

        var location = await _db.Locations.SingleOrDefaultAsync(l => l.TenantId == _tenant.TenantId && l.Id == request.LocationId, cancellationToken)
            ?? throw new NotFoundException("Location was not found for this tenant.");
        var terminal = await _db.Terminals.SingleOrDefaultAsync(t => t.TenantId == _tenant.TenantId && t.Id == request.TerminalId, cancellationToken)
            ?? throw new NotFoundException("Terminal was not found for this tenant.");
        if (terminal.LocationId != location.Id) throw new ForbiddenException("Terminal does not belong to the requested location.");
        if (terminal.Status != Pos.Domain.Tenancy.TerminalStatus.Active) throw new ForbiddenException("Terminal is disabled.");

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

        var (domainLines, totals) = CalculateLines(saleId, lineInputs, intraState);
        AuthorizeDiscounts(location, lineInputs);
        EnsurePayments(totals.GrandTotal, request.Payments);

        await LockStockKeysAsync(warehouse.Id, request.Lines, products, cancellationToken);
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
            HsnSacSnapshot = products[l.ProductId].HsnSac,
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
        sale.ReceiptJson = await ReceiptSnapshotAsync(location, terminal, customer?.DisplayName, cancellationToken);
        _db.SaleLines.AddRange(saleLines);

        var payments = request.Payments.Select(p =>
        {
            var amount = Money.Round(p.Amount);
            var tendered = Money.Round(p.AmountTendered ?? p.Amount);
            var changeDue = p.Method == PaymentMethod.Cash ? Money.Round(tendered - amount) : 0;
            return new PaymentEntity
        {
            Id = Guid.NewGuid(),
            TenantId = _tenant.TenantId,
            SaleId = saleId,
            Method = p.Method,
            Amount = amount,
            AmountDue = amount,
            AmountTendered = tendered,
            ChangeDue = changeDue,
            Currency = "INR",
            Status = PaymentStatus.Captured,
            Reference = string.IsNullOrWhiteSpace(p.Reference) ? null : p.Reference.Trim(),
            CapturedAtUtc = completedAt
        };
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
        _logger.LogInformation("Sale completed. SaleId={SaleId} TenantId={TenantId} UserId={UserId} LocationId={LocationId} TerminalId={TerminalId}", saleId, _tenant.TenantId, _tenant.UserId, location.Id, terminal.Id);
        return await LoadSaleAsync(saleId, idempotentReplay: false, cancellationToken);
    }

    public async Task<SaleResponse> HoldAsync(HoldSaleRequest request, CancellationToken cancellationToken, Guid? heldId = null)
    {
        ValidateHoldRequest(request);
        var existing = heldId is null ? await FindExistingAsync(request.TerminalId, request.ClientGeneratedId, cancellationToken) : null;
        if (existing is not null) return existing with { IdempotentReplay = true };

        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

        await TransactionLocks.LockAsync(_db, heldId is { } id ? $"held:{_tenant.TenantId}:{id}" : $"command:{_tenant.TenantId}:{request.TerminalId}:{request.ClientGeneratedId}", cancellationToken);
        existing = heldId is null ? await FindExistingAsync(request.TerminalId, request.ClientGeneratedId, cancellationToken) : null;
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

        var (domainLines, totals) = CalculateLines(saleId, lineInputs, intraState);
        AuthorizeDiscounts(location, lineInputs);
        await ValidateStockAsync(location.DefaultSalesWarehouseId, request.Lines, products, cancellationToken);

        var previous = heldId is { } previousId
            ? await _db.Sales.SingleOrDefaultAsync(s => s.TenantId == _tenant.TenantId && s.Id == previousId, cancellationToken)
                ?? throw new NotFoundException("Held sale was not found.")
            : null;
        if (previous is not null && (previous.Status is not (SaleStatus.Held or SaleStatus.Draft) || previous.LocationId != request.LocationId || previous.TerminalId != request.TerminalId || previous.ClientGeneratedId != request.ClientGeneratedId))
            throw new ConflictException("Only an open cart in the same terminal context can be updated.");
        if (previous is not null)
        {
            _db.SaleLines.RemoveRange(await _db.SaleLines.Where(l => l.TenantId == _tenant.TenantId && l.SaleId == previous.Id).ToArrayAsync(cancellationToken));
        }
        var heldAt = DateTimeOffset.UtcNow;
        var sale = new SaleEntity
        {
            Id = previous?.Id ?? saleId,
            TenantId = _tenant.TenantId,
            LocationId = location.Id,
            TerminalId = terminal.Id,
            CustomerId = customer?.Id,
            InvoiceNumber = $"HELD-{saleId:N}"[..17],
            Status = request.Status,
            Currency = "INR",
            Subtotal = totals.Subtotal,
            DiscountTotal = totals.DiscountTotal,
            TaxTotal = totals.TaxTotal,
            GrandTotal = totals.GrandTotal,
            ClientGeneratedId = request.ClientGeneratedId,
            CompletedAtUtc = null,
            SyncStatus = SyncStatus.LocalOnly
        };
        if (previous is null) _db.Sales.Add(sale);
        else
        {
            sale.InvoiceNumber = previous.InvoiceNumber;
            sale.Version = previous.Version;
            sale.CreatedAtUtc = previous.CreatedAtUtc;
            _db.Entry(previous).CurrentValues.SetValues(sale);
            sale = previous;
            saleId = previous.Id;
        }
        _db.SaleLines.AddRange(domainLines.Select(l => new SaleLineEntity
        {
            Id = l.Id,
            TenantId = _tenant.TenantId,
            SaleId = saleId,
            ProductId = l.ProductId,
            ProductNameSnapshot = l.ProductNameSnapshot,
            HsnSacSnapshot = products[l.ProductId].HsnSac,
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
        }));

        _db.AuditLogs.Add(Audit("SaleHeld", "Sale", saleId, terminal.Id, sale));
        _db.OutboxEvents.Add(Outbox("SaleHeld", new { sale.Id, sale.LocationId, sale.TerminalId, sale.CustomerId, sale.GrandTotal, HeldAtUtc = heldAt }));
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await LoadSaleAsync(saleId, false, cancellationToken);
    }

    public async Task<SaleResponse> CompleteHeldAsync(Guid saleId, CompleteHeldSaleRequest request, CancellationToken cancellationToken)
    {
        ValidatePayments(request.Payments);

        for (var attempt = 1; attempt <= MaxRetries; attempt++)
        {
            try
            {
                return await CompleteHeldOnceAsync(saleId, request, cancellationToken);
            }
            catch (Exception ex) when (IsRetryableDatabaseConcurrency(ex) && attempt < MaxRetries)
            {
                _logger.LogWarning(ex, "Retrying held sale completion after database concurrency failure. Attempt {Attempt} of {MaxRetries}. TenantId={TenantId} SaleId={SaleId}", attempt, MaxRetries, _tenant.TenantId, saleId);
                _db.ChangeTracker.Clear();
                await Task.Delay(TimeSpan.FromMilliseconds(Random.Shared.Next(150, 350) * (1 << (attempt - 1))), cancellationToken);
            }
            catch (Exception ex) when (IsRetryableDatabaseConcurrency(ex))
            {
                _logger.LogWarning(ex, "Held sale completion exhausted database concurrency retries. TenantId={TenantId} SaleId={SaleId}", _tenant.TenantId, saleId);
                throw new ConflictException("The held sale could not be completed because another checkout updated the same stock or invoice sequence. Please retry.");
            }
        }

        throw new ConflictException("The held sale could not be completed. Please retry.");
    }

    private async Task<SaleResponse> CompleteHeldOnceAsync(Guid saleId, CompleteHeldSaleRequest request, CancellationToken cancellationToken)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        await TransactionLocks.LockAsync(_db, $"held:{_tenant.TenantId}:{saleId}", cancellationToken);
        var sale = await _db.Sales.SingleOrDefaultAsync(s => s.TenantId == _tenant.TenantId && s.Id == saleId, cancellationToken)
            ?? throw new NotFoundException("Held sale was not found for this tenant.");
        if (sale.Status == SaleStatus.Completed) return await LoadSaleAsync(sale.Id, true, cancellationToken);
        if (sale.Status is not (SaleStatus.Held or SaleStatus.Draft)) throw new ConflictException("Only open carts can be completed through this endpoint.");

        var location = await _db.Locations.SingleOrDefaultAsync(l => l.TenantId == _tenant.TenantId && l.Id == sale.LocationId, cancellationToken)
            ?? throw new NotFoundException("Location was not found for this tenant.");
        var terminal = await _db.Terminals.SingleOrDefaultAsync(t => t.TenantId == _tenant.TenantId && t.Id == sale.TerminalId, cancellationToken)
            ?? throw new NotFoundException("Terminal was not found for this tenant.");
        if (terminal.LocationId != location.Id || terminal.Status != Pos.Domain.Tenancy.TerminalStatus.Active)
            throw new ForbiddenException("Terminal is not active at this location.");
        var warehouse = await _db.Warehouses.SingleOrDefaultAsync(w => w.TenantId == _tenant.TenantId && w.Id == location.DefaultSalesWarehouseId, cancellationToken)
            ?? throw new NotFoundException("Default sales warehouse was not found for this location.");
        if (warehouse.LocationId != location.Id) throw new ForbiddenException("Default sales warehouse does not belong to the requested location.");

        var lines = await _db.SaleLines.Where(l => l.TenantId == _tenant.TenantId && l.SaleId == sale.Id).ToArrayAsync(cancellationToken);
        var productIds = lines.Select(l => l.ProductId).Distinct().ToArray();
        var products = await _db.Products.Where(p => p.TenantId == _tenant.TenantId && productIds.Contains(p.Id) && p.IsActive).ToDictionaryAsync(p => p.Id, cancellationToken);
        if (products.Count != productIds.Length) throw new NotFoundException("One or more products were not found for this tenant.");

        var lineRequests = lines.Select(l => new CompleteSaleLineRequest(l.ProductId, l.Quantity, l.DiscountAmount)).ToArray();
        AuthorizeDiscounts(location, lines.Select(l => new SaleLineInput(l.ProductId, l.ProductNameSnapshot, l.SkuSnapshot, l.BarcodeSnapshot, l.Quantity, l.UnitPrice, l.DiscountAmount, l.TaxRate, Pos.Domain.Catalog.TaxBehavior.Inclusive)).ToArray());
        EnsurePayments(sale.GrandTotal, request.Payments);
        await LockStockKeysAsync(warehouse.Id, lineRequests, products, cancellationToken);
        await ValidateStockAsync(warehouse.Id, lineRequests, products, cancellationToken);

        var completedAt = DateTimeOffset.UtcNow;
        sale.InvoiceNumber = await AllocateInvoiceNumberAsync(location, cancellationToken);
        sale.Status = SaleStatus.Completed;
        sale.CompletedAtUtc = completedAt;
        sale.SyncStatus = SyncStatus.Synced;
        var customerName = sale.CustomerId is { } customerId
            ? await _db.Customers.Where(c => c.TenantId == _tenant.TenantId && c.Id == customerId).Select(c => c.DisplayName).SingleOrDefaultAsync(cancellationToken)
            : null;
        sale.ReceiptJson = await ReceiptSnapshotAsync(location, terminal, customerName, cancellationToken);

        var payments = request.Payments.Select(p =>
        {
            var amount = Money.Round(p.Amount);
            var tendered = Money.Round(p.AmountTendered ?? p.Amount);
            return new PaymentEntity
            {
                Id = Guid.NewGuid(),
                TenantId = _tenant.TenantId,
                SaleId = sale.Id,
                Method = p.Method,
                Amount = amount,
                AmountDue = amount,
                AmountTendered = tendered,
                ChangeDue = p.Method == PaymentMethod.Cash ? Money.Round(tendered - amount) : 0,
                Currency = "INR",
                Status = PaymentStatus.Captured,
                Reference = string.IsNullOrWhiteSpace(p.Reference) ? null : p.Reference.Trim(),
                CapturedAtUtc = completedAt
            };
        }).ToArray();
        _db.Payments.AddRange(payments);

        foreach (var line in lines.Where(l => products[l.ProductId].IsStockTracked))
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
                SourceDocumentId = sale.Id,
                OccurredAtUtc = completedAt,
                CreatedByUserId = _tenant.UserId
            });
        }

        _db.AuditLogs.Add(Audit("HeldSaleCompleted", "Sale", sale.Id, terminal.Id, sale));
        _db.OutboxEvents.Add(Outbox("SaleCompleted", sale));
        foreach (var payment in payments) _db.OutboxEvents.Add(Outbox("PaymentRecorded", payment));
        foreach (var line in lines.Where(l => products[l.ProductId].IsStockTracked)) _db.OutboxEvents.Add(Outbox("InventoryReduced", line));

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await LoadSaleAsync(sale.Id, false, cancellationToken);
    }

    public async Task<IReadOnlyList<SaleResponse>> ListAsync(SaleStatus? status, DateTimeOffset? fromUtc, DateTimeOffset? toUtc, CancellationToken cancellationToken)
    {
        var query = _db.Sales.AsNoTracking().Where(s => s.TenantId == _tenant.TenantId);
        if (status is { } requestedStatus) query = query.Where(s => s.Status == requestedStatus);
        if (fromUtc is { } from) query = query.Where(s => (s.CompletedAtUtc ?? s.CreatedAtUtc) >= from);
        if (toUtc is { } to) query = query.Where(s => (s.CompletedAtUtc ?? s.CreatedAtUtc) <= to);

        var saleIds = await query
            .OrderByDescending(s => s.CompletedAtUtc)
            .ThenByDescending(s => s.CreatedAtUtc)
            .Select(s => s.Id)
            .Take(100)
            .ToArrayAsync(cancellationToken);

        var sales = new List<SaleResponse>();
        foreach (var id in saleIds)
        {
            sales.Add(await LoadSaleAsync(id, false, cancellationToken));
        }

        return sales;
    }

    public async Task<PagedResponse<SaleResponse>> HistoryAsync(string? invoice, Guid? customerId, Guid? terminalId, PaymentMethod? method, DateTimeOffset? fromUtc, DateTimeOffset? toUtc, int page, int pageSize, CancellationToken cancellationToken)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var query = _db.Sales.AsNoTracking().Where(s => s.TenantId == _tenant.TenantId && s.Status == SaleStatus.Completed);
        if (!string.IsNullOrWhiteSpace(invoice)) query = query.Where(s => s.InvoiceNumber.Contains(invoice));
        if (customerId is { } customer) query = query.Where(s => s.CustomerId == customer);
        if (terminalId is { } terminal) query = query.Where(s => s.TerminalId == terminal);
        if (fromUtc is { } from) query = query.Where(s => s.CompletedAtUtc >= from);
        if (toUtc is { } to) query = query.Where(s => s.CompletedAtUtc <= to);
        if (method is { } paymentMethod)
            query = query.Where(s => _db.Payments.Any(p => p.TenantId == _tenant.TenantId && p.SaleId == s.Id && p.Method == paymentMethod));
        var count = await query.CountAsync(cancellationToken);
        var ids = await query.OrderByDescending(s => s.CompletedAtUtc).ThenBy(s => s.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).Select(s => s.Id).ToArrayAsync(cancellationToken);
        var items = new List<SaleResponse>();
        foreach (var id in ids) items.Add(await LoadSaleAsync(id, false, cancellationToken));
        return new PagedResponse<SaleResponse>(items, page, pageSize, count);
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
            .Select(l => new SaleLineResponse(l.Id, l.ProductId, l.ProductNameSnapshot, l.SkuSnapshot, l.BarcodeSnapshot, l.Quantity, l.UnitPrice, l.DiscountAmount, l.TaxableAmount, l.TaxRate, l.TaxAmount, l.CgstAmount, l.SgstAmount, l.IgstAmount, l.LineTotal, l.HsnSacSnapshot))
            .ToArrayAsync(cancellationToken);
        var payments = await _db.Payments.AsNoTracking()
            .Where(p => p.TenantId == _tenant.TenantId && p.SaleId == saleId)
            .Select(p => new PaymentResponse(p.Id, p.Method, p.Amount, p.AmountDue, p.AmountTendered, p.ChangeDue, p.Currency, p.Status, p.Reference))
            .ToArrayAsync(cancellationToken);

        return new SaleResponse(sale.Id, sale.LocationId, sale.TerminalId, sale.CustomerId, sale.InvoiceNumber, sale.Status, sale.Currency, sale.Subtotal, sale.DiscountTotal, sale.TaxTotal, sale.GrandTotal, sale.ClientGeneratedId, sale.CompletedAtUtc, sale.SyncStatus, lines, payments, idempotentReplay,
            sale.ReceiptJson is null ? null : JsonSerializer.Deserialize<ReceiptHeader>(sale.ReceiptJson));
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

    private async Task LockStockKeysAsync(Guid warehouseId, IReadOnlyList<CompleteSaleLineRequest> lines, IReadOnlyDictionary<Guid, Pos.Infrastructure.Persistence.Product> products, CancellationToken cancellationToken)
    {
        if (!_db.Database.IsNpgsql()) return;

        var productIds = lines
            .Where(l => products[l.ProductId].IsStockTracked)
            .Select(l => l.ProductId)
            .Distinct()
            .OrderBy(id => id)
            .ToArray();

        foreach (var productId in productIds)
        {
            await TransactionLocks.StockAsync(_db, _tenant.TenantId, warehouseId, productId, cancellationToken);
        }
    }

    private async Task<string> AllocateInvoiceNumberAsync(Location location, CancellationToken cancellationToken)
    {
        var period = DateTimeOffset.UtcNow.ToString("yyyyMM");
        var prefix = $"{location.Code}-{period}";
        await TransactionLocks.LockAsync(_db, $"invoice:{_tenant.TenantId}:{location.Id}:{period}", cancellationToken);
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

    private async Task<string> ReceiptSnapshotAsync(Location location, Terminal terminal, string? customerName, CancellationToken cancellationToken)
    {
        var business = await _db.Businesses.AsNoTracking().SingleAsync(b => b.TenantId == _tenant.TenantId && b.Id == location.BusinessId, cancellationToken);
        var organization = await _db.Organizations.AsNoTracking().SingleAsync(o => o.TenantId == _tenant.TenantId && o.Id == location.OrganizationId, cancellationToken);
        var cashier = await _db.Users.AsNoTracking().Where(u => u.TenantId == _tenant.TenantId && u.Id == _tenant.UserId).Select(u => u.DisplayName).SingleAsync(cancellationToken);
        return JsonSerializer.Serialize(new ReceiptHeader(business.Name, organization.LegalName, location.Name, location.Address, organization.Gstin, terminal.Name, cashier, customerName ?? "Walk-in"));
    }

    private void AuthorizeDiscounts(Location location, IReadOnlyList<SaleLineInput> lines)
    {
        var maxAllowed = _tenant.HasPermission(Permission.ApplyDiscount)
            ? location.ManagerMaxDiscountPercent
            : location.CashierMaxDiscountPercent;

        foreach (var line in lines)
        {
            var gross = Money.Round(line.Quantity * line.UnitPrice);
            if (gross == 0) continue;
            var rate = line.DiscountAmount / gross;
            if (rate > maxAllowed)
            {
                throw new ForbiddenException($"Discount exceeds allowed threshold of {maxAllowed:P0}.");
            }
        }
    }

    private static bool IsIntraState(Location location, Customer? customer)
    {
        return IndiaGstTaxCalculator.IsIntraState(location.StateCode, customer?.StateCode, customer?.Gstin);
    }

    private static void ValidateRequest(CompleteSaleRequest request)
    {
        if (request.Lines is null || request.Payments is null)
            throw new AppValidationException(new Dictionary<string, string[]> { ["sale"] = ["Lines and payments are required."] });
        var errors = new Dictionary<string, string[]>();
        if (request.ClientGeneratedId == Guid.Empty) errors["clientGeneratedId"] = ["Client-generated ID is required."];
        if (request.Lines.Count == 0) errors["lines"] = ["At least one sale line is required."];
        if (request.Payments.Count == 0) errors["payments"] = ["At least one payment is required."];
        if (request.Lines.Any(l => l.Quantity <= 0)) errors["quantity"] = ["Line quantities must be greater than zero."];
        if (request.Lines.Any(l => l.Quantity != decimal.Round(l.Quantity, 3))) errors["quantity"] = ["Quantities support at most three decimal places."];
        if (request.Lines.Any(l => l.DiscountAmount != Money.Round(l.DiscountAmount))) errors["discount"] = ["Discounts support at most two decimal places."];
        if (request.Lines.Any(l => l.DiscountAmount < 0)) errors["discount"] = ["Discounts cannot be negative."];
        AddPaymentValidationErrors(request.Payments, errors);
        if (errors.Count > 0) throw new AppValidationException(errors);
    }

    private static void ValidateHoldRequest(HoldSaleRequest request)
    {
        if (request.Lines is null)
            throw new AppValidationException(new Dictionary<string, string[]> { ["lines"] = ["Lines are required."] });
        var errors = new Dictionary<string, string[]>();
        if (request.Status is not (SaleStatus.Held or SaleStatus.Draft)) errors["status"] = ["Only Draft or Held is allowed."];
        if (request.ClientGeneratedId == Guid.Empty) errors["clientGeneratedId"] = ["Client-generated ID is required."];
        if (request.Lines.Count == 0) errors["lines"] = ["At least one sale line is required."];
        if (request.Lines.Any(l => l.Quantity <= 0)) errors["quantity"] = ["Line quantities must be greater than zero."];
        if (request.Lines.Any(l => l.Quantity != decimal.Round(l.Quantity, 3))) errors["quantity"] = ["Quantities support at most three decimal places."];
        if (request.Lines.Any(l => l.DiscountAmount != Money.Round(l.DiscountAmount))) errors["discount"] = ["Discounts support at most two decimal places."];
        if (request.Lines.Any(l => l.DiscountAmount < 0)) errors["discount"] = ["Discounts cannot be negative."];
        if (errors.Count > 0) throw new AppValidationException(errors);
    }

    private static void ValidatePayments(IReadOnlyList<CompleteSalePaymentRequest> payments)
    {
        if (payments is null)
            throw new AppValidationException(new Dictionary<string, string[]> { ["payments"] = ["Payments are required."] });
        var errors = new Dictionary<string, string[]>();
        if (payments.Count == 0) errors["payments"] = ["At least one payment is required."];
        AddPaymentValidationErrors(payments, errors);
        if (errors.Count > 0) throw new AppValidationException(errors);
    }

    private static void AddPaymentValidationErrors(IReadOnlyList<CompleteSalePaymentRequest> payments, Dictionary<string, string[]> errors)
    {
        if (payments.Any(p => !Enum.IsDefined(p.Method))) errors["method"] = ["Unknown payment method."];
        if (payments.Any(p => p.Amount != Money.Round(p.Amount) || (p.AmountTendered is { } tendered && tendered != Money.Round(tendered)))) errors["payments"] = ["Payment amounts must have at most two decimal places."];
        if (payments.Any(p => p.Amount <= 0)) errors["payments"] = ["Payment amounts must be greater than zero."];
        if (payments.Any(p => p.AmountTendered < 0)) errors["amountTendered"] = ["Amount tendered cannot be negative."];
    }

    private (IReadOnlyList<Pos.Domain.Sales.SaleLine> Lines, SaleTotals Totals) CalculateLines(Guid saleId, IReadOnlyList<SaleLineInput> lines, bool intraState)
    {
        try { return _calculator.CalculateLines(_tenant.TenantId, saleId, lines, intraState); }
        catch (InvalidOperationException ex)
        {
            throw new AppValidationException(new Dictionary<string, string[]> { ["lines"] = [ex.Message] });
        }
    }

    private static void EnsurePayments(decimal total, IReadOnlyList<CompleteSalePaymentRequest> payments)
    {
        try { SaleCalculator.EnsurePaymentsCoverTotal(total, payments.Select(p => new SalePaymentInput(p.Method, p.Amount, p.AmountTendered, p.Reference)).ToArray()); }
        catch (InvalidOperationException ex)
        {
            throw new AppValidationException(new Dictionary<string, string[]> { ["payments"] = [ex.Message] });
        }
    }

    private static bool IsRetryableDatabaseConcurrency(Exception ex)
    {
        if (DatabaseConcurrency.IsSerializationFailure(ex)) return true;

        var current = ex;
        while (current is not null)
        {
            var sqlState = current.GetType().GetProperty("SqlState", BindingFlags.Public | BindingFlags.Instance)?.GetValue(current) as string;
            var constraint = current.GetType().GetProperty("ConstraintName")?.GetValue(current) as string;
            if (sqlState == "23505" && constraint?.Contains("ClientGeneratedId", StringComparison.Ordinal) == true) return true;
            current = current.InnerException;
        }

        return false;
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
