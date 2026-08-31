using Pos.Domain.Catalog;
using Pos.Domain.Common;
using Pos.Domain.Inventory;
using Pos.Domain.Sales;

var tests = new (string Name, Action Body)[]
{
    ("Money rounds away from zero to two decimals", MoneyRounds),
    ("India GST inclusive tax splits intra-state into CGST and SGST", GstInclusiveSplits),
    ("Sale totals preserve immutable line snapshots", SaleTotalsAreCorrect),
    ("Inventory ledger derives balances from transactions", InventoryLedgerBuildsBalances),
    ("Inventory ledger prevents oversell", InventoryLedgerPreventsOversell),
    ("Tenant scope keeps balances isolated", TenantScopeIsolation),
    ("Sale completion requires payments to equal grand total", PaymentsMustMatchTotal)
};

var failures = new List<string>();

foreach (var test in tests)
{
    try
    {
        test.Body();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception ex)
    {
        failures.Add($"{test.Name}: {ex.Message}");
        Console.WriteLine($"FAIL {test.Name}: {ex.Message}");
    }
}

if (failures.Count > 0)
{
    Console.Error.WriteLine("Test failures:");
    foreach (var failure in failures)
    {
        Console.Error.WriteLine(failure);
    }

    Environment.Exit(1);
}

static void MoneyRounds()
{
    AssertEqual(10.24m, Money.Round(10.235m));
    AssertEqual(10.25m, Money.Round(10.245m));
}

static void GstInclusiveSplits()
{
    var calc = new IndiaGstTaxCalculator();
    var tax = calc.Calculate(118m, 0.18m, TaxBehavior.Inclusive, intraState: true);

    AssertEqual(100m, tax.TaxableAmount);
    AssertEqual(18m, tax.TaxAmount);
    AssertEqual(9m, tax.Cgst);
    AssertEqual(9m, tax.Sgst);
    AssertEqual(0m, tax.Igst);
}

static void SaleTotalsAreCorrect()
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

    AssertEqual(136m, totals.Subtotal);
    AssertEqual(6m, totals.DiscountTotal);
    AssertEqual(6.19m, totals.TaxTotal);
    AssertEqual(130m, totals.GrandTotal);
    AssertEqual("Milk 1L", lines.Single().ProductNameSnapshot);
    AssertEqual("MILK-1L", lines.Single().SkuSnapshot);
}

static void InventoryLedgerBuildsBalances()
{
    var tenantId = Guid.NewGuid();
    var warehouseId = Guid.NewGuid();
    var productId = Guid.NewGuid();
    var tx = Movements(tenantId, warehouseId, productId, 10, -3, 2);

    var balance = InventoryLedger.BuildBalances(tx).Single();

    AssertEqual(9m, balance.QuantityOnHand);
}

static void InventoryLedgerPreventsOversell()
{
    var tenantId = Guid.NewGuid();
    var warehouseId = Guid.NewGuid();
    var productId = Guid.NewGuid();
    var tx = Movements(tenantId, warehouseId, productId, 4);

    AssertThrows<InvalidOperationException>(() => InventoryLedger.EnsureSufficientStock(tenantId, warehouseId, productId, 5, tx));
}

static void TenantScopeIsolation()
{
    var tenantA = Guid.NewGuid();
    var tenantB = Guid.NewGuid();
    var warehouseId = Guid.NewGuid();
    var productId = Guid.NewGuid();
    var tx = Movements(tenantA, warehouseId, productId, 10)
        .Concat(Movements(tenantB, warehouseId, productId, 2))
        .ToArray();

    var balances = InventoryLedger.BuildBalances(tx);

    AssertEqual(10m, balances.Single(b => b.TenantId == tenantA).QuantityOnHand);
    AssertEqual(2m, balances.Single(b => b.TenantId == tenantB).QuantityOnHand);
}

static void PaymentsMustMatchTotal()
{
    AssertThrows<InvalidOperationException>(() =>
        SaleCalculator.EnsurePaymentsCoverTotal(100m, [new SalePaymentInput(PaymentMethod.Cash, 90m, null)]));
}

static IReadOnlyList<InventoryTransaction> Movements(Guid tenantId, Guid warehouseId, Guid productId, params decimal[] deltas)
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

static void AssertEqual<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"Expected {expected}, got {actual}.");
    }
}

static void AssertThrows<TException>(Action action)
    where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException($"Expected exception {typeof(TException).Name}.");
}
