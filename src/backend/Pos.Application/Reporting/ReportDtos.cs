namespace Pos.Application.Reporting;

public sealed record TodayReportResponse(decimal Revenue, int TransactionCount, decimal AverageOrderValue, decimal ItemsSold, IReadOnlyList<PaymentMixResponse> PaymentMix, IReadOnlyList<TopProductResponse> TopSellingProducts, IReadOnlyList<LowStockProductResponse> LowStockProducts);
public sealed record PaymentMixResponse(string Method, decimal Amount, int Count);
public sealed record TopProductResponse(Guid ProductId, string ProductName, decimal Quantity, decimal Revenue);
public sealed record LowStockProductResponse(Guid ProductId, string ProductName, decimal QuantityOnHand);
