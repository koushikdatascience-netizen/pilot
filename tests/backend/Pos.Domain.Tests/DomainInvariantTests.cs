using Pos.Domain.Catalog;
using Pos.Domain.Common;
using Pos.Domain.Inventory;
using Pos.Domain.Sales;
using Xunit;

namespace Pos.Domain.Tests;

public sealed class DomainInvariantTests
{
    [Fact]
    public void Money_rounds_away_from_zero_to_two_decimals()
    {
        Assert.Equal(10.24m, Money.Round(10.235m));
        Assert.Equal(10.25m, Money.Round(10.245m));
    }

    [Fact]
    public void Inclusive_gst_splits_intra_state_into_cgst_and_sgst()
    {
        var calc = new IndiaGstTaxCalculator();
        var tax = calc.Calculate(118m, 0.18m, TaxBehavior.Inclusive, intraState: true);

        Assert.Equal(100m, tax.TaxableAmount);
        Assert.Equal(18m, tax.TaxAmount);
        Assert.Equal(9m, tax.Cgst);
        Assert.Equal(9m, tax.Sgst);
        Assert.Equal(0m, tax.Igst);
    }

    [Fact]
    public void Exclusive_gst_splits_inter_state_into_igst()
    {
        var calc = new IndiaGstTaxCalculator();
        var tax = calc.Calculate(100m, 0.18m, TaxBehavior.Exclusive, intraState: false);

        Assert.Equal(100m, tax.TaxableAmount);
        Assert.Equal(18m, tax.TaxAmount);
        Assert.Equal(0m, tax.Cgst);
        Assert.Equal(0m, tax.Sgst);
        Assert.Equal(18m, tax.Igst);
    }

    [Fact]
    public void Sale_totals_preserve_financial_and_tax_snapshots()
    {
        var tenantId = Guid.NewGuid();
        var saleId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var calculator = new SaleCalculator(new IndiaGstTaxCalculator());

        var (lines, totals) = calculator.CalculateLines(
            tenantId,
            saleId,
            [new SaleLineInput(productId, "Milk 1L", "MILK-1L", "890", 2, 68, 6, 0.05m, TaxBehavior.Inclusive)],
            intraState: true);

        var line = lines.Single();
        Assert.Equal(136m, totals.Subtotal);
        Assert.Equal(6m, totals.DiscountTotal);
        Assert.Equal(6.19m, totals.TaxTotal);
        Assert.Equal(130m, totals.GrandTotal);
        Assert.Equal("Milk 1L", line.ProductNameSnapshot);
        Assert.Equal("MILK-1L", line.SkuSnapshot);
        Assert.Equal(123.81m, line.TaxableAmount);
        Assert.Equal(3.10m, line.CgstAmount);
        Assert.Equal(3.09m, line.SgstAmount);
    }

    [Fact]
    public void Inventory_ledger_builds_balances_from_transactions()
    {
        var tenantId = Guid.NewGuid();
        var warehouseId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var tx = Movements(tenantId, warehouseId, productId, 10, -3, 2);

        var balance = InventoryLedger.BuildBalances(tx).Single();

        Assert.Equal(9m, balance.QuantityOnHand);
    }

    [Fact]
    public void Inventory_ledger_prevents_oversell()
    {
        var tenantId = Guid.NewGuid();
        var warehouseId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var tx = Movements(tenantId, warehouseId, productId, 4);

        Assert.Throws<InvalidOperationException>(() => InventoryLedger.EnsureSufficientStock(tenantId, warehouseId, productId, 5, tx));
    }

    [Fact]
    public void Tenant_scope_keeps_inventory_balances_isolated()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var warehouseId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var tx = Movements(tenantA, warehouseId, productId, 10)
            .Concat(Movements(tenantB, warehouseId, productId, 2))
            .ToArray();

        var balances = InventoryLedger.BuildBalances(tx);

        Assert.Equal(10m, balances.Single(b => b.TenantId == tenantA).QuantityOnHand);
        Assert.Equal(2m, balances.Single(b => b.TenantId == tenantB).QuantityOnHand);
    }

    [Fact]
    public void Sale_completion_requires_payments_to_equal_grand_total()
    {
        Assert.Throws<InvalidOperationException>(() =>
            SaleCalculator.EnsurePaymentsCoverTotal(100m, [new SalePaymentInput(PaymentMethod.Cash, 90m, null)]));
    }

    private static IReadOnlyList<InventoryTransaction> Movements(Guid tenantId, Guid warehouseId, Guid productId, params decimal[] deltas)
    {
        return deltas.Select(delta => new InventoryTransaction
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            LocationId = Guid.NewGuid(),
            WarehouseId = warehouseId,
            ProductId = productId,
            Type = delta >= 0 ? InventoryTransactionType.OpeningStock : InventoryTransactionType.Sale,
            QuantityDelta = delta,
            OccurredAtUtc = DateTimeOffset.UtcNow
        }).ToArray();
    }
}
