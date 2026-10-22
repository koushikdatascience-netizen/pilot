using Pos.Domain.Sales;
using Xunit;

namespace Pos.Domain.Tests;

public sealed class ReturnAllocationTests
{
    [Theory]
    [InlineData("0.01", "3", "1")]
    [InlineData("100.00", "3", "1")]
    [InlineData("0.07", "0.003", "0.001")]
    public void Partial_allocations_are_nonnegative_and_sum_to_the_exact_original(string amount, string sold, string step)
    {
        var original = decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture);
        var quantity = decimal.Parse(sold, System.Globalization.CultureInfo.InvariantCulture);
        var part = decimal.Parse(step, System.Globalization.CultureInfo.InvariantCulture);
        decimal total = 0;
        for (decimal returned = 0; returned < quantity; returned += part)
        {
            var next = ReturnAllocation.Component(original, quantity, returned, part);
            Assert.True(next >= 0);
            total += next;
        }
        Assert.Equal(original, total);
    }

    [Fact]
    public void Gross_refund_is_exact_and_coupled_tax_rounding_never_produces_negative_components()
    {
        for (var baseCents = 0; baseCents < 8; baseCents++)
        for (var taxCents = 0; taxCents < 8; taxCents++)
        {
            decimal taxable = baseCents / 100m, cgst = taxCents / 100m, sgst = taxCents / 100m;
            var total = taxable + cgst + sgst;
            var parts = Enumerable.Range(0, 20).Select(i => ReturnAllocation.Line(total, taxable, cgst, sgst, 0, 20, i, 1)).ToArray();
            Assert.All(parts, p => { Assert.True(p.Taxable >= 0 && p.Cgst >= 0 && p.Sgst >= 0); Assert.Equal(p.Total, p.Taxable + p.Cgst + p.Sgst); });
            Assert.Equal(total, parts.Sum(p => p.Total)); Assert.Equal(taxable, parts.Sum(p => p.Taxable));
            Assert.Equal(cgst, parts.Sum(p => p.Cgst)); Assert.Equal(sgst, parts.Sum(p => p.Sgst));
        }
        Assert.Equal(10m, ReturnAllocation.Line(20m, 19.05m, .48m, .47m, 0, 2, 0, 1).Total);
    }

    [Fact]
    public void Invalid_allocations_are_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ReturnAllocation.Component(10, 1, 1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => ReturnAllocation.Component(10, 0, 0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => ReturnAllocation.Component(-1, 1, 0, 1));
    }
}
