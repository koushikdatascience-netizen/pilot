using System.Data;
using Microsoft.EntityFrameworkCore;
using Pos.Application.Common;
using Pos.Domain.Sales;
using Pos.Domain.Tenancy;
using Pos.Infrastructure.Persistence;
using InventoryType = Pos.Domain.Inventory.InventoryTransactionType;

namespace Pos.Application.Retail;

public sealed class SaleCorrectionService(CommerceDbContext db, ITenantContext tenant, CashDrawerService drawer)
{
    public async Task<CorrectionQuoteResponse> QuoteAsync(Guid saleId, CorrectionRequest request, bool isVoid, CancellationToken ct)
    {
        Validate(request);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        var result = await BuildAsync(saleId, request, isVoid, ct);
        await tx.CommitAsync(ct);
        return result;
    }

    public async Task<CorrectionResponse> CreateAsync(Guid saleId, CorrectionRequest request, bool isVoid, CancellationToken ct)
    {
        Validate(request);
        var reason = RetailTransactions.Reason(request.Reason);
        var hash = RetailTransactions.Hash(new { saleId, request.TerminalId, isVoid, request.ExpectedStatus, reason,
            Lines = request.Lines.OrderBy(l => l.SaleLineId).ToArray(),
            Refunds = request.Refunds.OrderBy(r => r.Method).Select(r => new { r.Method, r.Amount, Reference = r.Reference?.Trim() }).ToArray() });
        var result = await RetailTransactions.RunAsync(db, async token =>
        {
            await TransactionLocks.LockAsync(db, $"correction-command:{tenant.TenantId}:{request.TerminalId}:{request.ClientGeneratedId}", token);
            var replay = await db.SaleCorrections.SingleOrDefaultAsync(c => c.TenantId == tenant.TenantId && c.TerminalId == request.TerminalId && c.ClientGeneratedId == request.ClientGeneratedId, token);
            if (replay is not null)
            {
                if (replay.RequestHash != hash) throw new ConflictException("Command UUID was already used with a different payload.");
                return (replay.Id, Replay: true);
            }
            await TransactionLocks.LockAsync(db, $"correction-sale:{tenant.TenantId}:{saleId}", token);
            // Open-cart cancellation shares the same lock as editing/completing a held cart.
            if (isVoid) await TransactionLocks.LockAsync(db, $"held:{tenant.TenantId}:{saleId}", token);
            var quote = await BuildAsync(saleId, request, isVoid, token);
            if (request.Refunds.Sum(r => r.Amount) != quote.Total) throw RetailTransactions.Invalid("refunds", $"Refund allocations must total {quote.Total:0.00}.");
            foreach (var refund in request.Refunds)
                if (refund.Amount > (quote.TenderAvailable.SingleOrDefault(p => p.Method == refund.Method)?.Amount ?? 0))
                    throw new ConflictException("Refund exceeds the remaining original tender allocation.");
            var sale = await db.Sales.SingleAsync(s => s.TenantId == tenant.TenantId && s.Id == saleId, token);
            var document = new SaleCorrection { Id = Guid.NewGuid(), TenantId = tenant.TenantId, SaleId = saleId, TerminalId = request.TerminalId,
                ClientGeneratedId = request.ClientGeneratedId, RequestHash = hash, Kind = quote.Kind, Reason = reason, Total = quote.Total,
                ActorUserId = tenant.UserId, OccurredAtUtc = DateTimeOffset.UtcNow };
            document.Number = $"{(isVoid ? "V" : "R")}-{document.Id:N}";
            db.SaleCorrections.Add(document);
            if (quote.Kind == "OpenVoid") sale.Status = SaleStatus.Voided;
            var originals = await db.SaleLines.Where(l => l.TenantId == tenant.TenantId && l.SaleId == saleId).ToDictionaryAsync(l => l.Id, token);
            foreach (var line in quote.Lines.OrderBy(l => l.WarehouseId).ThenBy(l => originals[l.SaleLineId].ProductId))
            {
                db.SaleCorrectionLines.Add(new SaleCorrectionLine { Id = Guid.NewGuid(), TenantId = tenant.TenantId, CorrectionId = document.Id,
                    SaleLineId = line.SaleLineId, WarehouseId = line.WarehouseId, Quantity = line.Quantity, Restock = line.Restock,
                    DiscountAmount = line.DiscountAmount, TaxableAmount = line.TaxableAmount, CgstAmount = line.CgstAmount, SgstAmount = line.SgstAmount, IgstAmount = line.IgstAmount, Total = line.Total });
                if (line.Restock && line.WarehouseId is { } warehouse)
                {
                    var original = originals[line.SaleLineId];
                    await TransactionLocks.StockAsync(db, tenant.TenantId, warehouse, original.ProductId, token);
                    db.InventoryTransactions.Add(new InventoryTransaction { Id = Guid.NewGuid(), TenantId = tenant.TenantId, LocationId = sale.LocationId,
                        WarehouseId = warehouse, ProductId = original.ProductId, Type = InventoryType.SaleReturn, QuantityDelta = line.Quantity,
                        SourceDocumentType = "SaleCorrection", SourceDocumentId = document.Id, OccurredAtUtc = document.OccurredAtUtc, CreatedByUserId = tenant.UserId });
                }
            }
            foreach (var refund in request.Refunds)
                db.SaleRefunds.Add(new SaleRefund { Id = Guid.NewGuid(), TenantId = tenant.TenantId, CorrectionId = document.Id, Method = refund.Method, Amount = refund.Amount, Reference = refund.Reference?.Trim() });
            await drawer.CaptureAsync(request.TerminalId, document.Id, -request.Refunds.Where(r => r.Method == PaymentMethod.Cash).Sum(r => r.Amount), true, token);
            RetailTransactions.Audit(db, tenant, isVoid ? "SaleVoided" : "SaleReturned", "SaleCorrection", document.Id, request.TerminalId,
                new { document.SaleId, document.Number, document.Kind, document.Reason, document.Total, Lines = quote.Lines, Refunds = request.Refunds });
            return (document.Id, Replay: false);
        }, ct);
        return await LoadAsync(result.Id, result.Replay, ct);
    }

