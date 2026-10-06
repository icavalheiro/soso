using System.Diagnostics;
using System.Security.Claims;

namespace Soso.Api;

public sealed class ModificationAuditMiddleware(RequestDelegate next, ILogger<ModificationAuditMiddleware> logger)
{
    private static readonly EventId ModificationRecorded = new(1001, nameof(ModificationRecorded));

    public async Task InvokeAsync(HttpContext context)
    {
        var isModification = HttpMethods.IsPost(context.Request.Method)
            || HttpMethods.IsPut(context.Request.Method)
            || HttpMethods.IsPatch(context.Request.Method)
            || HttpMethods.IsDelete(context.Request.Method);
        var isApplicationRoute = context.Request.Path.StartsWithSegments("/api")
            || context.Request.Path.StartsWithSegments("/mcp");

        if (!isModification || !isApplicationRoute)
        {
            await next(context);
            return;
        }

        var traceId = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;
        context.Response.Headers["X-Trace-Id"] = traceId;
        await next(context);

        if (context.Response.StatusCode is >= 200 and < 400)
        {
            if (!context.Request.Path.StartsWithSegments("/api/admin/backup"))
            {
                try
                {
                    context.RequestServices.GetService<Store>()?.MarkModified();
                }
                catch (ArgumentNullException)
                {
                    // Middleware unit tests can invoke this without a configured service provider.
                }
            }
            var actorId = context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous";
            logger.LogInformation(
                ModificationRecorded,
                "Modification completed: {Method} {Path} returned {StatusCode} for actor {ActorId}; trace {TraceId}",
                context.Request.Method,
                context.Request.Path.Value,
                context.Response.StatusCode,
                actorId,
                traceId);
        }
    }
}
