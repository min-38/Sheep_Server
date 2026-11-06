using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using SheepServer.Arena;
using SheepServer.Rounds;

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
    public async Task Ready_OtherPlayerSeesReadyThenUnready()
    {
        using var host = await Connect();
        using var guest = await Connect();
        var created = await Request(host, """{"type":"createRoom"}""");
        string hostId = created.GetProperty("playerId").GetString()!;
        string code = created.GetProperty("code").GetString()!;
        var joined = await Request(guest, $$"""{"type":"joinRoom","code":"{{code}}"}""");
        string guestId = joined.GetProperty("playerId").GetString()!;

        await Send(guest, """{"type":"ready","ready":true}""");
        var ready = await ReceiveWhere(host, m => IsState(m) && ReadyOf(m, guestId) == true);
        Assert.False(ReadyOf(ready, hostId)); // 방장은 준비하지 않았음

        await Send(guest, """{"type":"ready","ready":false}""");
        await ReceiveWhere(host, m => IsState(m) && ReadyOf(m, guestId) == false);
    }

    [Fact]
    public async Task Ready_NotABoolean_IgnoredAndConnectionKeepsWorking()
    {
        using var host = await Connect();
        using var guest = await Connect();
        var created = await Request(host, """{"type":"createRoom"}""");
        string code = created.GetProperty("code").GetString()!;
        var joined = await Request(guest, $$"""{"type":"joinRoom","code":"{{code}}"}""");
        string guestId = joined.GetProperty("playerId").GetString()!;

        // true·false가 아니면 버림. 한 연결의 메시지는 차례대로 처리되므로, 뒤에 보낸 각도가 보이면 앞의 것도 처리된 것임
        await Send(guest, """{"type":"ready","ready":"true"}""");
        await Send(guest, """{"type":"ready","ready":1}""");
        await Send(guest, """{"type":"ready"}""");
        await Send(guest, """{"type":"turn","angle":2.5}""");
        var state = await ReceiveWhere(host, m => IsState(m) && AngleOf(m, guestId) == 2.5);
        Assert.False(ReadyOf(state, guestId));

        // 위의 것 때문에 연결이 끊기면 이 준비가 오지 않음
        await Send(guest, """{"type":"ready","ready":true}""");
        await ReceiveWhere(host, m => IsState(m) && ReadyOf(m, guestId) == true);
    }

    [Fact]
    public async Task Ready_BeforeJoiningRoom_Ignored()
    {
        using var ws = await Connect();

        // 방 밖에서 보낸 준비는 버림. 그 뒤 방을 만들어도 준비 안 한 채로 들어감
        await Send(ws, """{"type":"ready","ready":true}""");
        var created = await Request(ws, """{"type":"createRoom"}""");
        string playerId = created.GetProperty("playerId").GetString()!;

        var state = await ReceiveWhere(ws, m => IsState(m) && ReadyOf(m, playerId) != null);
        Assert.False(ReadyOf(state, playerId));
    }

    [Fact]
    public async Task ClockSync_BeforeJoiningRoom_RepliesWithSameT0AndServerTime()
    {
        using var ws = await Connect();

        // 방에 들어가기 전에도 답함. 입장 전에 시계를 맞출 수 있게 함
        var reply = await Request(ws, """{"type":"clockSync","t0":1234.5678}""");

        Assert.Equal("serverTime", reply.GetProperty("type").GetString());
        Assert.Equal(1234.5678, reply.GetProperty("t0").GetDouble());
        Assert.True(reply.GetProperty("ts").GetDouble() >= 0);
    }

    [Fact]
    public async Task ClockSync_InRoom_RepliesBetweenStates()
    {
        using var host = await Connect();
        await Request(host, """{"type":"createRoom"}""");

        // 방 안에서는 state가 틱마다 섞여 옴. 답은 틱을 기다리지 않고 따로 옴
        await Send(host, """{"type":"clockSync","t0":42}""");
        var reply = await ReceiveWhere(host, m => m.GetProperty("type").GetString() == "serverTime");

        Assert.Equal(42, reply.GetProperty("t0").GetDouble());
    }

    [Fact]
    public async Task ClockSync_TwiceInARow_ServerTimeDoesNotGoBack()
    {
        using var ws = await Connect();

        var first = await Request(ws, """{"type":"clockSync","t0":1}""");
        var second = await Request(ws, """{"type":"clockSync","t0":2}""");

        // 되돌아가지 않는 시계 (기술 설계서 시간 맞추기)
        Assert.True(second.GetProperty("ts").GetDouble() >= first.GetProperty("ts").GetDouble());
    }

    [Fact]
    public async Task ClockSync_T0NotANumber_NoReplyAndConnectionKeepsWorking()
    {
        using var ws = await Connect();

        // 숫자가 아니거나, 빠졌거나, double에 담기지 않는 t0은 답하지 않음. 그래서 처음 오는 답은 마지막 요청의 것임
        await Send(ws, """{"type":"clockSync","t0":"1"}""");
        await Send(ws, """{"type":"clockSync"}""");
        await Send(ws, """{"type":"clockSync","t0":1e400}""");
        var reply = await Request(ws, """{"type":"clockSync","t0":3}""");

        Assert.Equal(3, reply.GetProperty("t0").GetDouble());
    }

    [Fact]
    public async Task Ready_BothPlayers_BothGetCountdownAtSameServerTime()
    {
        using var host = await Connect();
        using var guest = await Connect();

        // 방을 만들기 전의 서버 시각을 재고 일부러 기다림. 방마다 따로 잰 시계로 at을 정하면 at이 아래 범위보다 앞서게 됨
        var serverTime = await Request(host, """{"type":"clockSync","t0":0}""");
        double ts = serverTime.GetProperty("ts").GetDouble();
        await Task.Delay(300);

        var created = await Request(host, """{"type":"createRoom"}""");
        string code = created.GetProperty("code").GetString()!;
        await Request(guest, $$"""{"type":"joinRoom","code":"{{code}}"}""");
        await Send(host, """{"type":"ready","ready":true}""");
        await Send(guest, """{"type":"ready","ready":true}""");
        var hostPhase = await ReceiveWhere(host, IsPhase);
        var guestPhase = await ReceiveWhere(guest, IsPhase);

        Assert.Equal("countdown", hostPhase.GetProperty("phase").GetString());
        Assert.Equal("countdown", guestPhase.GetProperty("phase").GetString());
        double at = hostPhase.GetProperty("at").GetDouble();
        Assert.Equal(at, guestPhase.GetProperty("at").GetDouble());
        // at은 serverTime의 ts와 같은 시계라야 클라가 내 시계로 바꿀 수 있음.
        // 아래쪽은 기다린 300ms에서 타이머 오차를 조금 뺌. 위쪽 1초는 틱 간격과 테스트 지연을 넉넉히 봄
        Assert.InRange(at, ts + 250 + RoundFlow.Lead, ts + 300 + RoundFlow.Lead + 1000);
    }

    [Fact]
    public async Task JoinRoom_DuringRound_ReturnsPlaying()
    {
        using var host = await Connect();
        using var guest = await Connect();
        using var late = await Connect();
        var created = await Request(host, """{"type":"createRoom"}""");
        string code = created.GetProperty("code").GetString()!;
        await Request(guest, $$"""{"type":"joinRoom","code":"{{code}}"}""");
        await Send(host, """{"type":"ready","ready":true}""");
        await Send(guest, """{"type":"ready","ready":true}""");
        await ReceiveWhere(host, IsPhase);

        // 카운트다운이 정해진 뒤에는 들어갈 수 없음 (기술 설계서 메시지 joinRoom)
        var reply = await Request(late, $$"""{"type":"joinRoom","code":"{{code}}"}""");

        Assert.Equal("error", reply.GetProperty("type").GetString());
        Assert.Equal("playing", reply.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Fire_NotANumber_IgnoredAndConnectionKeepsWorking()
    {
        using var ws = await Connect();
        await Request(ws, """{"type":"createRoom"}""");

        // 숫자가 아닌 각도나 시각, 빠진 필드는 버림. 이것 때문에 연결이 끊기면 아래 clockSync의 답이 오지 않음
        await Send(ws, """{"type":"fire","angle":"1","at":100}""");
        await Send(ws, """{"type":"fire","angle":1,"at":"100"}""");
        await Send(ws, """{"type":"fire"}""");
        await Send(ws, """{"type":"clockSync","t0":7}""");

        // 방을 만든 뒤에 오는 map은 건너뜀
        var reply = await ReceiveWhere(ws, m => IsType(m, "serverTime"));
        Assert.Equal(7, reply.GetProperty("t0").GetDouble());
    }

    // 실제 판을 끝까지 돌려 "시작!"을 기다리므로 7~9초 걸림
    [Fact]
    public async Task Fire_AtOpponentAfterDuel_BothGetShotThenEliminated()
    {
        using var host = await Connect();
        using var guest = await Connect();
        var (hostId, guestId, duelAt) = await PlayUntilDuel(host, guest);

        // host는 먼저 들어와 0번 자리(중심의 +x 쪽), guest는 1번 자리(-x 쪽)에서 바깥을 봄. host가 π를 보고 쏘면 guest가 맞음
        await Send(host, JsonSerializer.Serialize(new { type = "fire", angle = Math.PI, at = duelAt + 10 }));

        // 쏜 사람을 포함해 방 안 모두가 shot을 먼저, eliminated를 그다음에 받음 (기술 설계서 메시지 shot, eliminated)
        foreach (var ws in new[] { host, guest })
        {
            var shot = await ReceiveWhere(ws, m => IsType(m, "shot"));
            Assert.Equal(hostId, shot.GetProperty("playerId").GetString());
            Assert.Equal(Math.PI, shot.GetProperty("angle").GetDouble(), 1e-9);
            var eliminated = await ReceiveWhere(ws, m => IsType(m, "eliminated"));
            Assert.Equal(guestId, eliminated.GetProperty("playerId").GetString());
        }
    }

    // 실제 판을 끝까지 돌리고 결과 시간까지 기다리므로 10~12초 걸림
    [Fact]
    public async Task Fire_AtLastOpponent_BothGetResultThenNewRoundCanStart()
    {
        using var host = await Connect();
        using var guest = await Connect();
        var (hostId, _, duelAt) = await PlayUntilDuel(host, guest);
        await Send(host, JsonSerializer.Serialize(new { type = "fire", angle = Math.PI, at = duelAt + 10 }));

        // 둘 다 eliminated를 받고, W + D가 지난 틱에 result를, 그 바로 뒤에 준비 중으로 돌아가는 phase를 받음 (기술 설계서 메시지 result)
        double lobbyAt = 0;
        foreach (var ws in new[] { host, guest })
        {
            await ReceiveWhere(ws, m => IsType(m, "eliminated"));
            var result = await Receive(ws);
            Assert.Equal("result", result.GetProperty("type").GetString());
            Assert.Equal("win", result.GetProperty("outcome").GetString());
            Assert.Equal(new[] { hostId }, result.GetProperty("playerIds").EnumerateArray().Select(id => id.GetString()!));
            Assert.Equal(0, result.GetProperty("rounds").GetInt32());
            var lobby = await Receive(ws);
            Assert.True(IsPhase(lobby));
            Assert.Equal("lobby", lobby.GetProperty("phase").GetString());
            lobbyAt = lobby.GetProperty("at").GetDouble();
        }

        // 준비 중으로 돌아간 틱이 지나면 다시 모두 준비해 새 판을 시작할 수 있음
        var serverTime = await Request(host, """{"type":"clockSync","t0":0}""");
        double ts = serverTime.GetProperty("ts").GetDouble();
        await Task.Delay(TimeSpan.FromMilliseconds(Math.Max(0, lobbyAt - ts) + 100));
        await Send(host, """{"type":"ready","ready":true}""");
        await Send(guest, """{"type":"ready","ready":true}""");
        var countdown = await ReceiveWhere(host, IsPhase);
        Assert.Equal("countdown", countdown.GetProperty("phase").GetString());
    }

    [Fact]
    public async Task Ws_NonWebSocketRequest_Returns400()
    {
        var res = await factory.CreateClient().GetAsync("/ws");
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    Task<WebSocket> Connect() =>
        factory.Server.CreateWebSocketClient().ConnectAsync(new Uri(factory.Server.BaseAddress, "ws"), CancellationToken.None);

    // host가 방을 만들고 guest가 들어가 둘 다 준비한 뒤, 결투 시각이 지날 때까지 기다림. 둘의 id와 결투 시각을 돌려줌.
    // 결투 알림은 준비하고 7~9초 뒤에 옴 (카운트다운 3초, 걷기 3초, 무작위 대기 1~3초)
    static async Task<(string HostId, string GuestId, double DuelAt)> PlayUntilDuel(WebSocket host, WebSocket guest)
    {
        var created = await Request(host, """{"type":"createRoom"}""");
        string hostId = created.GetProperty("playerId").GetString()!;
        string code = created.GetProperty("code").GetString()!;
        var joined = await Request(guest, $$"""{"type":"joinRoom","code":"{{code}}"}""");
        string guestId = joined.GetProperty("playerId").GetString()!;
        await Send(host, """{"type":"ready","ready":true}""");
        await Send(guest, """{"type":"ready","ready":true}""");

        var duel = await ReceiveWhere(host, m => IsPhase(m) && m.GetProperty("phase").GetString() == "duel", TimeSpan.FromSeconds(15));
        double duelAt = duel.GetProperty("at").GetDouble();
        var serverTime = await Request(host, """{"type":"clockSync","t0":0}""");
        double ts = serverTime.GetProperty("ts").GetDouble();
        await Task.Delay(TimeSpan.FromMilliseconds(Math.Max(0, duelAt - ts) + 20));
        return (hostId, guestId, duelAt);
    }

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
    static async Task<JsonElement> ReceiveWhere(WebSocket ws, Func<JsonElement, bool> match, TimeSpan? wait = null)
    {
        // 서버가 안 보내면 영원히 기다리지 않고 5초(판을 기다리는 테스트는 wait) 뒤 실패함. state가 계속 와도 늘어나지 않음
        using var timeout = new CancellationTokenSource(wait ?? TimeSpan.FromSeconds(5));
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

    static bool IsPhase(JsonElement m) => m.GetProperty("type").GetString() == "phase";

    static bool IsType(JsonElement m, string type) => m.GetProperty("type").GetString() == type;

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

    // state에서 id가 같은 플레이어의 준비 여부. 없으면 null
    static bool? ReadyOf(JsonElement state, string id)
    {
        foreach (var player in state.GetProperty("players").EnumerateArray())
        {
            if (player.GetProperty("id").GetString() == id)
            {
                return player.GetProperty("ready").GetBoolean();
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
