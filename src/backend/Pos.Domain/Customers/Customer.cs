using Pos.Domain.Common;

namespace Pos.Domain.Customers;

public sealed record Customer : TenantEntity
{
    public required string DisplayName { get; init; }
    public string? Phone { get; init; }
    public string? Email { get; init; }
    public string? Gstin { get; init; }
    public decimal CreditLimit { get; init; }
    public bool IsWalkIn { get; init; }
}
