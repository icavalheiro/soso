using System.Net.WebSockets;
using System.Text.Json;

namespace Soso.Api;

public static class BoardEventEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static void MapBoardEvents(this WebApplication app)
    {
        app.MapGet("/api/events", Handle).RequireAuthorization();
    }

    private static async Task Handle(HttpContext context, BoardEventHub events)
    {
        if (!IsSameOrigin(context.Request))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        using var subscription = events.Subscribe(BoardService.UserId(context.User), out var subscriptionReader);
        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        using var eventSubscription = subscriptionReader;
        using var connection = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        var receive = ReceiveUntilClosed(socket, connection);
        try
        {
            await foreach (var item in eventSubscription.ReadAllAsync(connection.Token))
            {
                var payload = JsonSerializer.SerializeToUtf8Bytes(item, JsonOptions);
                await socket.SendAsync(payload, WebSocketMessageType.Text, true, connection.Token);
            }
        }
        catch (OperationCanceledException) when (connection.IsCancellationRequested)
        {
        }
        catch (WebSocketException)
        {
        }
        finally
        {
            connection.Cancel();
            try
            {
                await receive;
            }
            catch (OperationCanceledException)
            {
            }
            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Connection closed", CancellationToken.None);
            }
        }
    }

    private static async Task ReceiveUntilClosed(WebSocket socket, CancellationTokenSource connection)
    {
        try
        {
            var buffer = new byte[1024];
            while (socket.State == WebSocketState.Open)
            {
                var result = await socket.ReceiveAsync(buffer.AsMemory(), connection.Token);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (connection.IsCancellationRequested)
        {
        }
        catch (WebSocketException)
        {
        }
        finally
        {
            connection.Cancel();
        }
    }

    private static bool IsSameOrigin(HttpRequest request)
    {
        var originHeader = request.Headers.Origin.ToString();
        var expectedOrigin = new UriBuilder(request.Scheme, request.Host.Host, request.Host.Port ?? (request.IsHttps ? 443 : 80)).Uri;
        var validOrigin = Uri.TryCreate(originHeader, UriKind.Absolute, out var origin)
            && origin.UserInfo.Length == 0
            && origin.AbsolutePath == "/"
            && origin.Query.Length == 0
            && origin.Fragment.Length == 0;
        if (!validOrigin)
        {
            return false;
        }
        return origin == expectedOrigin;
    }
}
