using System.Net;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Soso.Api;

if (args.Contains("--healthcheck"))
{
    using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
    try
    {
        var response = await client.GetAsync("http://127.0.0.1:8080/api/health");
        Environment.ExitCode = response.IsSuccessStatusCode ? 0 : 1;
    }
    catch (HttpRequestException)
    {
        Environment.ExitCode = 1;
    }
    catch (TaskCanceledException)
    {
        Environment.ExitCode = 1;
    }
    return;
}

var builder = WebApplication.CreateBuilder(args);
var isDevelopment = builder.Environment.IsDevelopment();
var localHttpRequested = builder.Configuration.GetValue<bool>("LocalHttp");
var allowLocalHttp = isDevelopment && localHttpRequested;
var cookieSecurePolicy = allowLocalHttp ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
var dataPath = builder.Configuration["DataPath"] ?? "data";
Directory.CreateDirectory(dataPath);
builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataPath, "keys")));
builder.Services.AddSingleton<Store>();
builder.Services.AddSingleton<BoardService>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddMcpServer(options => options.ServerInfo = new() { Name = "Soso", Version = "1.0.0" })
    .WithHttpTransport(options => options.Stateless = true).WithTools<McpTools>();
builder.Services.Configure<ForwardedHeadersOptions>(options => TunnelProxy.Configure(options, builder.Configuration));
builder.Services.AddSingleton<IPasswordHasher<Account>, PasswordHasher<Account>>();
builder.Services.AddProblemDetails();
builder.Services.AddValidation();
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.Name = allowLocalHttp ? "soso-local-csrf" : "__Host-soso-csrf";
    options.Cookie.SecurePolicy = cookieSecurePolicy;
    options.Cookie.SameSite = SameSiteMode.Strict;
});
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNameCaseInsensitive = false;
    options.SerializerOptions.NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.Strict;
    options.SerializerOptions.AllowDuplicateProperties = false;
});
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
{
    options.Cookie.Name = allowLocalHttp ? "soso-local" : "__Host-soso";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = cookieSecurePolicy;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.ExpireTimeSpan = TimeSpan.FromHours(12);
    options.SlidingExpiration = false;
    options.Events.OnRedirectToLogin = context =>
    {
        context.Response.StatusCode = 401;
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = context =>
    {
        context.Response.StatusCode = 403;
        return Task.CompletedTask;
    };
    options.Events.OnValidatePrincipal = context =>
    {
        var store = context.HttpContext.RequestServices.GetRequiredService<Store>();
        var id = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        var account = id is null ? null : store.Accounts.FindById(id);
        var stamp = context.Principal?.FindFirstValue("stamp");
        var invalid = account is null || account.Disabled || account.SecurityStamp != stamp;
        if (invalid)
        {
            context.RejectPrincipal();
        }
        return Task.CompletedTask;
    };
}).AddScheme<AuthenticationSchemeOptions, McpAuthentication>("McpBearer", _ => { });
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
    options.AddPolicy("Admin", policy => policy.RequireRole("admin"));
    options.AddPolicy("Mcp", policy => policy.AddAuthenticationSchemes("McpBearer").RequireAuthenticatedUser());
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ =>
            new FixedWindowRateLimiterOptions { PermitLimit = 240, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ =>
            new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 6 * 1024 * 1024);
var app = builder.Build();
AuthEndpoints.Bootstrap(app.Services.GetRequiredService<Store>(), app.Configuration, app.Services.GetRequiredService<IPasswordHasher<Account>>());
app.UseForwardedHeaders();
if (!app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Testing"))
{
    app.UseHsts();
    app.Use(async (context, next) =>
    {
        var internalHealth = context.Request.Path == "/api/health" && context.Connection.RemoteIpAddress is not null && IPAddress.IsLoopback(context.Connection.RemoteIpAddress);
        if (!context.Request.IsHttps && !internalHealth)
        {
            context.Response.StatusCode = 400;
            return;
        }
        await next();
    });
}
app.UseExceptionHandler();
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' blob: data:; font-src 'self'; connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
    try
    {
        await next();
    }
    catch (ApiException exception)
    {
        await TypedResults.Problem(exception.Message, statusCode: exception.Status).ExecuteAsync(context);
    }
    catch (AntiforgeryValidationException)
    {
        await TypedResults.Problem("Invalid CSRF token. Refresh and try again.", statusCode: 400).ExecuteAsync(context);
    }
});
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();
app.UseAntiforgery();
app.Use(async (context, next) =>
{
    var isApi = context.Request.Path.StartsWithSegments("/api");
    var isRead = HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method) || HttpMethods.IsOptions(context.Request.Method);
    if (isApi && !isRead)
    {
        await context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context);
    }
    if (isApi)
    {
        context.Response.Headers.CacheControl = "no-store";
    }
    await next();
});
app.MapAccounts();
app.MapBoards();
app.MapMcp("/mcp").RequireAuthorization("Mcp");
app.MapGet("/api/health", () => TypedResults.Ok(new { status = "ok" })).AllowAnonymous();
app.MapFallback(async context =>
{
    var isBackend = context.Request.Path.StartsWithSegments("/api") || context.Request.Path.StartsWithSegments("/mcp");
    var file = Path.Combine(app.Environment.WebRootPath ?? "wwwroot", "index.html");
    if (isBackend || !File.Exists(file))
    {
        context.Response.StatusCode = 404;
        return;
    }
    context.Response.ContentType = "text/html";
    await context.Response.SendFileAsync(file);
}).AllowAnonymous();
app.Run();

public partial class Program;