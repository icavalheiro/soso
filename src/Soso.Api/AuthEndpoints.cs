using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;

namespace Soso.Api;

public static class AuthEndpoints
{
    public static ClaimsPrincipal Principal(Account account) => new(new ClaimsIdentity(
        [new(ClaimTypes.NameIdentifier, account.Id), new(ClaimTypes.Name, account.Name), new("stamp", account.SecurityStamp), new(ClaimTypes.Role, account.IsAdmin ? "admin" : "user")], CookieAuthenticationDefaults.AuthenticationScheme));

    public static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    public static void Bootstrap(Store store, IConfiguration configuration, IPasswordHasher<Account> hasher)
    {
        var hasAdmin = store.Accounts.FindAll().Any(account => account.IsAdmin && !account.Disabled);
        if (hasAdmin)
        {
            return;
        }
        var email = configuration["Bootstrap:Email"];
        var password = configuration["Bootstrap:Password"];
        var validEmail = new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(email);
        var validPassword = password is { Length: >= 14 and <= 128 };
        if (!validEmail || string.IsNullOrWhiteSpace(email) || !validPassword)
        {
            throw new InvalidOperationException("Set Bootstrap__Email and Bootstrap__Password (14-128 characters) to create the initial administrator.");
        }
        var normalized = email.Trim().ToLowerInvariant();
        var existing = store.Accounts.FindOne(account => account.Email == normalized);
        if (existing is not null)
        {
            throw new InvalidOperationException("Bootstrap email already belongs to a non-admin account. Restore administrator access explicitly.");
        }
        var account = new Account { Email = normalized, Name = configuration["Bootstrap:Name"] ?? "Administrator", IsAdmin = true };
        account.PasswordHash = hasher.HashPassword(account, password!);
        store.Accounts.Insert(account);
    }

