using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Pos.Infrastructure.Persistence;
using Pos.Application.Auth;

namespace Pos.Api.Security;

public sealed class SignedBearerAuthenticationOptions : AuthenticationSchemeOptions;

public sealed class SignedBearerAuthenticationHandler : AuthenticationHandler<SignedBearerAuthenticationOptions>
{
    public const string SchemeName = "SignedBearer";
    private readonly IConfiguration _configuration;
    private readonly CommerceDbContext _db;

    public SignedBearerAuthenticationHandler(IOptionsMonitor<SignedBearerAuthenticationOptions> options, ILoggerFactory logger, UrlEncoder encoder, IConfiguration configuration, CommerceDbContext db)
        : base(options, logger, encoder)
    {
        _configuration = configuration;
        _db = db;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(header) || !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticateResult.NoResult();
        }

        System.Security.Claims.ClaimsPrincipal? principal;
        try { principal = HmacTokenIssuer.Validate(header["Bearer ".Length..].Trim(), _configuration); }
        catch (Exception ex) when (ex is FormatException or System.Text.Json.JsonException or ArgumentException)
        {
            return AuthenticateResult.Fail("Invalid bearer token.");
        }
        if (principal is null ||
            !Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ||
            !Guid.TryParse(principal.FindFirstValue("tenant_id"), out var tenantId))
            return AuthenticateResult.Fail("Invalid bearer token.");
        var user = await _db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == userId && u.TenantId == tenantId && u.IsActive, Context.RequestAborted);
        var active = user is not null && int.TryParse(principal.FindFirstValue("auth_version"), out var version) && user.AuthVersion == version;
        active = active && await _db.Tenants.AsNoTracking().AnyAsync(t => t.Id == tenantId && t.Status == Pos.Domain.Tenancy.TenantStatus.Active, Context.RequestAborted);
        if (!active) return AuthenticateResult.Fail("Account is inactive.");
        var identity = (ClaimsIdentity)principal.Identity!;
        foreach (var claim in identity.FindAll("permission").ToArray()) identity.RemoveClaim(claim);
        foreach (var permission in await IdentityPermissions.LoadAsync(_db, tenantId, userId, Context.RequestAborted))
            identity.AddClaim(new Claim("permission", permission.ToString()));
        return AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName));
    }
}
