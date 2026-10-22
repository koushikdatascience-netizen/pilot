using System.Data;
using System.Net.Mail;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pos.Application.Auth;
using Pos.Application.Common;
using Pos.Domain.Identity;
using Pos.Domain.Security;
using Pos.Infrastructure.Persistence;
using User = Pos.Infrastructure.Persistence.User;
using Role = Pos.Infrastructure.Persistence.Role;

namespace Pos.Application.Administration;

public sealed class AdministrationService(CommerceDbContext db, ITenantContext tenant, IPasswordHasher hasher)
{
    public async Task<IReadOnlyList<RoleResponse>> RolesAsync(CancellationToken ct)
    {
        var roles = await db.Roles.AsNoTracking().Where(r => r.TenantId == tenant.TenantId).OrderBy(r => r.Name).ToArrayAsync(ct);
        var ids = roles.Select(r => r.Id).ToArray();
        var permissions = await db.RolePermissions.Where(p => ids.Contains(p.RoleId)).ToArrayAsync(ct);
        return roles.Select(r => new RoleResponse(r.Id, r.Name, r.Version, permissions.Where(p => p.RoleId == r.Id).Select(p => p.Permission).Order().ToArray())).ToArray();
    }

    public async Task<PagedResponse<UserResponse>> UsersAsync(string? q, int page, int pageSize, CancellationToken ct)
    {
        page = Math.Clamp(page, 1, 1_000_000); pageSize = Math.Clamp(pageSize, 1, 100);
        var query = db.Users.AsNoTracking().Where(u => u.TenantId == tenant.TenantId);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var search = q.Trim().ToLowerInvariant();
            query = query.Where(u => u.DisplayName.ToLower().Contains(search) || u.Email.ToLower().Contains(search));
        }
        var count = await query.CountAsync(ct);
        var users = await query.OrderBy(u => u.DisplayName).ThenBy(u => u.Id).Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync(ct);
        var ids = users.Select(u => u.Id).ToArray();
        var roles = await (from assignment in db.UserRoles join role in db.Roles on assignment.RoleId equals role.Id
                           where ids.Contains(assignment.UserId) && role.TenantId == tenant.TenantId select assignment).ToArrayAsync(ct);
        return new(users.Select(u => Response(u, roles.Where(r => r.UserId == u.Id).Select(r => r.RoleId))).ToArray(), page, pageSize, count);
    }

    public async Task<RoleResponse> SaveRoleAsync(Guid? id, SaveRoleRequest request, CancellationToken ct)
    {
        var name = Name(request.Name);
        if (name.Length > 80) throw Invalid("name", "Role name must be at most 80 characters.");
        if (request.Permissions is null || request.Permissions.Length > 32 || request.Permissions.Any(p => !Enum.IsDefined(p))) throw Invalid("permissions", "Select valid permissions.");
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        await LockAndAuthorizeAsync(ct);
        var role = id.HasValue ? await db.Roles.SingleOrDefaultAsync(r => r.TenantId == tenant.TenantId && r.Id == id, ct)
            ?? throw new NotFoundException("Role not found.") : new Role { Id = Guid.NewGuid(), TenantId = tenant.TenantId };
        if (id.HasValue && role.Version != request.Version) throw new ConflictException("Role changed. Refresh before saving.");
        if (await db.Roles.AnyAsync(r => r.TenantId == tenant.TenantId && r.Id != role.Id && r.Name.ToLower() == name.ToLower(), ct))
            throw new ConflictException("Role name already exists.");
        var old = await db.RolePermissions.Where(p => p.RoleId == role.Id).ToArrayAsync(ct);
        var before = new { role.Name, Permissions = old.Select(p => p.Permission.ToString()).Order().ToArray() };
        var wanted = request.Permissions.Distinct().Order().ToArray();
        db.RolePermissions.RemoveRange(old.Where(p => !wanted.Contains(p.Permission)));
        foreach (var permission in wanted.Where(p => !old.Any(o => o.Permission == p)))
            db.RolePermissions.Add(new RolePermission { RoleId = role.Id, Permission = permission });
        role.Name = name;
        if (!id.HasValue) db.Roles.Add(role);
        else db.Entry(role).Property(r => r.Name).IsModified = true;
        Audit(id.HasValue ? "RoleUpdated" : "RoleCreated", "Role", role.Id, id.HasValue ? before : null,
            new { role.Name, Permissions = wanted.Select(p => p.ToString()).ToArray() });
        await db.SaveChangesAsync(ct);
        await EnsureAdministratorAsync(ct);
        await tx.CommitAsync(ct);
        return new(role.Id, role.Name, role.Version, wanted);
    }

    public async Task<UserResponse> CreateUserAsync(CreateUserRequest request, CancellationToken ct)
    {
        var name = Name(request.DisplayName);
        var email = request.Email?.Trim().ToLowerInvariant() ?? "";
        if (email.Length > 220 || !MailAddress.TryCreate(email, out var parsed) || parsed.Address != email)
            throw Invalid("email", "Enter a valid email address.");
        Password(request.Password);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        await LockAndAuthorizeAsync(ct);
        var roles = await ValidateRolesAsync(request.RoleIds, ct);
        if (await db.Users.AnyAsync(u => u.Email.ToLower() == email, ct)) throw new ConflictException("Email is unavailable.");
        var user = new User { Id = Guid.NewGuid(), TenantId = tenant.TenantId, DisplayName = name, Email = email, PasswordHash = hasher.Hash(request.Password) };
        db.Users.Add(user);
        foreach (var role in roles) db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role });
        Audit("UserCreated", "User", user.Id, null, UserAudit(user, roles));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Response(user, roles);
    }

    public async Task<UserResponse> UpdateUserAsync(Guid id, UpdateUserRequest request, CancellationToken ct)
    {
        var name = Name(request.DisplayName);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        await LockAndAuthorizeAsync(ct);
        var user = await UserAsync(id, request.Version, ct);
        var roles = await ValidateRolesAsync(request.RoleIds, ct);
        var old = await db.UserRoles.Where(r => r.UserId == user.Id).ToArrayAsync(ct);
        var before = UserAudit(user, old.Select(r => r.RoleId));
        db.UserRoles.RemoveRange(old.Where(r => !roles.Contains(r.RoleId)));
        foreach (var role in roles.Where(r => !old.Any(o => o.RoleId == r))) db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role });
        user.DisplayName = name;
        if (user.IsActive != request.IsActive) user.AuthVersion++;
        user.IsActive = request.IsActive;
        // Role-only edits must also advance the optimistic concurrency token.
        db.Entry(user).Property(u => u.DisplayName).IsModified = true;
        Audit("UserUpdated", "User", user.Id, before, UserAudit(user, roles));
        await db.SaveChangesAsync(ct);
        await EnsureAdministratorAsync(ct);
        await tx.CommitAsync(ct);
        return Response(user, roles);
    }

    public async Task<UserResponse> ResetPasswordAsync(Guid id, ResetPasswordRequest request, CancellationToken ct)
    {
        Password(request.Password);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        await LockAndAuthorizeAsync(ct);
        var user = await UserAsync(id, request.Version, ct);
        user.PasswordHash = hasher.Hash(request.Password);
        user.AuthVersion++;
        Audit("PasswordReset", "User", user.Id, null, new { SessionsRevoked = true });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        var roles = await db.UserRoles.Where(r => r.UserId == id).Select(r => r.RoleId).ToArrayAsync(ct);
        return Response(user, roles);
    }

    private async Task LockAndAuthorizeAsync(CancellationToken ct)
    {
        await TransactionLocks.LockAsync(db, $"identity:{tenant.TenantId}", ct);
        var active = await db.Users.AnyAsync(u => u.Id == tenant.UserId && u.TenantId == tenant.TenantId && u.IsActive, ct);
        if (!active || !(await IdentityPermissions.LoadAsync(db, tenant.TenantId, tenant.UserId, ct)).Contains(Permission.ManageUsers))
            throw new ForbiddenException("User administration permission is required.");
    }

    private async Task EnsureAdministratorAsync(CancellationToken ct)
    {
        var exists = await (from user in db.Users join assignment in db.UserRoles on user.Id equals assignment.UserId
                            join role in db.Roles on assignment.RoleId equals role.Id
                            join permission in db.RolePermissions on role.Id equals permission.RoleId
                            where user.TenantId == tenant.TenantId && user.IsActive && role.TenantId == tenant.TenantId && permission.Permission == Permission.ManageUsers
                            select user.Id).AnyAsync(ct);
        if (!exists) throw new ConflictException("At least one active user administrator must remain.");
    }

    private async Task<Guid[]> ValidateRolesAsync(Guid[]? roleIds, CancellationToken ct)
    {
        if (roleIds is null || roleIds.Length > 50) throw Invalid("roleIds", "Select at most 50 tenant roles.");
        var ids = roleIds.Distinct().ToArray();
        if (await db.Roles.CountAsync(r => r.TenantId == tenant.TenantId && ids.Contains(r.Id), ct) != ids.Length)
            throw Invalid("roleIds", "A selected role is unavailable.");
        return ids;
    }

    private async Task<User> UserAsync(Guid id, long version, CancellationToken ct)
    {
        var user = await db.Users.SingleOrDefaultAsync(u => u.TenantId == tenant.TenantId && u.Id == id, ct) ?? throw new NotFoundException("User not found.");
        if (user.Version != version) throw new ConflictException("User changed. Refresh before saving.");
        return user;
    }
    private void Audit(string action, string entity, Guid id, object? before, object after) => db.AuditLogs.Add(new AuditLog
    {
        Id = Guid.NewGuid(), TenantId = tenant.TenantId, ActorUserId = tenant.UserId, Action = action, EntityName = entity, EntityId = id,
        OccurredAtUtc = DateTimeOffset.UtcNow, BeforeJson = before is null ? null : JsonSerializer.Serialize(before), AfterJson = JsonSerializer.Serialize(after)
    });
    private static UserResponse Response(User user, IEnumerable<Guid> roles) => new(user.Id, user.DisplayName, user.Email, user.IsActive, user.Version, roles.Order().ToArray());
    private static object UserAudit(User user, IEnumerable<Guid> roles) => new { user.Id, user.DisplayName, user.Email, user.IsActive, RoleIds = roles.Order().ToArray() };
    private static string Name(string? value) => !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= 160 ? value.Trim() : throw Invalid("name", "Name is required and must be at most 160 characters.");
    private static void Password(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length is < 12 or > 128) throw Invalid("password", "Use a password of 12-128 characters.");
    }
    private static AppValidationException Invalid(string field, string message) => new(new Dictionary<string, string[]> { [field] = [message] });
}