    public static void MapAccounts(this WebApplication app)
    {
        var group = app.MapGroup("/api/auth");
        group.MapGet("/csrf", (HttpContext context, IAntiforgery antiforgery) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            return TypedResults.Ok(new { token = antiforgery.GetAndStoreTokens(context).RequestToken });
        }).AllowAnonymous();
        group.MapPost("/login", async (LoginRequest request, HttpContext context, Store store, IPasswordHasher<Account> hasher) =>
        {
            Account? account;
            lock (store.Gate)
            {
                var email = request.Email.Trim().ToLowerInvariant();
                account = store.Accounts.FindOne(account => account.Email == email);
                var placeholder = new Account();
                var hash = account?.PasswordHash ?? hasher.HashPassword(placeholder, "dummy-password-not-an-account");
                var result = hasher.VerifyHashedPassword(account ?? placeholder, hash, request.Password);
                var locked = account?.LockedUntil > DateTime.UtcNow;
                var rejected = account is null || account.Disabled || locked || result == PasswordVerificationResult.Failed;
                if (rejected)
                {
                    if (account is not null && !locked)
                    {
                        account.FailedLogins++;
                        if (account.FailedLogins >= 5)
                        {
                            account.LockedUntil = DateTime.UtcNow.AddMinutes(15);
                            account.FailedLogins = 0;
                        }
                        store.Accounts.Update(account);
                    }
                    throw new ApiException(401, "Invalid credentials or temporarily locked account.");
                }
                account!.FailedLogins = 0;
                account.LockedUntil = null;
                if (result == PasswordVerificationResult.SuccessRehashNeeded)
                {
                    account.PasswordHash = hasher.HashPassword(account, request.Password);
                }
                store.Accounts.Update(account);
            }
            await context.SignInAsync(Principal(account!));
            return TypedResults.Ok(AccountResponse.From(account!));
        }).AllowAnonymous().RequireRateLimiting("login");
        group.MapPost("/logout", async (HttpContext context) =>
        {
            await context.SignOutAsync();
            return TypedResults.NoContent();
        });
        group.MapGet("/me", (ClaimsPrincipal user, Store store) => TypedResults.Ok(AccountResponse.From(store.Accounts.FindById(BoardService.UserId(user)))));
        group.MapPut("/profile", (ProfileRequest request, ClaimsPrincipal user, Store store) =>
        {
            lock (store.Gate)
            {
                var account = store.Accounts.FindById(BoardService.UserId(user));
                account.Name = BoardService.Text(request.Name, 80);
                account.Theme = request.Theme;
                account.Settings = request.Settings;
                store.Accounts.Update(account);
                return TypedResults.Ok(AccountResponse.From(account));
            }
        });
        group.MapPut("/password", async (PasswordRequest request, HttpContext context, Store store, IPasswordHasher<Account> hasher) =>
        {
            lock (store.Gate)
            {
                var account = store.Accounts.FindById(BoardService.UserId(context.User));
                var result = hasher.VerifyHashedPassword(account, account.PasswordHash, request.CurrentPassword);
                if (result == PasswordVerificationResult.Failed)
                {
                    throw new ApiException(400, "Current password is incorrect.");
                }
                account.PasswordHash = hasher.HashPassword(account, request.NewPassword);
                account.SecurityStamp = Guid.NewGuid().ToString("N");
                store.Accounts.Update(account);
                store.Tokens.DeleteMany(token => token.UserId == account.Id);
            }
            await context.SignOutAsync();
            return TypedResults.NoContent();
        }).RequireRateLimiting("login");
        group.MapGet("/tokens", (ClaimsPrincipal user, Store store) =>
        {
            var id = BoardService.UserId(user);
            return TypedResults.Ok(store.Tokens.Find(token => token.UserId == id).Select(token => new { token.Id, token.Name, token.ExpiresAt, token.BoardIds }));
        });
        group.MapPost("/tokens", (TokenRequest request, ClaimsPrincipal user, Store store) =>
        {
            lock (store.Gate)
            {
                var id = BoardService.UserId(user);
                if (store.Tokens.Count(token => token.UserId == id) >= 10)
                {
                    throw new ApiException(400, "Revoke an existing token first (limit: 10).");
                }
                var secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
                var token = new AccessToken { Id = HashToken(secret), UserId = id, Name = request.Name.Trim(), ExpiresAt = DateTime.UtcNow.AddDays(30) };
                store.Tokens.Insert(token);
                return TypedResults.Ok(new { token = secret, token.ExpiresAt });
            }
        });
        group.MapPut("/tokens/{id}/boards", (string id, UpdateTokenBoardsRequest request, ClaimsPrincipal user, Store store, BoardService service) =>
        {
            lock (store.Gate)
            {
                var token = store.Tokens.FindById(id);
                var ownsToken = token?.UserId == BoardService.UserId(user);
                if (!ownsToken)
                {
                    throw new ApiException(404, "Token not found.");
                }
                var boardIds = request.BoardIds.Distinct().ToList();
                foreach (var boardId in boardIds)
                {
                    service.RequireBoard(boardId, user);
                }
                token!.BoardIds = boardIds;
                if (!store.Tokens.Update(token))
                {
                    throw new ApiException(500, "Token permissions could not be saved.");
                }
                var savedToken = store.Tokens.FindById(id) ?? throw new ApiException(500, "Token permissions could not be saved.");
                return TypedResults.Ok(new { savedToken.Id, savedToken.Name, savedToken.ExpiresAt, savedToken.BoardIds });
            }
        });
        group.MapDelete("/tokens/{id}", (string id, ClaimsPrincipal user, Store store) =>
        {
            lock (store.Gate)
            {
                var token = store.Tokens.FindById(id);
                if (token?.UserId != BoardService.UserId(user))
                {
                    throw new ApiException(404, "Token not found.");
                }
                store.Tokens.Delete(id);
                return TypedResults.NoContent();
            }
        });
        app.MapGet("/api/people", (Store store) => TypedResults.Ok(store.Accounts.FindAll().Where(account => !account.Disabled).Select(account => new { account.Id, account.Name, account.AvatarId })));
        var admin = app.MapGroup("/api/admin/accounts").RequireAuthorization("Admin");
        admin.MapGet("/", (Store store) => TypedResults.Ok(store.Accounts.FindAll().Select(AccountResponse.From)));
        admin.MapPost("/", (CreateAccountRequest request, Store store, IPasswordHasher<Account> hasher) =>
        {
            lock (store.Gate)
            {
                var email = request.Email.Trim().ToLowerInvariant();
                if (store.Accounts.Exists(account => account.Email == email))
                {
                    throw new ApiException(409, "Email already in use.");
                }
                var account = new Account { Email = email, Name = BoardService.Text(request.Name, 80), IsAdmin = request.IsAdmin };
                account.PasswordHash = hasher.HashPassword(account, request.Password);
                store.Accounts.Insert(account);
                return TypedResults.Created($"/api/admin/accounts/{account.Id}", AccountResponse.From(account));
            }
        });
        admin.MapPut("/{id}", (string id, AccountStateRequest request, Store store, IPasswordHasher<Account> hasher) =>
        {
            lock (store.Gate)
            {
                var account = store.Accounts.FindById(id) ?? throw new ApiException(404, "Account not found.");
                var boardIds = request.BoardIds?.Distinct().ToList();
                var boardsExist = boardIds is null || boardIds.All(boardId => store.Boards.FindById(boardId) is not null);
                if (!boardsExist)
                {
                    throw new ApiException(400, "Unknown board assignment.");
                }
                var lastAdmin = account.IsAdmin && request.Disabled && store.Accounts.FindAll().Count(user => user.IsAdmin && !user.Disabled) <= 1;
                if (lastAdmin)
                {
                    throw new ApiException(400, "Cannot disable the last administrator.");
                }
                account.Disabled = request.Disabled;
                if (boardIds is not null)
                {
                    account.BoardIds = boardIds;
                }
                account.SecurityStamp = Guid.NewGuid().ToString("N");
                account.FailedLogins = 0;
                account.LockedUntil = null;
                if (request.Password is not null)
                {
                    account.PasswordHash = hasher.HashPassword(account, request.Password);
                }
                store.Accounts.Update(account);
                store.Tokens.DeleteMany(token => token.UserId == id);
                return TypedResults.Ok(AccountResponse.From(account));
            }
        });
    }
}
