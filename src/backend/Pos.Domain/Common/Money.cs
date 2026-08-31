namespace Pos.Domain.Common;

public readonly record struct Money(decimal Amount, string Currency)
{
    public static Money Inr(decimal amount) => new(Round(amount), "INR");

    public static decimal Round(decimal amount) => decimal.Round(amount, 2, MidpointRounding.AwayFromZero);

    public Money Add(Money other)
    {
        EnsureSameCurrency(other);
        return new Money(Round(Amount + other.Amount), Currency);
    }

    public Money Subtract(Money other)
    {
        EnsureSameCurrency(other);
        return new Money(Round(Amount - other.Amount), Currency);
    }

    private void EnsureSameCurrency(Money other)
    {
        if (!string.Equals(Currency, other.Currency, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Currency mismatch: {Currency} vs {other.Currency}.");
        }
    }
}
