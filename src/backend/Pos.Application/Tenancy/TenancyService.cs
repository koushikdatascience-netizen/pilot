using Microsoft.EntityFrameworkCore;
using Pos.Application.Common;
using Pos.Infrastructure.Persistence;

namespace Pos.Application.Tenancy;

public sealed class TenancyService
{
    private readonly CommerceDbContext _db;
    private readonly ITenantContext _tenant;

    public TenancyService(CommerceDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task<BootstrapResponse> BootstrapAsync(CancellationToken cancellationToken)
    {
        var tenant = await _db.Tenants.AsNoTracking().SingleAsync(t => t.Id == _tenant.TenantId, cancellationToken);
        var organization = await _db.Organizations.AsNoTracking().FirstAsync(o => o.TenantId == _tenant.TenantId, cancellationToken);
        var business = await _db.Businesses.AsNoTracking().FirstAsync(b => b.TenantId == _tenant.TenantId && b.OrganizationId == organization.Id, cancellationToken);
        var location = await _db.Locations.AsNoTracking().FirstAsync(l => l.TenantId == _tenant.TenantId && l.BusinessId == business.Id, cancellationToken);
        var terminal = await _db.Terminals.AsNoTracking().FirstAsync(t => t.TenantId == _tenant.TenantId && t.LocationId == location.Id, cancellationToken);
        return new BootstrapResponse(tenant.Id, tenant.Name, organization.Id, business.Id, location.Id, terminal.Id, location.DefaultSalesWarehouseId);
    }

    public async Task<IReadOnlyList<TerminalContextResponse>> TerminalsAsync(CancellationToken cancellationToken)
    {
        return await _db.Terminals.AsNoTracking()
            .Where(t => t.TenantId == _tenant.TenantId && t.Status == Pos.Domain.Tenancy.TerminalStatus.Active)
            .Join(_db.Locations.Where(l => l.TenantId == _tenant.TenantId), t => t.LocationId, l => l.Id, (t, l) => new { Terminal = t, Location = l })
            .OrderBy(x => x.Location.Name).ThenBy(x => x.Terminal.Name)
            .Select(x => new TerminalContextResponse(_tenant.TenantId, x.Location.OrganizationId, x.Location.BusinessId, x.Location.Id, x.Location.Name, x.Terminal.Id, x.Terminal.Name, x.Location.DefaultSalesWarehouseId))
            .ToArrayAsync(cancellationToken);
    }
}
