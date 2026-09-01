namespace Pos.Application.Tenancy;

public sealed record BootstrapResponse(Guid TenantId, string TenantName, Guid OrganizationId, Guid BusinessId, Guid LocationId, Guid TerminalId, Guid WarehouseId);
