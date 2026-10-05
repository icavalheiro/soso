using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Soso.Api;
using Xunit;

namespace Soso.Api.Tests;

public sealed class ModificationAuditTests
{
    [Fact]
    public async Task SuccessfulMutationLogsActorResourceAndTraceWithoutRequestContent()
    {
        var logger = new CapturingLogger();
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Put;
        context.Request.Path = "/api/boards/board-id/tickets/ticket-id";
        context.Request.QueryString = new QueryString("?private=not-logged");
        context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "user-id")], "test"));
        context.TraceIdentifier = "trace-id";

        var middleware = new ModificationAuditMiddleware(_ =>
        {
            context.Response.StatusCode = StatusCodes.Status200OK;
            return Task.CompletedTask;
        }, logger);
        await middleware.InvokeAsync(context);

        var message = Assert.Single(logger.Messages);
        Assert.Contains("PUT /api/boards/board-id/tickets/ticket-id", message);
        Assert.Contains("user-id", message);
        Assert.Contains("trace-id", message);
        Assert.DoesNotContain("private", message);
        Assert.Equal("trace-id", context.Response.Headers["X-Trace-Id"]);
    }

    [Fact]
    public async Task ReadsAndFailedMutationsAreNotLogged()
    {
        var logger = new CapturingLogger();
        var context = new DefaultHttpContext();
        var middleware = new ModificationAuditMiddleware(_ => Task.CompletedTask, logger);

        await middleware.InvokeAsync(context);
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/api/boards";
        middleware = new ModificationAuditMiddleware(_ =>
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return Task.CompletedTask;
        }, logger);
        await middleware.InvokeAsync(context);

        Assert.Empty(logger.Messages);
    }

    private sealed class CapturingLogger : ILogger<ModificationAuditMiddleware>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
        }
    }
}
