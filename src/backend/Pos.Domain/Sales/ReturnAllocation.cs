using Pos.Domain.Common;

namespace Pos.Domain.Sales;

public sealed record ReturnAmounts(decimal Taxable, decimal Cgst, decimal Sgst, decimal Igst, decimal Total);

public static class ReturnAllocation
{
    public static ReturnAmounts Line(decimal total, decimal taxable, decimal cgst, decimal sgst, decimal igst, decimal sold, decimal returned, decimal quantity)
    {
        if (taxable < 0 || cgst < 0 || sgst < 0 || igst < 0 || total != taxable + cgst + sgst + igst || (igst > 0 && cgst + sgst > 0))
            throw new ArgumentException("Unsupported historical tax components.");
        Component(total, sold, returned, quantity);
        ReturnAmounts Cumulative(decimal units)
        {
            var gross = Money.Round(total * (units / sold));
            var originalTax = cgst + sgst + igst;
            var tax = total == 0 ? 0 : Money.Round(gross * (originalTax / total));
            var central = originalTax == 0 || igst > 0 ? 0 : Money.Round(tax * (cgst / originalTax));
            return new(gross - tax, central, igst > 0 ? 0 : tax - central, igst > 0 ? tax : 0, gross);
        }
        var before = Cumulative(returned); var after = Cumulative(returned + quantity);
        return new(after.Taxable - before.Taxable, after.Cgst - before.Cgst, after.Sgst - before.Sgst, after.Igst - before.Igst, after.Total - before.Total);
    }

    public static decimal Component(decimal originalAmount, decimal soldQuantity, decimal returnedQuantity, decimal quantity)
    {
        if (originalAmount < 0 || soldQuantity <= 0 || returnedQuantity < 0 || quantity <= 0 || returnedQuantity + quantity > soldQuantity)
            throw new ArgumentOutOfRangeException(nameof(quantity), "Invalid cumulative return allocation.");
        return Money.Round(originalAmount * ((returnedQuantity + quantity) / soldQuantity))
            - Money.Round(originalAmount * (returnedQuantity / soldQuantity));
    }
}
