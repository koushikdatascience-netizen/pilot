using Pos.Domain.Common;

namespace Pos.Domain.Tenancy;

public enum TenantStatus { Active, Suspended }
public enum BusinessType { Retail, Grocery, Restaurant, Hotel, Pharmacy, Liquor, Mall, Wholesale, Service }
public enum TerminalStatus { Active, Disabled }

public sealed record Tenant(Guid Id, string Name, TenantStatus Status, DateTimeOffset CreatedAtUtc);

public sealed record Organization : TenantEntity
{
    public required string Name { get; init; }
    public required string LegalName { get; init; }
    public string? Gstin { get; init; }
}

public sealed record Business : TenantEntity
{
    public required Guid OrganizationId { get; init; }
    public required string Name { get; init; }
    public required BusinessType BusinessType { get; init; }
}

public sealed record Location : TenantEntity
{
    public required Guid OrganizationId { get; init; }
    public required string Name { get; init; }
    public required string Code { get; init; }
    public required string StateCode { get; init; }
    public string? Address { get; init; }
}

public sealed record Terminal : TenantEntity
{
    public required Guid LocationId { get; init; }
    public required string Name { get; init; }
    public required string DeviceCode { get; init; }
    public required TerminalStatus Status { get; init; }
}

public sealed record Warehouse : TenantEntity
{
    public required Guid LocationId { get; init; }
    public required string Name { get; init; }
    public required string Code { get; init; }
}
