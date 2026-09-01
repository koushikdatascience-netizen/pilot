using System.Security.Claims;
using Pos.Application.Common;
using Pos.Domain.Identity;

namespace Pos.Api.Security;

public sealed class HttpCurrentUserAccessor : ICurrentUserAccessor
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpCurrentUserAccessor(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public CurrentUser Current
    {
        get
        {
            var user = _httpContextAccessor.HttpContext?.User;
            if (user?.Identity?.IsAuthenticated != true) throw new UnauthorizedAccessException();

            var userId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new UnauthorizedAccessException());
            var tenantId = Guid.Parse(user.FindFirstValue("tenant_id") ?? throw new UnauthorizedAccessException());
            var email = user.FindFirstValue(ClaimTypes.Email) ?? "";
            var permissions = user.FindAll("permission")
                .Select(c => Enum.TryParse<Permission>(c.Value, out var parsed) ? parsed : (Permission?)null)
                .Where(p => p.HasValue)
                .Select(p => p!.Value)
                .ToHashSet();

            return new CurrentUser(userId, tenantId, email, permissions);
        }
    }
}
