using Pos.Domain.Identity;

namespace Pos.Application.Administration;

public sealed record RoleResponse(Guid Id, string Name, long Version, IReadOnlyList<Permission> Permissions);
public sealed record SaveRoleRequest(string Name, Permission[] Permissions, long? Version);
public sealed record UserResponse(Guid Id, string DisplayName, string Email, bool IsActive, long Version, IReadOnlyList<Guid> RoleIds);
public sealed record CreateUserRequest(string DisplayName, string Email, string Password, Guid[] RoleIds);
public sealed record UpdateUserRequest(string DisplayName, bool IsActive, Guid[] RoleIds, long Version);
public sealed record ResetPasswordRequest(string Password, long Version);
public sealed record AuditResponse(Guid Id, Guid? ActorUserId, string? ActorName, string Action, string EntityName, Guid EntityId,
    Guid? TerminalId, DateTimeOffset OccurredAtUtc, string? BeforeJson, string? AfterJson);
