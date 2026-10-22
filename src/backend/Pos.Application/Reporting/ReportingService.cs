using Microsoft.EntityFrameworkCore;
using Pos.Application.Common;
using Pos.Domain.Common;
using Pos.Domain.Sales;
using Pos.Infrastructure.Persistence;

namespace Pos.Application.Reporting;

public sealed class ReportingService
{
    private readonly CommerceDbContext _db;
    private readonly ITenantContext _tenant;

    public ReportingService(CommerceDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task<TodayReportResponse> TodayAsync(CancellationToken cancellationToken)
    {
        var today = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero);
        var tomorrow = today.AddDays(1);
        var sales = await _db.Sales.AsNoTracking()
            .Where(s => s.TenantId == _tenant.TenantId && s.Status == SaleStatus.Completed && s.CompletedAtUtc >= today && s.CompletedAtUtc < tomorrow)
            .ToArrayAsync(cancellationToken);
        var saleIds = sales.Select(s => s.Id).ToArray();

        var top = await _db.SaleLines.AsNoTracking()
            .Where(l => l.TenantId == _tenant.TenantId && saleIds.Contains(l.SaleId))
            .GroupBy(l => new { l.ProductId, l.ProductNameSnapshot })
            .OrderByDescending(g => g.Sum(x => x.LineTotal))
            .Select(g => new TopProductResponse(g.Key.ProductId, g.Key.ProductNameSnapshot, g.Sum(x => x.Quantity), g.Sum(x => x.LineTotal)))
            .Take(5)
            .ToArrayAsync(cancellationToken);

        var itemsSold = await _db.SaleLines.AsNoTracking()
            .Where(l => l.TenantId == _tenant.TenantId && saleIds.Contains(l.SaleId))
            .SumAsync(l => (decimal?)l.Quantity, cancellationToken) ?? 0;

        var paymentMix = await _db.Payments.AsNoTracking()
            .Where(p => p.TenantId == _tenant.TenantId && saleIds.Contains(p.SaleId))
            .GroupBy(p => p.Method)
            .OrderByDescending(g => g.Sum(x => x.Amount))
            .Select(g => new PaymentMixResponse(g.Key.ToString(), g.Sum(x => x.Amount), g.Count()))
            .ToArrayAsync(cancellationToken);

        var low = await _db.InventoryTransactions.AsNoTracking()
            .Where(t => t.TenantId == _tenant.TenantId)
            .GroupBy(t => t.ProductId)
            .Select(g => new { ProductId = g.Key, QuantityOnHand = g.Sum(x => x.QuantityDelta) })
            .Where(x => x.QuantityOnHand <= 10)
            .Join(_db.Products.AsNoTracking().Where(p => p.TenantId == _tenant.TenantId), b => b.ProductId, p => p.Id, (b, p) => new LowStockProductResponse(b.ProductId, p.Name, b.QuantityOnHand))
            .ToArrayAsync(cancellationToken);

        var revenue = Money.Round(sales.Sum(s => s.GrandTotal));
        var corrections = await _db.SaleCorrections.AsNoTracking().Where(c => c.TenantId == _tenant.TenantId && c.OccurredAtUtc >= today && c.OccurredAtUtc < tomorrow).ToArrayAsync(cancellationToken);
        var correctionIds = corrections.Select(c => c.Id).ToArray();
        var refundMix = await _db.SaleRefunds.AsNoTracking().Where(r => r.TenantId == _tenant.TenantId && correctionIds.Contains(r.CorrectionId))
            .GroupBy(r => r.Method).Select(g => new PaymentMixResponse(g.Key.ToString(), g.Sum(r => r.Amount), g.Count())).ToArrayAsync(cancellationToken);
        var refundTotal = corrections.Sum(c => c.Total);
        return new TodayReportResponse(revenue - refundTotal, sales.Length, sales.Length == 0 ? 0 : Money.Round(sales.Average(s => s.GrandTotal)), itemsSold, paymentMix, top, low, revenue, refundTotal, refundMix);
    }
}
