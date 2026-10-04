using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Soso.Api;

public sealed class McpAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, Store store)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string TokenClaim = "mcp_token";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        var hasBearer = header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) && header.Length <= 256;
        if (!hasBearer)
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }
        var token = store.Tokens.FindById(AuthEndpoints.HashToken(header[7..]));
        var account = token is null ? null : store.Accounts.FindById(token.UserId);
        var invalid = token is null || token.ExpiresAt <= DateTime.UtcNow || account is null || account.Disabled;
        if (invalid)
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid or expired token."));
        }
        var principal = AuthEndpoints.Principal(account!);
        ((System.Security.Claims.ClaimsIdentity)principal.Identity!).AddClaim(new(TokenClaim, token!.Id));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
    }
}