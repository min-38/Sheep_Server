using System.Net.WebSockets;
using System.Text.Json;
using SheepServer.Rooms;
using SheepServer.Arena;
using SheepServer.Clock;

// 컨셉 기획서: 한 방 2~8명
const int MaxPlayers = 8;

// 결투장 맵. 바뀌지 않아 한 번만 만들어 모든 연결이 같이 씀
var mapMessage = new MapMessage(ArenaMap.Fixed.Width, ArenaMap.Fixed.Height, ArenaMap.Fixed.Cells);

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<RoomRegistry>();
var app = builder.Build();

app.UseWebSockets();

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

    // 받기 루프의 답, 틱 루프의 state, 다른 사람이 쏜 shot이 모두 이 큐를 거쳐 같은 소켓으로 나감.
    // 한도를 넘게 밀리면 연결을 끊어 받기 루프가 끝나고, 아래 finally에서 방을 나감 (기술 설계서 아키텍처)
    var queue = new SendQueue(message => Send(ws, message, ct).AsTask(), ws.Abort);
    var sending = SendAll();
    async Task SendAll()
    {
        try
        {
            await queue.RunAsync(ct);
        }
        // 보내다 끊기면 받기 루프도 끝나게 끊음. 그래야 방을 나감
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Send failed {ConnectionId}", id);
            ws.Abort();
        }
    }

    // 방은 보내기를 Func<object, Task>로 받음. 큐에 넣기만 하고 바로 끝나, 방의 틱과 다른 사람의 받기 루프가 이 연결을 기다리지 않음
    Task Enqueue(object message)
    {
        queue.Enqueue(message);
        return Task.CompletedTask;
    }

    // 방 안 모두의 큐에 메시지들을 순서대로 넣음. TickLoop과 같은 방식
    async Task Broadcast(Room room, List<object> messages)
    {
        foreach (var (to, send) in room.GetSenders())
        {
            try
            {
                foreach (var message in TickLoop.MessagesFor(to, messages))
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
    // 규칙에 맞으면 true와 그 이름, 그 밖(없거나 문자열이 아니거나 틀린 이름)은 false.
    // 첫 화면이 늘 닉네임을 보내므로 없으면 임시 이름을 주지 않고 거절함 (SHEEP-73).
    // 서버도 클라와 같은 규칙으로 다시 검사함 (시스템 기획서 방 화면 닉네임)
    static bool ReadNickname(JsonElement root, out string? nickname)
    {
        nickname = root.TryGetProperty("nickname", out var n) && n.ValueKind == JsonValueKind.String ? Nickname.Normalize(n.GetString()) : null;
        return nickname != null;
    }

    // 캐릭터 번호. 없으면 0번이고, 있으면 0 이상 캐릭터 수 미만 정수여야 함. 틀리면 false (기술 설계서 메시지 createRoom character).
    // 정수가 아니면 TryGetInt32가 false
    static bool ReadCharacter(JsonElement root, out int character)
    {
        character = 0;
        return !root.TryGetProperty("character", out var c)
            || (c.ValueKind == JsonValueKind.Number && c.TryGetInt32(out character) && Character.IsValid(character));
    }

    // 사람 색 번호. 없으면 null이고 방이 들어온 순서대로 줌. 있으면 0 이상 색 수 미만 정수여야 함. 틀리면 false (기술 설계서 메시지 createRoom color)
    static bool ReadColor(JsonElement root, out int? color)
    {
        color = null;
        if (!root.TryGetProperty("color", out var c))
        {
            return true;
        }
        if (c.ValueKind != JsonValueKind.Number || !c.TryGetInt32(out int value) || !PersonColor.IsValid(value))
        {
            return false;
        }
        color = value;
        return true;
    }

    // 총을 든 손. 없으면 오른손이고, 있으면 "right" 또는 "left"여야 함. 틀리면 false (기술 설계서 메시지 createRoom hand)
    static bool ReadHand(JsonElement root, out Hand hand)
    {
        hand = Hand.Right;
        if (!root.TryGetProperty("hand", out var h))
        {
            return true;
        }
        switch (h.ValueKind == JsonValueKind.String ? h.GetString() : null)
        {
            case "right":
                return true;
            case "left":
                hand = Hand.Left;
                return true;
            default:
                return false;
        }
    }

    // 방에 들어간 답(roomCreated, joined). 칠판에 적을 방 이름과 비공개 여부, 바 좌석 수가 되는 인원 제한을 함께 줌 (기술 설계서 메시지 roomCreated)
    object Entered(string type, string code)
    {
        var room = registry.GetRoomById(code)!;
        return new { type, code, playerId, name = room.Name, @private = room.IsPrivate, maxPlayers = room.MaxPlayers };
    }

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
                    if (!ReadNickname(root, out string? hostName))
                    {
                        return [new { type = "error", reason = "nickname" }];
                    }
                    if (!ReadCharacter(root, out int hostCharacter))
                    {
                        return [new { type = "error", reason = "character" }];
                    }
                    if (!ReadColor(root, out int? hostColor))
                    {
                        return [new { type = "error", reason = "color" }];
                    }
                    if (!ReadHand(root, out Hand hostHand))
                    {
                        return [new { type = "error", reason = "hand" }];
                    }
                    // 방 이름은 없으면 "닉네임's room", 있으면 규칙에 맞아야 함 (시스템 기획서 방 화면 방 만들기)
                    string? roomName = null;
                    if (root.TryGetProperty("name", out var nameProp))
                    {
                        roomName = nameProp.ValueKind == JsonValueKind.String ? RoomName.Normalize(nameProp.GetString()) : null;
                        if (roomName == null)
                        {
                            return [new { type = "error", reason = "roomName" }];
                        }
                    }

                    // 인원 제한은 없으면 8명, 있으면 2~8명 정수. 정수가 아니면 TryGetInt32가 false
                    int maxPlayers = MaxPlayers;
                    if (root.TryGetProperty("maxPlayers", out var maxProp)
                        && (maxProp.ValueKind != JsonValueKind.Number || !maxProp.TryGetInt32(out maxPlayers) || maxPlayers < 2 || maxPlayers > MaxPlayers))
                    {
                        return [new { type = "error", reason = "maxPlayers" }];
                    }

                    // private가 true일 때만 비공개 방. 없거나 다른 값이면 공개 방
                    bool isPrivate = root.TryGetProperty("private", out var p) && p.ValueKind == JsonValueKind.True;

                    // 클라는 방 밖에서만 보내지만, 이미 방에 있으면 먼저 나가 두 방에 걸치지 않게 함
                    registry.LeaveRoom(roomId, playerId, ServerClock.Now);
                    roomId = registry.AddRoom(playerId, maxPlayers, Enqueue, hostName, isPrivate, roomName, hostCharacter, hostColor, hostHand);
                    return [Entered("roomCreated", roomId), mapMessage];

                case "joinRoom":
                    if (!ReadNickname(root, out string? guestName))
                    {
                        return [new { type = "error", reason = "nickname" }];
                    }
                    if (!ReadCharacter(root, out int guestCharacter))
                    {
                        return [new { type = "error", reason = "character" }];
                    }
                    if (!ReadColor(root, out int? guestColor))
                    {
                        return [new { type = "error", reason = "color" }];
                    }
                    if (!ReadHand(root, out Hand guestHand))
                    {
                        return [new { type = "error", reason = "hand" }];
                    }
                    string? code = root.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.String
                        ? c.GetString()!.ToUpperInvariant() : null;
                    if (code != null && code == roomId)
                    {
                        return [Entered("joined", code), mapMessage];
                    }
                    switch (registry.JoinRoom(code, playerId, Enqueue, guestName, guestCharacter, guestColor, guestHand))
                    {
                        case JoinResult.Joined:
                            // 새 방에 들어간 뒤에야 옛 방에서 나감. 코드를 잘못 쳐도 있던 방을 잃지 않게 함
                            registry.LeaveRoom(roomId, playerId, ServerClock.Now);
                            roomId = code;
                            return [Entered("joined", code!), mapMessage];
                        case JoinResult.Full:
                            return [new { type = "error", reason = "full" }];
                        case JoinResult.Playing:
                            return [new { type = "error", reason = "playing" }];
                        default:
                            return [new { type = "error", reason = "notFound" }];
                    }

                case "listRooms":
                    // 방에 들어가기 전에도 받음. 첫 화면의 대기실 보기가 열려 있는 동안 2초마다 보냄
                    return [new { type = "rooms", rooms = registry.List() }];

                case "leaveRoom":
                    // 방에 없어도 답함. 대기실의 나가기 버튼이 첫 화면으로 돌아가는 신호로 씀
                    registry.LeaveRoom(roomId, playerId, ServerClock.Now);
                    roomId = null;
                    return [new { type = "left" }];

                case "turn":
                    // 숫자인지 먼저 봄. 아니면 TryGetDouble이 던져 연결이 끊김. 범위 검사는 Room.SetAngle이 함.
                    // pitch는 없거나 숫자가 아니면 null로 넘겨 위아래 시선만 그대로 둠. 지금 클라는 보내지 않음
                    if (root.TryGetProperty("angle", out var a) && a.ValueKind == JsonValueKind.Number && a.TryGetDouble(out double angle))
                    {
                        double? turnPitch = root.TryGetProperty("pitch", out var tp) && tp.ValueKind == JsonValueKind.Number && tp.TryGetDouble(out double tpValue) ? tpValue : null;
                        registry.GetRoomById(roomId)?.SetAngle(playerId, angle, turnPitch);
                    }
                    return [];

                case "ready":
                    // true·false인지 먼저 봄. 아니면 GetBoolean이 던져 연결이 끊김
                    if (root.TryGetProperty("ready", out var r) && (r.ValueKind == JsonValueKind.True || r.ValueKind == JsonValueKind.False))
                    {
                        registry.GetRoomById(roomId)?.SetReady(playerId, r.GetBoolean());
                    }
                    return [];

                case "dress":
                    // 둘 다 정수인지 먼저 봄. 아니면 GetInt32가 던져 연결이 끊김. 범위, 겹치는 색, 판 도중은 Room.Dress가 봄. 답은 없음
                    if (root.TryGetProperty("character", out var dressCharacter) && dressCharacter.ValueKind == JsonValueKind.Number && dressCharacter.TryGetInt32(out int newCharacter)
                        && root.TryGetProperty("color", out var dressColor) && dressColor.ValueKind == JsonValueKind.Number && dressColor.TryGetInt32(out int newColor))
                    {
                        registry.GetRoomById(roomId)?.Dress(playerId, newCharacter, newColor);
                    }
                    return [];

                case "fire":
                    // 숫자인지 먼저 봄. 아니면 TryGetDouble이 던져 연결이 끊김. pitch가 없어도 버림. 범위 검사와 판정은 Room.Fire가 함
                    if (root.TryGetProperty("angle", out var angleProp) && angleProp.ValueKind == JsonValueKind.Number && angleProp.TryGetDouble(out double fireAngle)
                        && root.TryGetProperty("pitch", out var pitchProp) && pitchProp.ValueKind == JsonValueKind.Number && pitchProp.TryGetDouble(out double firePitch)
                        && root.TryGetProperty("at", out var atProp) && atProp.ValueKind == JsonValueKind.Number && atProp.TryGetDouble(out double fireAt)
                        && registry.GetRoomById(roomId) is Room room)
                    {
                        // shot은 틱을 기다리지 않고 바로 방 안 모두(쏜 사람 포함)에게 보냄. 명중은 틱에서 정함 (기술 설계서 메시지 shot, eliminated)
                        await Broadcast(room, room.Fire(playerId, fireAngle, firePitch, fireAt, ServerClock.Now));
                    }
                    return [];

                case "draw":
                    // 숫자인지 먼저 봄. 아니면 TryGetDouble이 던져 연결이 끊김. 결투 중인지와 시각 검사는 Room.Draw가 함
                    if (root.TryGetProperty("at", out var drawAtProp) && drawAtProp.ValueKind == JsonValueKind.Number && drawAtProp.TryGetDouble(out double drawAt))
                    {
                        registry.GetRoomById(roomId)?.Draw(playerId, drawAt, ServerClock.Now);
                    }
                    return [];

                case "spectate":
                    // playerId가 id 문자열이면 그 사람을 보고, null이면 관전을 그만둠. 그 밖은 버림. 받는 조건은 Room.Spectate가 봄.
                    // TryGetGuid는 문자열이 아니면 던지므로 종류를 먼저 봄
                    if (root.TryGetProperty("playerId", out var watch)
                        && (watch.ValueKind == JsonValueKind.Null || (watch.ValueKind == JsonValueKind.String && watch.TryGetGuid(out _))))
                    {
                        Guid? watchId = watch.ValueKind == JsonValueKind.Null ? null : watch.GetGuid();
                        if (registry.GetRoomById(roomId)?.Spectate(playerId, watchId) is SpectatingMessage spectating)
                        {
                            return [spectating];
                        }
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
                queue.Enqueue(reply);
            }
        }
        // 남은 답을 마저 보낸 뒤에 닫아 보내기와 닫기가 겹치지 않게 함
        queue.Complete();
        await sending;
        // 클라가 닫자고 했으면 닫기 응답을 보냄. 메시지가 너무 커서 서버가 먼저 닫았으면 이미 보냈음
        if (ws.State == WebSocketState.CloseReceived)
        {
            await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, null, ct);
        }
        logger.LogInformation("WebSocket closed {ConnectionId}", id);
    }
    // 클라가 close 프레임 없이 끊거나 보낼 큐가 넘쳐 서버가 끊으면 여기로 옴. 요청 하나의 끝일 뿐이라 로그만 남김
    catch (Exception ex) when (ex is WebSocketException or OperationCanceledException)
    {
        logger.LogInformation("WebSocket dropped {ConnectionId}", id);
    }
    finally
    {
        // 정상 종료든 끊김이든 방에서 빠짐. 마지막 사람이면 방도 지워짐
        registry.LeaveRoom(roomId, playerId, ServerClock.Now);
        // 보내는 작업이 소켓을 놓은 뒤에 소켓이 정리되게 기다림. 끊긴 소켓이면 SendAll이 던지지 않고 바로 끝남
        queue.Complete();
        await sending;
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
