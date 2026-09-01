using Pos.Domain.Identity;

namespace Pos.Application.Auth;

public sealed record LoginRequest(string Email, string Password);
public sealed record LoginResponse(string AccessToken, Guid TenantId, Guid UserId, string DisplayName, string Email, IReadOnlyCollection<Permission> Permissions);
