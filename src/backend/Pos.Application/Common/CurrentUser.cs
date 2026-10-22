using Pos.Domain.Identity;

namespace Pos.Application.Common;

public sealed record CurrentUser(Guid UserId, Guid TenantId, string Email, IReadOnlySet<Permission> Permissions, int AuthVersion = 1);

public interface ICurrentUserAccessor
{
    CurrentUser Current { get; }
}

public interface ITenantContext
{
    Guid TenantId { get; }
    Guid UserId { get; }
    bool HasPermission(Permission permission);
}

public sealed class TenantContext : ITenantContext
{
    private readonly ICurrentUserAccessor _accessor;

    public TenantContext(ICurrentUserAccessor accessor)
    {
        _accessor = accessor;
    }

    public Guid TenantId => _accessor.Current.TenantId;
    public Guid UserId => _accessor.Current.UserId;
    public bool HasPermission(Permission permission) => _accessor.Current.Permissions.Contains(permission);
}