    public async Task<CorrectionHistoryResponse> HistoryAsync(Guid saleId, CancellationToken ct)
    {
        var sale = await db.Sales.AsNoTracking().SingleOrDefaultAsync(s => s.TenantId == tenant.TenantId && s.Id == saleId, ct) ?? throw new NotFoundException("Sale not found.");
        var documents = await db.SaleCorrections.AsNoTracking().Where(c => c.TenantId == tenant.TenantId && c.SaleId == saleId).OrderBy(c => c.OccurredAtUtc).Select(c => c.Id).ToArrayAsync(ct);
        var items = new List<CorrectionResponse>();
        foreach (var id in documents) items.Add(await LoadAsync(id, false, ct));
        var sold = await db.SaleLines.Where(l => l.TenantId == tenant.TenantId && l.SaleId == saleId).SumAsync(l => (decimal?)l.Quantity, ct) ?? 0;
        var state = items.Any(c => c.Kind is "Void" or "OpenVoid") ? "Voided" : items.Count == 0 ? sale.Status.ToString() : items.SelectMany(c => c.Lines).Sum(l => l.Quantity) == sold ? "Returned" : "PartiallyReturned";
        return new(state, items);
    }

    private async Task<CorrectionQuoteResponse> BuildAsync(Guid saleId, CorrectionRequest request, bool isVoid, CancellationToken ct)
    {
        if (!await db.Terminals.AnyAsync(t => t.TenantId == tenant.TenantId && t.Id == request.TerminalId && t.Status == TerminalStatus.Active, ct)) throw new NotFoundException("Active processing terminal not found.");
        var sale = await db.Sales.AsNoTracking().SingleOrDefaultAsync(s => s.TenantId == tenant.TenantId && s.Id == saleId, ct) ?? throw new NotFoundException("Sale not found.");
        var prior = await db.SaleCorrections.Where(c => c.TenantId == tenant.TenantId && c.SaleId == saleId).Select(c => c.Id).ToArrayAsync(ct);
        if (isVoid && request.ExpectedStatus != sale.Status) throw new ConflictException("Sale status changed or was not specified. Refresh before voiding.");
        if (isVoid && prior.Length != 0) throw new ConflictException("A sale with an earlier correction cannot be voided.");
        if (isVoid && request.Lines.Length != 0) throw RetailTransactions.Invalid("lines", "Void reverses the entire sale; do not select individual lines.");
        if (isVoid && sale.Status is SaleStatus.Held or SaleStatus.Draft) return new("OpenVoid", 0, [], []);
        if (sale.Status != SaleStatus.Completed) throw new ConflictException("Only completed sales can be returned or financially reversed.");
        var originals = await db.SaleLines.AsNoTracking().Where(l => l.TenantId == tenant.TenantId && l.SaleId == saleId).ToArrayAsync(ct);
        var old = await db.SaleCorrectionLines.AsNoTracking().Where(l => l.TenantId == tenant.TenantId && prior.Contains(l.CorrectionId)).ToArrayAsync(ct);
        var inputs = isVoid ? originals.Select(l => new ReturnLineRequest(l.Id, l.Quantity, true)).ToArray() : request.Lines;
        if (inputs.Length == 0) throw RetailTransactions.Invalid("lines", "Select at least one return line.");
        var stock = await db.InventoryTransactions.AsNoTracking().Where(i => i.TenantId == tenant.TenantId && i.SourceDocumentId == saleId && i.Type == InventoryType.Sale).ToArrayAsync(ct);
        var result = new List<CorrectionLineResponse>();
        foreach (var input in inputs)
        {
            var original = originals.SingleOrDefault(l => l.Id == input.SaleLineId) ?? throw new NotFoundException("Original invoice line not found.");
            var already = old.Where(l => l.SaleLineId == original.Id).Sum(l => l.Quantity);
            if (already + input.Quantity > original.Quantity) throw new ConflictException("Return quantity exceeds the remaining invoice quantity.");
            if (original.LineTotal != original.TaxableAmount + original.CgstAmount + original.SgstAmount + original.IgstAmount ||
                original.TaxableAmount < 0 || original.CgstAmount < 0 || original.SgstAmount < 0 || original.IgstAmount < 0 ||
                (original.IgstAmount > 0 && original.CgstAmount + original.SgstAmount > 0))
                throw new ConflictException("Historical invoice components require manual review before return.");
            decimal Allocate(decimal amount) => ReturnAllocation.Component(amount, original.Quantity, already, input.Quantity);
            var allocation = ReturnAllocation.Line(original.LineTotal, original.TaxableAmount, original.CgstAmount, original.SgstAmount, original.IgstAmount, original.Quantity, already, input.Quantity);
            var warehouses = stock.Where(i => i.ProductId == original.ProductId).Select(i => i.WarehouseId).Distinct().ToArray();
            if (warehouses.Length > 1) throw new ConflictException("Historical warehouse allocation requires manual review.");
            result.Add(new(original.Id, input.Quantity, input.Restock, warehouses.Length == 1 ? warehouses[0] : null, Allocate(original.DiscountAmount), allocation.Taxable, allocation.Cgst, allocation.Sgst, allocation.Igst, allocation.Total));
        }
        var captured = await db.Payments.AsNoTracking().Where(p => p.TenantId == tenant.TenantId && p.SaleId == saleId && p.Status == PaymentStatus.Captured).ToArrayAsync(ct);
        var refunded = await db.SaleRefunds.AsNoTracking().Where(r => r.TenantId == tenant.TenantId && prior.Contains(r.CorrectionId)).ToArrayAsync(ct);
        var tender = captured.GroupBy(p => p.Method).Select(g => new RefundRequest(g.Key, g.Sum(p => p.AmountDue) - refunded.Where(r => r.Method == g.Key).Sum(r => r.Amount), null)).ToArray();
        return new(isVoid ? "Void" : "Return", result.Sum(l => l.Total), result, tender);
    }

