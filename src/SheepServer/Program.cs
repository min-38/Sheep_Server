using System.Net.WebSockets;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.UseWebSockets();

app.MapGet("/", () => "Hello World!");

app.Map("/ws", async (HttpContext context, ILogger<Program> logger) =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }

    using var ws = await context.WebSockets.AcceptWebSocketAsync();
    var id = context.Connection.Id;
    logger.LogInformation("WebSocket connected {ConnectionId}", id);
    try
    {
        await Echo(ws, context.RequestAborted);
        logger.LogInformation("WebSocket closed {ConnectionId}", id);
    }
    // 클라가 close 프레임 없이 끊으면 여기로 옴. 요청 하나의 끝일 뿐이라 로그만 남김
    catch (Exception ex) when (ex is WebSocketException or OperationCanceledException)
    {
        logger.LogInformation("WebSocket dropped {ConnectionId}", id);
    }
});

app.Run();

static async Task Echo(WebSocket ws, CancellationToken ct)
{
    var buffer = new byte[4 * 1024];
    while (true)
    {
        var result = await ws.ReceiveAsync(buffer.AsMemory(), ct);
        if (result.MessageType == WebSocketMessageType.Close)
        {
            await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, null, ct);
            return;
        }
        await ws.SendAsync(buffer.AsMemory(0, result.Count), result.MessageType, result.EndOfMessage, ct);
    }
}
