using Microsoft.EntityFrameworkCore;
using Pos.Application.Common;
using Pos.Infrastructure.Persistence;

namespace Pos.Application.Administration;

public sealed class AuditService(CommerceDbContext db, ITenantContext tenant)
{
    public async Task<PagedResponse<AuditResponse>> ListAsync(string? action, string? entity, Guid? actorId, DateTimeOffset? fromUtc, DateTimeOffset? toUtc, int page, int pageSize, CancellationToken ct)
    {
        if (fromUtc > toUtc) throw new AppValidationException(new Dictionary<string, string[]> { ["dates"] = ["Start must not be after end."] });
        page = Math.Clamp(page, 1, 1_000_000); pageSize = Math.Clamp(pageSize, 1, 100);
        var query = db.AuditLogs.AsNoTracking().Where(a => a.TenantId == tenant.TenantId);
        if (!string.IsNullOrWhiteSpace(action)) query = query.Where(a => a.Action == action.Trim());
        if (!string.IsNullOrWhiteSpace(entity)) query = query.Where(a => a.EntityName == entity.Trim());
        if (actorId.HasValue) query = query.Where(a => a.ActorUserId == actorId);
        if (fromUtc.HasValue) query = query.Where(a => a.OccurredAtUtc >= fromUtc.Value.ToUniversalTime());
        if (toUtc.HasValue) query = query.Where(a => a.OccurredAtUtc <= toUtc.Value.ToUniversalTime());
        var count = await query.CountAsync(ct);
        var items = await (from audit in query.OrderByDescending(a => a.OccurredAtUtc).ThenByDescending(a => a.Id).Skip((page - 1) * pageSize).Take(pageSize)
                           join user in db.Users.Where(u => u.TenantId == tenant.TenantId) on audit.ActorUserId equals user.Id into actors
                           from actor in actors.DefaultIfEmpty()
                           select new AuditResponse(audit.Id, audit.ActorUserId, actor == null ? null : actor.DisplayName, audit.Action, audit.EntityName,
                               audit.EntityId, audit.TerminalId, audit.OccurredAtUtc, audit.BeforeJson, audit.AfterJson)).ToArrayAsync(ct);
        return new(items, page, pageSize, count);
    }
}
