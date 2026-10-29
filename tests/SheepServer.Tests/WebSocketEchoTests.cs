using System.Net;
using System.Net.WebSockets;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;

namespace SheepServer.Tests;

public class WebSocketEchoTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Echo_ReturnsSentText()
    {
        using var ws = await Connect();
        Assert.Equal("양", await Echo(ws, "양"));
    }

    [Fact]
    public async Task Echo_WorksAfterPreviousConnectionClosed()
    {
        using (var first = await Connect())
            await first.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);

        using var second = await Connect();
        Assert.Equal("늑대", await Echo(second, "늑대"));
    }

    [Fact]
    public async Task Ws_NonWebSocketRequest_Returns400()
    {
        var res = await factory.CreateClient().GetAsync("/ws");
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    Task<WebSocket> Connect() =>
        factory.Server.CreateWebSocketClient().ConnectAsync(new Uri(factory.Server.BaseAddress, "ws"), CancellationToken.None);

    static async Task<string> Echo(WebSocket ws, string text)
    {
        await ws.SendAsync(Encoding.UTF8.GetBytes(text).AsMemory(), WebSocketMessageType.Text, true, CancellationToken.None);
        var buffer = new byte[1024];
        var result = await ws.ReceiveAsync(buffer.AsMemory(), CancellationToken.None);
        return Encoding.UTF8.GetString(buffer, 0, result.Count);
    }
}
