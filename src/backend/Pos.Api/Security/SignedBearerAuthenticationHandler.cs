using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Pos.Api.Security;

public sealed class SignedBearerAuthenticationOptions : AuthenticationSchemeOptions;

public sealed class SignedBearerAuthenticationHandler : AuthenticationHandler<SignedBearerAuthenticationOptions>
{
    public const string SchemeName = "SignedBearer";
    private readonly IConfiguration _configuration;

    public SignedBearerAuthenticationHandler(IOptionsMonitor<SignedBearerAuthenticationOptions> options, ILoggerFactory logger, UrlEncoder encoder, IConfiguration configuration)
        : base(options, logger, encoder)
    {
        _configuration = configuration;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(header) || !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var principal = HmacTokenIssuer.Validate(header["Bearer ".Length..].Trim(), _configuration);
        return Task.FromResult(principal is null
            ? AuthenticateResult.Fail("Invalid bearer token.")
            : AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
    }
}
