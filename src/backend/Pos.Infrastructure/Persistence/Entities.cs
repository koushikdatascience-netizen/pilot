using Pos.Domain.Catalog;
using Pos.Domain.Identity;
using Pos.Domain.Inventory;
using Pos.Domain.Sales;
using Pos.Domain.Tenancy;

namespace Pos.Infrastructure.Persistence;

public abstract class TenantEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public long Version { get; set; } = 1;
}

public sealed class Tenant
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public TenantStatus Status { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class Organization : TenantEntity
{
    public string Name { get; set; } = "";
    public string LegalName { get; set; } = "";
    public string? Gstin { get; set; }
}

public sealed class Business : TenantEntity
{
    public Guid OrganizationId { get; set; }
    public string Name { get; set; } = "";
    public BusinessType BusinessType { get; set; }
}

public sealed class Location : TenantEntity
{
    public Guid OrganizationId { get; set; }
    public Guid BusinessId { get; set; }
    public string Name { get; set; } = "";
    public string Code { get; set; } = "";
    public string StateCode { get; set; } = "";
    public string? Address { get; set; }
    public Guid DefaultSalesWarehouseId { get; set; }
    public decimal CashierMaxDiscountPercent { get; set; } = 0.10m;
    public decimal ManagerMaxDiscountPercent { get; set; } = 1.00m;
}

public sealed class Terminal : TenantEntity
{
    public Guid LocationId { get; set; }
    public string Name { get; set; } = "";
    public string DeviceCode { get; set; } = "";
    public TerminalStatus Status { get; set; }
}

public sealed class Warehouse : TenantEntity
{
    public Guid LocationId { get; set; }
    public string Name { get; set; } = "";
    public string Code { get; set; } = "";
}

public sealed class Role : TenantEntity
{
    public string Name { get; set; } = "";
}

public sealed class RolePermission
{
    public Guid RoleId { get; set; }
    public Permission Permission { get; set; }
}

public sealed class User : TenantEntity
{
    public string DisplayName { get; set; } = "";
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public int AuthVersion { get; set; } = 1;
}

public sealed class UserRole
{
    public Guid UserId { get; set; }
    public Guid RoleId { get; set; }
}

public sealed class Category : TenantEntity
{
    public string Name { get; set; } = "";
    public Guid? ParentCategoryId { get; set; }
}

public sealed class TaxCategory : TenantEntity
{
    public string Name { get; set; } = "";
    public string CountryCode { get; set; } = "";
    public decimal Rate { get; set; }
    public TaxBehavior TaxBehavior { get; set; }
}

public sealed class Product : TenantEntity
{
    public Guid CategoryId { get; set; }
    public Guid TaxCategoryId { get; set; }
    public string Name { get; set; } = "";
    public string Sku { get; set; } = "";
    public string? Barcode { get; set; }
    public string? HsnSac { get; set; }
    public string UnitOfMeasure { get; set; } = "";
    public decimal PriceAmount { get; set; }
    public string Currency { get; set; } = "INR";
    public bool IsStockTracked { get; set; } = true;
    public bool IsActive { get; set; } = true;
}

public sealed class Customer : TenantEntity
{
    public string DisplayName { get; set; } = "";
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Gstin { get; set; }
    public string? StateCode { get; set; }
    public decimal CreditLimit { get; set; }
    public bool IsWalkIn { get; set; }
}

public sealed class Sale : TenantEntity
{
    public string? ReceiptJson { get; set; }
    public Guid LocationId { get; set; }
    public Guid TerminalId { get; set; }
    public Guid? CustomerId { get; set; }
    public string InvoiceNumber { get; set; } = "";
    public SaleStatus Status { get; set; }
    public string Currency { get; set; } = "INR";
    public decimal Subtotal { get; set; }
    public decimal DiscountTotal { get; set; }
    public decimal TaxTotal { get; set; }
    public decimal GrandTotal { get; set; }
    public Guid ClientGeneratedId { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public SyncStatus SyncStatus { get; set; }
}

public sealed class SaleLine : TenantEntity
{
    public string? HsnSacSnapshot { get; set; }
    public Guid SaleId { get; set; }
    public Guid ProductId { get; set; }
    public string ProductNameSnapshot { get; set; } = "";
    public string SkuSnapshot { get; set; } = "";
    public string? BarcodeSnapshot { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxableAmount { get; set; }
    public decimal TaxRate { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal CgstAmount { get; set; }
    public decimal SgstAmount { get; set; }
    public decimal IgstAmount { get; set; }
    public decimal LineTotal { get; set; }
}

public sealed class Payment : TenantEntity
{
    public Guid SaleId { get; set; }
    public PaymentMethod Method { get; set; }
    public decimal Amount { get; set; }
    public decimal AmountDue { get; set; }
    public decimal AmountTendered { get; set; }
    public decimal ChangeDue { get; set; }
    public string Currency { get; set; } = "INR";
    public PaymentStatus Status { get; set; }
    public string? Provider { get; set; }
    public string? Reference { get; set; }
    public DateTimeOffset? CapturedAtUtc { get; set; }
}

public sealed class InventoryTransaction : TenantEntity
{
    public Guid LocationId { get; set; }
    public Guid WarehouseId { get; set; }
    public Guid ProductId { get; set; }
    public InventoryTransactionType Type { get; set; }
    public decimal QuantityDelta { get; set; }
    public decimal? UnitCost { get; set; }
    public string? SourceDocumentType { get; set; }
    public Guid? SourceDocumentId { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; }
    public Guid? CreatedByUserId { get; set; }
}

public sealed class AuditLog : TenantEntity
{
    public Guid? ActorUserId { get; set; }
    public string Action { get; set; } = "";
    public string EntityName { get; set; } = "";
    public Guid EntityId { get; set; }
    public Guid? TerminalId { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; }
    public string? BeforeJson { get; set; }
    public string? AfterJson { get; set; }
}

public sealed class OutboxEvent : TenantEntity
{
    public string EventType { get; set; } = "";
    public string PayloadJson { get; set; } = "";
    public DateTimeOffset OccurredAtUtc { get; set; }
    public DateTimeOffset? ProcessedAtUtc { get; set; }
    public int Attempts { get; set; }
}

public sealed class InvoiceSequence
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid LocationId { get; set; }
    public string FinancialPeriod { get; set; } = "";
    public string Prefix { get; set; } = "";
    public int NextNumber { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public uint xmin { get; set; }
}

public sealed class SaleCorrection : TenantEntity
{
    public Guid SaleId { get; set; }
    public Guid TerminalId { get; set; }
    public Guid ClientGeneratedId { get; set; }
    public string RequestHash { get; set; } = "";
    public string Number { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Reason { get; set; } = "";
    public decimal Total { get; set; }
    public Guid ActorUserId { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; }
}

public sealed class SaleCorrectionLine : TenantEntity
{
    public Guid CorrectionId { get; set; }
    public Guid SaleLineId { get; set; }
    public Guid? WarehouseId { get; set; }
    public decimal Quantity { get; set; }
    public bool Restock { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxableAmount { get; set; }
    public decimal CgstAmount { get; set; }
    public decimal SgstAmount { get; set; }
    public decimal IgstAmount { get; set; }
    public decimal Total { get; set; }
}

public sealed class SaleRefund : TenantEntity
{
    public Guid CorrectionId { get; set; }
    public PaymentMethod Method { get; set; }
    public decimal Amount { get; set; }
    public string? Reference { get; set; }
}

public sealed class CashDrawerSession : TenantEntity
{
    public Guid TerminalId { get; set; }
    public Guid OpenCommandId { get; set; }
    public string OpenRequestHash { get; set; } = "";
    public decimal OpeningFloat { get; set; }
    public Guid OpenedByUserId { get; set; }
    public DateTimeOffset OpenedAtUtc { get; set; }
    public Guid? CloseCommandId { get; set; }
    public string? CloseRequestHash { get; set; }
    public Guid? ClosedByUserId { get; set; }
    public DateTimeOffset? ClosedAtUtc { get; set; }
    public decimal? ExpectedAtClose { get; set; }
    public decimal? CountedAtClose { get; set; }
    public decimal? Variance { get; set; }
    public string? CloseNote { get; set; }
}

public sealed class CashDrawerMovement : TenantEntity
{
    public Guid SessionId { get; set; }
    public string Kind { get; set; } = "";
    public Guid SourceId { get; set; }
    public string? RequestHash { get; set; }
    public decimal AmountDelta { get; set; }
    public string Reason { get; set; } = "";
    public Guid ActorUserId { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; }
}

public sealed class TerminalDevice : TenantEntity
{
    public Guid TerminalId { get; set; }
    public Guid DeviceId { get; set; }
    public long AcknowledgedSequence { get; set; }
    public long OfferedSequence { get; set; }
    public DateTimeOffset LastSeenAtUtc { get; set; }
}

public sealed class SyncChange : TenantEntity
{
    public long Sequence { get; set; }
    public Guid EventId { get; set; }
    public int SchemaVersion { get; set; } = 1;
    public string EventType { get; set; } = "";
    public string PayloadJson { get; set; } = "";
}
