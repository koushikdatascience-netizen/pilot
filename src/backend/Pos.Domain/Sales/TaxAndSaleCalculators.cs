using Pos.Domain.Catalog;
using Pos.Domain.Common;

namespace Pos.Domain.Sales;

public sealed record TaxBreakdown(decimal TaxableAmount, decimal TaxAmount, decimal Cgst, decimal Sgst, decimal Igst);

public interface ITaxCalculator
{
    TaxBreakdown Calculate(decimal lineAmountAfterDiscount, decimal taxRate, TaxBehavior behavior, bool intraState);
}

public sealed class IndiaGstTaxCalculator : ITaxCalculator
{
    public TaxBreakdown Calculate(decimal lineAmountAfterDiscount, decimal taxRate, TaxBehavior behavior, bool intraState)
    {
        if (lineAmountAfterDiscount < 0) throw new ArgumentOutOfRangeException(nameof(lineAmountAfterDiscount));
        if (taxRate < 0) throw new ArgumentOutOfRangeException(nameof(taxRate));

        var taxable = behavior == TaxBehavior.Inclusive
            ? lineAmountAfterDiscount / (1 + taxRate)
            : lineAmountAfterDiscount;
        var tax = behavior == TaxBehavior.Inclusive
            ? lineAmountAfterDiscount - taxable
            : taxable * taxRate;

        taxable = Money.Round(taxable);
        tax = Money.Round(tax);

        if (intraState)
        {
            var half = Money.Round(tax / 2);
            return new TaxBreakdown(taxable, tax, half, tax - half, 0);
        }

        return new TaxBreakdown(taxable, tax, 0, 0, tax);
    }
}

public sealed record SaleLineInput(Guid ProductId, string ProductName, string Sku, string? Barcode, decimal Quantity, decimal UnitPrice, decimal DiscountAmount, decimal TaxRate, TaxBehavior TaxBehavior);
public sealed record SalePaymentInput(PaymentMethod Method, decimal Amount, string? Reference);
public sealed record SaleTotals(decimal Subtotal, decimal DiscountTotal, decimal TaxTotal, decimal GrandTotal);

public sealed class SaleCalculator
{
    private readonly ITaxCalculator _taxCalculator;

    public SaleCalculator(ITaxCalculator taxCalculator)
    {
        _taxCalculator = taxCalculator;
    }

    public (IReadOnlyList<SaleLine> Lines, SaleTotals Totals) CalculateLines(Guid tenantId, Guid saleId, IReadOnlyList<SaleLineInput> inputs, bool intraState)
    {
        if (inputs.Count == 0) throw new InvalidOperationException("A sale requires at least one line.");

        var lines = new List<SaleLine>();
        decimal subtotal = 0;
        decimal discountTotal = 0;
        decimal taxTotal = 0;
        decimal grandTotal = 0;

        foreach (var input in inputs)
        {
            if (input.Quantity <= 0) throw new InvalidOperationException("Line quantity must be greater than zero.");
            if (input.UnitPrice < 0) throw new InvalidOperationException("Unit price cannot be negative.");
            if (input.DiscountAmount < 0) throw new InvalidOperationException("Discount cannot be negative.");

            var gross = Money.Round(input.Quantity * input.UnitPrice);
            if (input.DiscountAmount > gross) throw new InvalidOperationException("Discount cannot exceed line gross amount.");

            var afterDiscount = Money.Round(gross - input.DiscountAmount);
            var tax = _taxCalculator.Calculate(afterDiscount, input.TaxRate, input.TaxBehavior, intraState);
            var lineTotal = input.TaxBehavior == TaxBehavior.Inclusive
                ? afterDiscount
                : Money.Round(afterDiscount + tax.TaxAmount);

            lines.Add(new SaleLine
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                SaleId = saleId,
                ProductId = input.ProductId,
                ProductNameSnapshot = input.ProductName,
                SkuSnapshot = input.Sku,
                BarcodeSnapshot = input.Barcode,
                Quantity = input.Quantity,
                UnitPrice = input.UnitPrice,
                DiscountAmount = input.DiscountAmount,
                TaxRate = input.TaxRate,
                TaxAmount = tax.TaxAmount,
                LineTotal = lineTotal
            });

            subtotal += gross;
            discountTotal += input.DiscountAmount;
            taxTotal += tax.TaxAmount;
            grandTotal += lineTotal;
        }

        return (lines, new SaleTotals(Money.Round(subtotal), Money.Round(discountTotal), Money.Round(taxTotal), Money.Round(grandTotal)));
    }

    public static void EnsurePaymentsCoverTotal(decimal grandTotal, IReadOnlyList<SalePaymentInput> payments)
    {
        var captured = payments.Sum(p => p.Amount);
        if (Money.Round(captured) != Money.Round(grandTotal))
        {
            throw new InvalidOperationException($"Payments must equal grand total. Expected {grandTotal}, received {captured}.");
        }
    }
}
