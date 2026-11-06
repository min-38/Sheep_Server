using System.Net.WebSockets;
using System.Text.Json;
using SheepServer.Rooms;
using SheepServer.Arena;
using SheepServer.Clock;

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

    // 받기 루프의 답과 틱 루프의 state가 같은 소켓으로 나감. 보내기가 겹치면 WebSocket이 던져서 한 번에 하나씩 보냄
    var sendLock = new SemaphoreSlim(1, 1);
    async Task SendLocked(object message)
    {
        await sendLock.WaitAsync(ct);
        try
        {
            await Send(ws, message, ct);
        }
        finally
        {
            sendLock.Release();
        }
    }

    // 방 안 모두에게 메시지들을 순서대로 보냄. TickLoop과 같은 방식.
    // ponytail: 한 명씩 기다리며 보내 느린 연결 하나가 쏜 사람의 받기 루프를 늦출 수 있음. TickLoop과 같은 한계이고, 문제가 되면 연결마다 보낼 큐를 둠
    async Task Broadcast(Room room, List<object> messages)
    {
        foreach (var send in room.GetSenders())
        {
            try
            {
                foreach (var message in messages)
                {
                    await send(message);
                }
            }
            // 막 나간 사람의 연결이면 던짐. 그 사람만 건너뛰고 나머지에게는 보냄
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Broadcast failed {ConnectionId}", id);
            }
        }
    }

    // 받은 메시지 하나를 처리하고 보낼 답들을 순서대로 돌려줌 (기술 설계서 메시지).
    // 발사처럼 방 안 모두에게 가는 것은 여기서 보내고 끝나길 기다림
    async Task<object[]> Handle(string text)
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
                    roomId = registry.AddRoom(playerId, MaxPlayers, SendLocked);
                    return [new { type = "roomCreated", code = roomId, playerId }, mapMessage];

                case "joinRoom":
                    string? code = root.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.String
                        ? c.GetString()!.ToUpperInvariant() : null;
                    if (code != null && code == roomId)
                    {
                        return [new { type = "joined", code, playerId }, mapMessage];
                    }
                    switch (registry.JoinRoom(code, playerId, SendLocked))
                    {
                        case JoinResult.Joined:
                            // 새 방에 들어간 뒤에야 옛 방에서 나감. 코드를 잘못 쳐도 있던 방을 잃지 않게 함
                            registry.LeaveRoom(roomId, playerId);
                            roomId = code;
                            return [new { type = "joined", code, playerId }, mapMessage];
                        case JoinResult.Full:
                            return [new { type = "error", reason = "full" }];
                        case JoinResult.Playing:
                            return [new { type = "error", reason = "playing" }];
                        default:
                            return [new { type = "error", reason = "notFound" }];
                    }

                case "turn":
                    // 숫자인지 먼저 봄. 아니면 TryGetDouble이 던져 연결이 끊김. 범위 검사는 Room.SetAngle이 함
                    if (root.TryGetProperty("angle", out var a) && a.ValueKind == JsonValueKind.Number && a.TryGetDouble(out double angle))
                    {
                        registry.GetRoomById(roomId)?.SetAngle(playerId, angle);
                    }
                    return [];

                case "ready":
                    // true·false인지 먼저 봄. 아니면 GetBoolean이 던져 연결이 끊김
                    if (root.TryGetProperty("ready", out var r) && (r.ValueKind == JsonValueKind.True || r.ValueKind == JsonValueKind.False))
                    {
                        registry.GetRoomById(roomId)?.SetReady(playerId, r.GetBoolean());
                    }
                    return [];

                case "fire":
                    // 숫자인지 먼저 봄. 아니면 TryGetDouble이 던져 연결이 끊김. 범위 검사와 판정은 Room.Fire가 함
                    if (root.TryGetProperty("angle", out var angleProp) && angleProp.ValueKind == JsonValueKind.Number && angleProp.TryGetDouble(out double fireAngle)
                        && root.TryGetProperty("at", out var atProp) && atProp.ValueKind == JsonValueKind.Number && atProp.TryGetDouble(out double fireAt)
                        && registry.GetRoomById(roomId) is Room room)
                    {
                        // shot은 틱을 기다리지 않고 바로 방 안 모두(쏜 사람 포함)에게 보냄. 명중은 틱에서 정함 (기술 설계서 메시지 shot, eliminated)
                        await Broadcast(room, room.Fire(playerId, fireAngle, fireAt, ServerClock.Now));
                    }
                    return [];

                case "clockSync":
                    // 클라가 보낸 시각을 그대로 돌려줌. 클라가 시각을 찍어 보내는 순간과 서버가 받는 순간 사이에 네트워크 지연이 있음
                    // 1e400 같은 값은 무한대가 돼 답을 JSON으로 만들 때 던지므로 IsFinite까지 봄
                    if (root.TryGetProperty("t0", out var t0Prop) && t0Prop.ValueKind == JsonValueKind.Number && t0Prop.TryGetDouble(out double t0) && double.IsFinite(t0))
                    {
                        double ts = ServerClock.Now;
                        return [new { type = "serverTime", t0, ts }];
                    }
                    return [];

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
            foreach (var reply in await Handle(text))
            {
                await SendLocked(reply);
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
