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
        await ws.SendAsync(Encoding.UTF8.GetBytes(json).AsMemory(), WebSocketMessageType.Text, true, CancellationToken.None);
        return await Receive(ws);
    }

    // 다음 메시지 하나를 끝까지 받아 JSON으로 돌려줌. 맵은 버퍼보다 클 수 있어 끝날 때까지 이어 받음
    static async Task<JsonElement> Receive(WebSocket ws)
    {
        // 서버가 안 보내면 영원히 기다리지 않고 5초 뒤 실패함
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var buffer = new byte[1024];
        using var message = new MemoryStream();
        while (true)
        {
            var result = await ws.ReceiveAsync(buffer.AsMemory(), timeout.Token);
            message.Write(buffer, 0, result.Count);
            if (result.EndOfMessage)
            {
                break;
            }
        }
        using var doc = JsonDocument.Parse(message.ToArray());
        return doc.RootElement.Clone();
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
