using Microsoft.EntityFrameworkCore;
using Pos.Domain.Identity;
using Pos.Infrastructure.Persistence;

namespace Pos.Application.Auth;

public static class IdentityPermissions
{
    public static Task<Permission[]> LoadAsync(CommerceDbContext db, Guid tenantId, Guid userId, CancellationToken ct) =>
        (from assignment in db.UserRoles
         join role in db.Roles on assignment.RoleId equals role.Id
         join permission in db.RolePermissions on role.Id equals permission.RoleId
         where assignment.UserId == userId && role.TenantId == tenantId
         select permission.Permission).Distinct().ToArrayAsync(ct);
}
