using Pos.Domain.Common;

namespace Pos.Domain.Identity;

public enum Permission
{
    ManageCatalog,
    ManageInventory,
    CompleteSale,
    ApplyDiscount,
    ViewReports,
    ManageSettings,
    ManageUsers,
    ViewAudit,
    ProcessReturns,
    VoidSales,
    ManageCashDrawer
}

public sealed record Role : TenantEntity
{
    public required string Name { get; init; }
    public required IReadOnlySet<Permission> Permissions { get; init; }
}

public sealed record User : TenantEntity
{
    public required string DisplayName { get; init; }
    public required string Email { get; init; }
    public required string PasswordHash { get; init; }
    public required IReadOnlySet<Guid> RoleIds { get; init; }
    public bool IsActive { get; init; } = true;
}
