using System.Net.WebSockets;
using System.Text.Json;
using SheepServer.Rooms;
using SheepServer.Arena;

// 컨셉 기획서: 한 방 2~7명
const int MaxPlayers = 7;

// 결투장 맵. 바뀌지 않아 한 번만 만들어 모든 연결이 같이 씀
var mapMessage = new MapMessage(ArenaMap.Fixed.Width, ArenaMap.Fixed.Height, ArenaMap.Fixed.Cells);

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<RoomRegistry>();
var app = builder.Build();

app.UseWebSockets();

app.MapGet("/", () => "Hello World!");

app.Map("/ws", async (HttpContext context, RoomRegistry registry, ILogger<Program> logger) =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }

    using var ws = await context.WebSockets.AcceptWebSocketAsync();
    var id = context.Connection.Id;
    var ct = context.RequestAborted;
    logger.LogInformation("WebSocket connected {ConnectionId}", id);

    // 연결 하나 = 플레이어 하나. 들어간 방은 이 연결이 기억함
    var playerId = Guid.NewGuid();
    string? roomId = null;

    // 받은 메시지 하나를 처리하고 보낼 답들을 순서대로 돌려줌 (기술 설계서 메시지)
    object[] Handle(string text)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(text);
        }
        catch (JsonException)
        {
            // 깨진 JSON은 무시함. 답할지는 기술 설계서 미정 사항
            return [];
        }

        using (doc)
        {
            var root = doc.RootElement;
            string? type = root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String
                ? t.GetString() : null;

            switch (type)
            {
                case "createRoom":
                    // 클라는 방 밖에서만 보내지만, 이미 방에 있으면 먼저 나가 두 방에 걸치지 않게 함
                    registry.LeaveRoom(roomId, playerId);
                    roomId = registry.AddRoom(playerId, MaxPlayers);
                    return [new { type = "roomCreated", code = roomId }, mapMessage];

                case "joinRoom":
                    string? code = root.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.String
                        ? c.GetString()!.ToUpperInvariant() : null;
                    if (code != null && code == roomId)
                    {
                        return [new { type = "joined", code }, mapMessage];
                    }
                    switch (registry.JoinRoom(code, playerId))
                    {
                        case JoinResult.Joined:
                            // 새 방에 들어간 뒤에야 옛 방에서 나감. 코드를 잘못 쳐도 있던 방을 잃지 않게 함
                            registry.LeaveRoom(roomId, playerId);
                            roomId = code;
                            return [new { type = "joined", code }, mapMessage];
                        case JoinResult.Full:
                            return [new { type = "error", reason = "full" }];
                        default:
                            return [new { type = "error", reason = "notFound" }];
                    }

                default:
                    // 모르는 type도 무시함 (기술 설계서 미정 사항)
                    return [];
            }
        }
    }

    try
    {
        while (await ReceiveText(ws, ct) is string text)
        {
            foreach (var reply in Handle(text))
            {
                await Send(ws, reply, ct);
            }
        }
        // 클라가 닫자고 했으면 닫기 응답을 보냄. 메시지가 너무 커서 서버가 먼저 닫았으면 이미 보냈음
        if (ws.State == WebSocketState.CloseReceived)
        {
            await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, null, ct);
        }
        logger.LogInformation("WebSocket closed {ConnectionId}", id);
    }
    // 클라가 close 프레임 없이 끊으면 여기로 옴. 요청 하나의 끝일 뿐이라 로그만 남김
    catch (Exception ex) when (ex is WebSocketException or OperationCanceledException)
    {
        logger.LogInformation("WebSocket dropped {ConnectionId}", id);
    }
    finally
    {
        // 정상 종료든 끊김이든 방에서 빠짐. 마지막 사람이면 방도 지워짐
        registry.LeaveRoom(roomId, playerId);
    }
});

app.Run();

// 메시지 하나를 끝까지 받아 문자열로 돌려줌. 상대가 닫거나 너무 큰 메시지를 보내면 null
static async Task<string?> ReceiveText(WebSocket ws, CancellationToken ct)
{
    // 지금 메시지는 수십 바이트. 끝없이 보내 서버 메모리를 채우는 것을 막음
    const int MaxMessageBytes = 4 * 1024;
    var buffer = new byte[1024];
    using var message = new MemoryStream();
    while (true)
    {
        var result = await ws.ReceiveAsync(buffer.AsMemory(), ct);
        if (result.MessageType == WebSocketMessageType.Close)
        {
            return null;
        }
        message.Write(buffer, 0, result.Count);
        if (message.Length > MaxMessageBytes)
        {
            await ws.CloseAsync(WebSocketCloseStatus.MessageTooBig, null, ct);
            return null;
        }
        if (result.EndOfMessage)
        {
            return System.Text.Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
        }
    }
}

// 답은 JSON 텍스트 한 개 (기술 설계서 메시지). Web 옵션이라 속성 이름이 camelCase로 나감
static ValueTask Send(WebSocket ws, object message, CancellationToken ct) =>
    ws.SendAsync(JsonSerializer.SerializeToUtf8Bytes(message, JsonSerializerOptions.Web).AsMemory(), WebSocketMessageType.Text, true, ct);
