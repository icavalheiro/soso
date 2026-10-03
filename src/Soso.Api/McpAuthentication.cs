using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Soso.Api;

public sealed class McpAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, Store store)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
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
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(AuthEndpoints.Principal(account!), Scheme.Name)));
    }
}