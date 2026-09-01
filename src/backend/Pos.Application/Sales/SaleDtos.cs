using Pos.Domain.Sales;

namespace Pos.Application.Sales;

public sealed record CompleteSaleLineRequest(Guid ProductId, decimal Quantity, decimal DiscountAmount);
public sealed record CompleteSalePaymentRequest(PaymentMethod Method, decimal Amount, string? Reference);
public sealed record CompleteSaleRequest(Guid LocationId, Guid TerminalId, Guid? CustomerId, Guid ClientGeneratedId, IReadOnlyList<CompleteSaleLineRequest> Lines, IReadOnlyList<CompleteSalePaymentRequest> Payments);
public sealed record PaymentResponse(Guid Id, PaymentMethod Method, decimal Amount, string Currency, PaymentStatus Status, string? Reference);
public sealed record SaleLineResponse(Guid Id, Guid ProductId, string ProductName, string Sku, string? Barcode, decimal Quantity, decimal UnitPrice, decimal DiscountAmount, decimal TaxableAmount, decimal TaxRate, decimal TaxAmount, decimal CgstAmount, decimal SgstAmount, decimal IgstAmount, decimal LineTotal);
public sealed record SaleResponse(Guid Id, Guid LocationId, Guid TerminalId, Guid? CustomerId, string InvoiceNumber, SaleStatus Status, string Currency, decimal Subtotal, decimal DiscountTotal, decimal TaxTotal, decimal GrandTotal, Guid ClientGeneratedId, DateTimeOffset? CompletedAtUtc, SyncStatus SyncStatus, IReadOnlyList<SaleLineResponse> Lines, IReadOnlyList<PaymentResponse> Payments, bool IdempotentReplay);
