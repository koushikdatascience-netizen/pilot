using Microsoft.EntityFrameworkCore;
using Pos.Application.Common;
using Pos.Domain.Security;
using Pos.Infrastructure.Persistence;

namespace Pos.Application.Auth;

public interface ITokenIssuer
{
    string Issue(CurrentUser user);
}

public sealed class AuthService
{
    private readonly CommerceDbContext _db;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ITokenIssuer _tokenIssuer;

    public AuthService(CommerceDbContext db, IPasswordHasher passwordHasher, ITokenIssuer tokenIssuer)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _tokenIssuer = tokenIssuer;
    }

    public async Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        {
            throw new AppValidationException(new Dictionary<string, string[]> { ["credentials"] = ["Email and password are required."] });
        }

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var user = await _db.Users.SingleOrDefaultAsync(u => u.Email.ToLower() == normalizedEmail && u.IsActive, cancellationToken)
            ?? throw new ForbiddenException("Invalid credentials.");

        if (!_passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            throw new ForbiddenException("Invalid credentials.");
        }
        if (!await _db.Tenants.AnyAsync(t => t.Id == user.TenantId && t.Status == Pos.Domain.Tenancy.TenantStatus.Active, cancellationToken))
            throw new ForbiddenException("Account is inactive.");

        var permissions = await IdentityPermissions.LoadAsync(_db, user.TenantId, user.Id, cancellationToken);

        var currentUser = new CurrentUser(user.Id, user.TenantId, user.Email, permissions.ToHashSet(), user.AuthVersion);
        return new LoginResponse(_tokenIssuer.Issue(currentUser), user.TenantId, user.Id, user.DisplayName, user.Email, permissions);
    }
}
