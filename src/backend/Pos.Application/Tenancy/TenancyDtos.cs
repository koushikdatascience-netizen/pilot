namespace Pos.Application.Tenancy;

public sealed record BootstrapResponse(Guid TenantId, string TenantName, Guid OrganizationId, Guid BusinessId, Guid LocationId, Guid TerminalId, Guid WarehouseId);
public sealed record TerminalContextResponse(Guid TenantId, Guid OrganizationId, Guid BusinessId, Guid LocationId, string LocationName, Guid TerminalId, string TerminalName, Guid WarehouseId);
