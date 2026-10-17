namespace Pos.Application.Customers;

public sealed record CustomerResponse(Guid Id, string DisplayName, string? Phone, string? Email, string? Gstin, string? StateCode, decimal CreditLimit, bool IsWalkIn);
public sealed record CreateCustomerRequest(string DisplayName, string? Phone, string? Email, string? Gstin, string? StateCode, decimal CreditLimit);
