using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

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
        var buffer = new byte[1024];
        var result = await ws.ReceiveAsync(buffer.AsMemory(), CancellationToken.None);
        using var doc = JsonDocument.Parse(buffer.AsMemory(0, result.Count));
        return doc.RootElement.Clone();
    }
}
