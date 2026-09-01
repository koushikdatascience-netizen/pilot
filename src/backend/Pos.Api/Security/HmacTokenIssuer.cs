using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Pos.Application.Auth;
using Pos.Application.Common;

namespace Pos.Api.Security;

public sealed class HmacTokenIssuer : ITokenIssuer
{
    private readonly IConfiguration _configuration;

    public HmacTokenIssuer(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public string Issue(CurrentUser user)
    {
        var payload = new TokenPayload(user.UserId, user.TenantId, user.Email, user.Permissions.Select(p => p.ToString()).ToArray(), DateTimeOffset.UtcNow.AddHours(8).ToUnixTimeSeconds());
        var payloadPart = Base64Url(JsonSerializer.SerializeToUtf8Bytes(payload));
        return $"{payloadPart}.{Sign(payloadPart, SigningKey(_configuration))}";
    }

    public static ClaimsPrincipal? Validate(string token, IConfiguration configuration)
    {
        var parts = token.Split('.');
        if (parts.Length != 2) return null;

        var expected = Sign(parts[0], SigningKey(configuration));
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(parts[1]))) return null;

        var payload = JsonSerializer.Deserialize<TokenPayload>(Base64UrlDecode(parts[0]));
        if (payload is null || payload.ExpiresAtUnix <= DateTimeOffset.UtcNow.ToUnixTimeSeconds()) return null;

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, payload.UserId.ToString()),
            new(ClaimTypes.Email, payload.Email),
            new("tenant_id", payload.TenantId.ToString())
        };
        claims.AddRange(payload.Permissions.Select(p => new Claim("permission", p)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, SignedBearerAuthenticationHandler.SchemeName));
    }

    private static string Sign(string payload, string key)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
        return Base64Url(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload)));
    }

    private static string SigningKey(IConfiguration configuration)
    {
        return configuration["Auth:SigningKey"]
            ?? configuration["AUTH_SIGNING_KEY"]
            ?? Environment.GetEnvironmentVariable("AUTH_SIGNING_KEY")
            ?? (string.Equals(configuration["ASPNETCORE_ENVIRONMENT"], "Testing", StringComparison.OrdinalIgnoreCase) ? "testing-signing-key" : null)
            ?? throw new InvalidOperationException("Auth signing key is required.");
    }

    private static string Base64Url(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');
        return Convert.FromBase64String(padded);
    }

    private sealed record TokenPayload(Guid UserId, Guid TenantId, string Email, IReadOnlyList<string> Permissions, long ExpiresAtUnix);
}
