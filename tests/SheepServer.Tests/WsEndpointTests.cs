using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using SheepServer.Arena;

namespace SheepServer.Tests;

public class WsEndpointTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task CreateRoom_ThenOtherPlayerJoinsWithCode()
    {
        using var host = await Connect();
        using var guest = await Connect();

        var created = await Request(host, """{"type":"createRoom"}""");
        Assert.Equal("roomCreated", created.GetProperty("type").GetString());
        string code = created.GetProperty("code").GetString()!;

        var joined = await Request(guest, $$"""{"type":"joinRoom","code":"{{code}}"}""");
        Assert.Equal("joined", joined.GetProperty("type").GetString());
        Assert.Equal(code, joined.GetProperty("code").GetString());
    }

    [Fact]
    public async Task JoinRoom_LowercaseCode_Joins()
    {
        using var host = await Connect();
        using var guest = await Connect();
        var created = await Request(host, """{"type":"createRoom"}""");
        string code = created.GetProperty("code").GetString()!;

        var joined = await Request(guest, $$"""{"type":"joinRoom","code":"{{code.ToLowerInvariant()}}"}""");

        Assert.Equal("joined", joined.GetProperty("type").GetString());
    }

    [Fact]
    public async Task JoinRoom_UnknownCode_ReturnsNotFound()
    {
        using var ws = await Connect();

        // '-'는 방 코드에 안 쓰는 글자라 이 코드의 방은 절대 없음
        var reply = await Request(ws, """{"type":"joinRoom","code":"NO-ROOM"}""");

        Assert.Equal("error", reply.GetProperty("type").GetString());
        Assert.Equal("notFound", reply.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task CreateRoom_SendsMapAfterRoomCreated()
    {
        using var host = await Connect();

        var created = await Request(host, """{"type":"createRoom"}""");
        var map = await Receive(host);

        Assert.Equal("roomCreated", created.GetProperty("type").GetString());
        AssertFixedMap(map);
    }

    [Fact]
    public async Task JoinRoom_SendsMapAfterJoined()
    {
        using var host = await Connect();
        using var guest = await Connect();
        var created = await Request(host, """{"type":"createRoom"}""");
        string code = created.GetProperty("code").GetString()!;

        var joined = await Request(guest, $$"""{"type":"joinRoom","code":"{{code}}"}""");
        var map = await Receive(guest);

        Assert.Equal("joined", joined.GetProperty("type").GetString());
        AssertFixedMap(map);
    }

    [Fact]
    public async Task JoinRoom_SameRoomAgain_SendsMapAgain()
    {
        using var host = await Connect();
        var created = await Request(host, """{"type":"createRoom"}""");
        string code = created.GetProperty("code").GetString()!;
        await Receive(host);

        // 이미 들어가 있는 방의 코드로 다시 입장함
        var joined = await Request(host, $$"""{"type":"joinRoom","code":"{{code}}"}""");
        var map = await Receive(host);

        Assert.Equal("joined", joined.GetProperty("type").GetString());
        AssertFixedMap(map);
    }

    [Fact]
    public async Task CreateRoomAndJoin_ReplyWithEachPlayerId()
    {
        using var host = await Connect();
        using var guest = await Connect();

        var created = await Request(host, """{"type":"createRoom"}""");
        string code = created.GetProperty("code").GetString()!;
        var joined = await Request(guest, $$"""{"type":"joinRoom","code":"{{code}}"}""");

        // 클라는 이 id로 state에서 자기를 찾음 (기술 설계서 메시지 roomCreated, joined)
        string hostId = created.GetProperty("playerId").GetString()!;
        string guestId = joined.GetProperty("playerId").GetString()!;
        Assert.NotEmpty(hostId);
        Assert.NotEmpty(guestId);
        Assert.NotEqual(hostId, guestId);
    }

    [Fact]
    public async Task Turn_OtherPlayerSeesAngleInState()
    {
        using var host = await Connect();
        using var guest = await Connect();
        var created = await Request(host, """{"type":"createRoom"}""");
        string hostId = created.GetProperty("playerId").GetString()!;
        string code = created.GetProperty("code").GetString()!;
        var joined = await Request(guest, $$"""{"type":"joinRoom","code":"{{code}}"}""");
        string guestId = joined.GetProperty("playerId").GetString()!;

        await Send(guest, """{"type":"turn","angle":1.25}""");

        // 틱마다 오는 state 중 손님의 각도가 바뀐 것이 오면 됨. 둘 다 방에 있어야 함
        var state = await ReceiveWhere(host, m => IsState(m) && AngleOf(m, guestId) == 1.25);
        Assert.True(state.GetProperty("tick").GetInt32() > 0);
        Assert.Equal(2, state.GetProperty("players").GetArrayLength());
        Assert.NotNull(AngleOf(state, hostId));
    }

    [Fact]
    public async Task Turn_NotANumber_IgnoredAndConnectionKeepsWorking()
    {
        using var host = await Connect();
        using var guest = await Connect();
        var created = await Request(host, """{"type":"createRoom"}""");
        string code = created.GetProperty("code").GetString()!;
        var joined = await Request(guest, $$"""{"type":"joinRoom","code":"{{code}}"}""");
        string guestId = joined.GetProperty("playerId").GetString()!;

        // 숫자가 아닌 각도와 빠진 각도는 버림. 이것 때문에 연결이 끊기면 다음 turn이 오지 않음
        await Send(guest, """{"type":"turn","angle":"1"}""");
        await Send(guest, """{"type":"turn"}""");
        await Send(guest, """{"type":"turn","angle":2.5}""");

        await ReceiveWhere(host, m => IsState(m) && AngleOf(m, guestId) == 2.5);
    }

    [Fact]
    public async Task Ws_NonWebSocketRequest_Returns400()
    {
        var res = await factory.CreateClient().GetAsync("/ws");
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    Task<WebSocket> Connect() =>
        factory.Server.CreateWebSocketClient().ConnectAsync(new Uri(factory.Server.BaseAddress, "ws"), CancellationToken.None);

    // 메시지 하나를 보내고 답 하나를 받아 JSON으로 돌려줌
    static async Task<JsonElement> Request(WebSocket ws, string json)
    {
        await Send(ws, json);
        return await Receive(ws);
    }

    static Task Send(WebSocket ws, string json) =>
        ws.SendAsync(Encoding.UTF8.GetBytes(json).AsMemory(), WebSocketMessageType.Text, true, CancellationToken.None).AsTask();

    // state가 아닌 다음 메시지. state는 틱마다 와서 다른 답 사이에 섞일 수 있음 (기술 설계서 메시지 state)
    static Task<JsonElement> Receive(WebSocket ws) => ReceiveWhere(ws, m => !IsState(m));

    // 조건에 맞는 메시지가 올 때까지 받고 나머지는 버림
    static async Task<JsonElement> ReceiveWhere(WebSocket ws, Func<JsonElement, bool> match)
    {
        // 서버가 안 보내면 영원히 기다리지 않고 5초 뒤 실패함. state가 계속 와도 5초는 늘어나지 않음
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (true)
        {
            var message = await ReceiveOne(ws, timeout.Token);
            if (match(message))
            {
                return message;
            }
        }
    }

    // 다음 메시지 하나를 끝까지 받아 JSON으로 돌려줌. 맵은 버퍼보다 클 수 있어 끝날 때까지 이어 받음
    static async Task<JsonElement> ReceiveOne(WebSocket ws, CancellationToken ct)
    {
        var buffer = new byte[1024];
        using var message = new MemoryStream();
        while (true)
        {
            var result = await ws.ReceiveAsync(buffer.AsMemory(), ct);
            message.Write(buffer, 0, result.Count);
            if (result.EndOfMessage)
            {
                break;
            }
        }
        using var doc = JsonDocument.Parse(message.ToArray());
        return doc.RootElement.Clone();
    }

    static bool IsState(JsonElement m) => m.GetProperty("type").GetString() == "state";

    // state에서 id가 같은 플레이어의 각도. 없으면 null
    static double? AngleOf(JsonElement state, string id)
    {
        foreach (var player in state.GetProperty("players").EnumerateArray())
        {
            if (player.GetProperty("id").GetString() == id)
            {
                return player.GetProperty("angle").GetDouble();
            }
        }
        return null;
    }

    // 받은 맵 메시지가 서버의 고정 결투장과 같은지 봄 (기술 설계서 메시지 map)
    static void AssertFixedMap(JsonElement map)
    {
        Assert.Equal("map", map.GetProperty("type").GetString());
        Assert.Equal(ArenaMap.Fixed.Width, map.GetProperty("width").GetInt32());
        Assert.Equal(ArenaMap.Fixed.Height, map.GetProperty("height").GetInt32());
        Assert.Equal(ArenaMap.Fixed.Cells, map.GetProperty("cells").Deserialize<int[][]>());
    }
}
