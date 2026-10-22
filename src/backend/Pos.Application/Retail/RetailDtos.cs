using Pos.Domain.Sales;

namespace Pos.Application.Retail;

public sealed record ReturnLineRequest(Guid SaleLineId, decimal Quantity, bool Restock);
public sealed record RefundRequest(PaymentMethod Method, decimal Amount, string? Reference);
public sealed record CorrectionRequest(Guid TerminalId, Guid ClientGeneratedId, string Reason, ReturnLineRequest[] Lines, RefundRequest[] Refunds, SaleStatus? ExpectedStatus = null);
public sealed record CorrectionLineResponse(Guid SaleLineId, decimal Quantity, bool Restock, Guid? WarehouseId, decimal DiscountAmount, decimal TaxableAmount, decimal CgstAmount, decimal SgstAmount, decimal IgstAmount, decimal Total);
public sealed record CorrectionResponse(Guid Id, Guid SaleId, Guid TerminalId, Guid ClientGeneratedId, string Number, string Kind, string Reason, decimal Total, DateTimeOffset OccurredAtUtc, IReadOnlyList<CorrectionLineResponse> Lines, IReadOnlyList<RefundRequest> Refunds, bool IdempotentReplay);
public sealed record CorrectionHistoryResponse(string EffectiveStatus, IReadOnlyList<CorrectionResponse> Corrections);
public sealed record CorrectionQuoteResponse(string Kind, decimal Total, IReadOnlyList<CorrectionLineResponse> Lines, IReadOnlyList<RefundRequest> TenderAvailable);
public sealed record OpenDrawerRequest(Guid ClientGeneratedId, decimal OpeningFloat);
public sealed record CloseDrawerRequest(Guid ClientGeneratedId, decimal CountedCash, string? Note);
public sealed record DrawerMovementRequest(Guid ClientGeneratedId, string Kind, decimal Amount, string Reason);
public sealed record DrawerMovementResponse(Guid Id, string Kind, Guid SourceId, decimal AmountDelta, string Reason, Guid ActorUserId, DateTimeOffset OccurredAtUtc);
public sealed record DrawerSessionResponse(Guid Id, Guid TerminalId, DateTimeOffset OpenedAtUtc, DateTimeOffset? ClosedAtUtc, decimal OpeningFloat, decimal ExpectedCash, decimal? CountedCash, decimal? Variance, string? CloseNote, IReadOnlyList<DrawerMovementResponse> Movements);
