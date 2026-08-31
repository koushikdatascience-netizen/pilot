using Pos.Domain.Common;

namespace Pos.Domain.Sales;

public enum SaleStatus { Draft, Held, Completed, Voided, Refunded }
public enum PaymentMethod { Cash, Upi, Card, Wallet, Account }
public enum PaymentStatus { Pending, Captured, Failed, Refunded }
public enum SyncStatus { LocalOnly, PendingSync, Synced, Failed }

public sealed record Sale : TenantEntity
{
    public required Guid LocationId { get; init; }
    public required Guid TerminalId { get; init; }
    public Guid? CustomerId { get; init; }
    public required string InvoiceNumber { get; init; }
    public required SaleStatus Status { get; init; }
    public required string Currency { get; init; }
    public required decimal Subtotal { get; init; }
    public required decimal DiscountTotal { get; init; }
    public required decimal TaxTotal { get; init; }
    public required decimal GrandTotal { get; init; }
    public required Guid ClientGeneratedId { get; init; }
    public DateTimeOffset? CompletedAtUtc { get; init; }
    public required SyncStatus SyncStatus { get; init; }
    public required IReadOnlyList<SaleLine> Lines { get; init; }
    public required IReadOnlyList<Payment> Payments { get; init; }
}

public sealed record SaleLine : TenantEntity
{
    public required Guid SaleId { get; init; }
    public required Guid ProductId { get; init; }
    public required string ProductNameSnapshot { get; init; }
    public required string SkuSnapshot { get; init; }
    public string? BarcodeSnapshot { get; init; }
    public required decimal Quantity { get; init; }
    public required decimal UnitPrice { get; init; }
    public required decimal DiscountAmount { get; init; }
    public required decimal TaxRate { get; init; }
    public required decimal TaxAmount { get; init; }
    public required decimal LineTotal { get; init; }
}

public sealed record Payment : TenantEntity
{
    public required Guid SaleId { get; init; }
    public required PaymentMethod Method { get; init; }
    public required decimal Amount { get; init; }
    public required string Currency { get; init; }
    public required PaymentStatus Status { get; init; }
    public string? Provider { get; init; }
    public string? Reference { get; init; }
    public DateTimeOffset? CapturedAtUtc { get; init; }
}