    private async Task<CorrectionResponse> LoadAsync(Guid id, bool replay, CancellationToken ct)
    {
        var d = await db.SaleCorrections.AsNoTracking().SingleAsync(c => c.TenantId == tenant.TenantId && c.Id == id, ct);
        var lines = await db.SaleCorrectionLines.AsNoTracking().Where(l => l.TenantId == tenant.TenantId && l.CorrectionId == id)
            .Select(l => new CorrectionLineResponse(l.SaleLineId, l.Quantity, l.Restock, l.WarehouseId, l.DiscountAmount, l.TaxableAmount, l.CgstAmount, l.SgstAmount, l.IgstAmount, l.Total)).ToArrayAsync(ct);
        var refunds = await db.SaleRefunds.AsNoTracking().Where(r => r.TenantId == tenant.TenantId && r.CorrectionId == id).Select(r => new RefundRequest(r.Method, r.Amount, r.Reference)).ToArrayAsync(ct);
        return new(d.Id, d.SaleId, d.TerminalId, d.ClientGeneratedId, d.Number, d.Kind, d.Reason, d.Total, d.OccurredAtUtc, lines, refunds, replay);
    }
    private static void Validate(CorrectionRequest request)
    {
        RetailTransactions.Reason(request.Reason);
        if (request.ClientGeneratedId == Guid.Empty) throw RetailTransactions.Invalid("clientGeneratedId", "A permanent command UUID is required.");
        if (request.Lines is null || request.Refunds is null || request.Lines.Length > 200 || request.Refunds.Length > 5 || request.Lines.Any(l => l is null) || request.Refunds.Any(r => r is null)) throw RetailTransactions.Invalid("lines", "Invalid correction size.");
        if (request.Lines.GroupBy(l => l.SaleLineId).Any(g => g.Count() > 1) || request.Lines.Any(l => l.Quantity <= 0 || l.Quantity != decimal.Round(l.Quantity, 3)))
            throw RetailTransactions.Invalid("quantity", "Use distinct invoice lines and positive quantities with at most three decimals.");
        if (request.Refunds.GroupBy(r => r.Method).Any(g => g.Count() > 1)) throw RetailTransactions.Invalid("refunds", "Combine each refund method into one allocation.");
        foreach (var refund in request.Refunds)
        {
            RetailTransactions.Money(refund.Amount, true);
            if (!Enum.IsDefined(refund.Method) || refund.Reference?.Length > 160 || (refund.Method is not (PaymentMethod.Cash or PaymentMethod.Credit) && string.IsNullOrWhiteSpace(refund.Reference)))
                throw RetailTransactions.Invalid("refunds", "Choose a valid method; electronic refunds require an operator reference of at most 160 characters.");
        }
    }
}
