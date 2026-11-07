using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using SheepServer.Arena;
using SheepServer.Rounds;
using SheepServer.Shooting;

namespace SheepServer.Tests;

public class WsEndpointTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    // 다 걸은 둘은 중심을 사이에 두고 2(R + 걸은 거리) 떨어짐. 그 거리에서 머리 가운데를 겨누는 위아래 각도.
    // 몸은 반샷이라 한 발에 쓰러뜨리려면 머리를 쏨 (컨셉 기획서 규칙 머리는 즉사, 몸은 반샷)
    private static readonly double HeadAcross =
        Math.Atan(((HitScan.BodyTop + HitScan.HeadTop) / 2 - HitScan.EyeHeight) / (2 * (StartPlacement.Radius + RoundFlow.WalkDistance) - HitScan.HeadRadius));

    [Fact]
    public async Task CreateRoom_ThenOtherPlayerJoinsWithCode()
    {
        using var host = await Connect();
        using var guest = await Connect();

        var created = await Request(host, """{"type":"createRoom","nickname":"보안관"}""");
        Assert.Equal("roomCreated", created.GetProperty("type").GetString());
        string code = created.GetProperty("code").GetString()!;

        var joined = await Request(guest, $$"""{"type":"joinRoom","code":"{{code}}","nickname":"떠돌이"}""");
        Assert.Equal("joined", joined.GetProperty("type").GetString());
        Assert.Equal(code, joined.GetProperty("code").GetString());
    }

    [Fact]
    public async Task CreateRoom_Nickname_InState()
    {
        using var host = await Connect();

        var created = await Request(host, """{"type":"createRoom","nickname":" 보안관 "}""");
        string hostId = created.GetProperty("playerId").GetString()!;
        var state = await ReceiveWhere(host, IsState);

        // 앞뒤 공백을 지운 이름이 state에 실림 (기술 설계서 메시지 state)
        Assert.Equal("보안관", NicknameOf(state, hostId));
    }

    [Theory]
    [InlineData("\"가\"")]
    [InlineData("\"보안관!\"")]
    [InlineData("\"보 안\"")]
    [InlineData("7")]
    [InlineData("null")]
    public async Task CreateRoom_BadNickname_Error(string nickname)
    {
        using var host = await Connect();

        var reply = await Request(host, $$"""{"type":"createRoom","nickname":{{nickname}}}""");

        // 서버도 클라와 같은 규칙으로 다시 검사함 (시스템 기획서 방 화면 닉네임)
        Assert.Equal("error", reply.GetProperty("type").GetString());
        Assert.Equal("nickname", reply.GetProperty("reason").GetString());
    }

    [Theory]
    [InlineData("""{"type":"createRoom"}""")]
    [InlineData("""{"type":"quickJoin"}""")]
    public async Task EnterWithoutNickname_Error(string message)
    {
        using var ws = await Connect();

        var reply = await Request(ws, message);

        // 첫 화면이 늘 닉네임을 보내므로 임시 이름을 주지 않고 틀린 닉네임과 같이 거절함 (SHEEP-73)
        Assert.Equal("error", reply.GetProperty("type").GetString());
        Assert.Equal("nickname", reply.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task JoinRoom_NoNickname_ErrorAndStaysOut()
    {
        using var host = await Connect();
        using var guest = await Connect();
        var created = await Request(host, """{"type":"createRoom","nickname":"보안관"}""");
        string code = created.GetProperty("code").GetString()!;

        var reply = await Request(guest, $$"""{"type":"joinRoom","code":"{{code}}"}""");
        await Task.Delay(100); // 그사이 틱이 지나 새 state가 옴
        var state = await ReceiveWhere(host, IsState);

        Assert.Equal("error", reply.GetProperty("type").GetString());
        Assert.Equal("nickname", reply.GetProperty("reason").GetString());
        Assert.Equal(1, state.GetProperty("players").GetArrayLength());
    }

    [Fact]
    public async Task JoinRoom_BadNickname_ErrorAndStaysOut()
    {
        using var host = await Connect();
        using var guest = await Connect();
        var created = await Request(host, """{"type":"createRoom","nickname":"보안관"}""");
        string code = created.GetProperty("code").GetString()!;

        var reply = await Request(guest, $$"""{"type":"joinRoom","code":"{{code}}","nickname":"보 안"}""");
        await Task.Delay(100); // 그사이 틱이 지나 새 state가 옴
        var state = await ReceiveWhere(host, IsState);

        Assert.Equal("error", reply.GetProperty("type").GetString());
        Assert.Equal("nickname", reply.GetProperty("reason").GetString());
        Assert.Equal(1, state.GetProperty("players").GetArrayLength());
    }

    [Fact]
    public async Task CreateRoom_NameAndMax_InReplyAndList()
    {
        using var host = await Connect();
        using var browser = await Connect();

        var created = await Request(host, """{"type":"createRoom","nickname":"보안관","name":" 먼지 낀 술집 ","maxPlayers":4,"private":false}""");
        string code = created.GetProperty("code").GetString()!;
        var list = await Request(browser, """{"type":"listRooms"}""");

        // 앞뒤 공백을 지운 이름과 비공개 여부가 답에 실리고, 목록에는 이름과 인원 제한이 실림 (기술 설계서 메시지 roomCreated, rooms)
        Assert.Equal("먼지 낀 술집", created.GetProperty("name").GetString());
        Assert.False(created.GetProperty("private").GetBoolean());
        var room = list.GetProperty("rooms").EnumerateArray().Single(r => r.GetProperty("code").GetString() == code);
        Assert.Equal("먼지 낀 술집", room.GetProperty("name").GetString());
        Assert.Equal(4, room.GetProperty("max").GetInt32());
    }

    [Theory]
    [InlineData("\"a\"")]
    [InlineData("\"가나다라마바사아자차카타파하가나다\"")]
    [InlineData("\"a\\nb\"")]
    [InlineData("7")]
    [InlineData("null")]
    public async Task CreateRoom_BadName_Error(string name)
    {
        using var host = await Connect();

        var reply = await Request(host, $$"""{"type":"createRoom","nickname":"보안관","name":{{name}}}""");

        Assert.Equal("error", reply.GetProperty("type").GetString());
        Assert.Equal("roomName", reply.GetProperty("reason").GetString());
    }

    [Theory]
    [InlineData("1")]
    [InlineData("9")]
    [InlineData("2.5")]
    [InlineData("\"8\"")]
    [InlineData("null")]
    public async Task CreateRoom_BadMaxPlayers_Error(string maxPlayers)
    {
        using var host = await Connect();

        var reply = await Request(host, $$"""{"type":"createRoom","nickname":"보안관","maxPlayers":{{maxPlayers}}}""");

        // 인원 제한은 2~8명 정수 (시스템 기획서 방 화면 방 만들기)
        Assert.Equal("error", reply.GetProperty("type").GetString());
        Assert.Equal("maxPlayers", reply.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task JoinRoom_Joined_HasNameAndPrivate()
    {
        using var host = await Connect();
        using var guest = await Connect();
        var created = await Request(host, """{"type":"createRoom","nickname":"보안관","name":"비밀 금광","private":true}""");
        string code = created.GetProperty("code").GetString()!;

        var joined = await Request(guest, $$"""{"type":"joinRoom","code":"{{code}}","nickname":"떠돌이"}""");

        // 들어온 사람도 칠판에 방 이름과 "비공개"를 적을 수 있게 함 (시스템 기획서 방 화면 방 만들기)
        Assert.Equal("비밀 금광", joined.GetProperty("name").GetString());
        Assert.True(joined.GetProperty("private").GetBoolean());
    }

    [Fact]
    public async Task ListRooms_BeforeJoining_Rooms()
    {
        using var host = await Connect();
        using var browser = await Connect();
        var created = await Request(host, """{"type":"createRoom","nickname":"보안관"}""");
        string code = created.GetProperty("code").GetString()!;

        // 방에 들어가기 전에도 보낼 수 있음 (기술 설계서 메시지 listRooms)
        var reply = await Request(browser, """{"type":"listRooms"}""");

        Assert.Equal("rooms", reply.GetProperty("type").GetString());
        var room = reply.GetProperty("rooms").EnumerateArray().Single(r => r.GetProperty("code").GetString() == code);
        var player = Assert.Single(room.GetProperty("players").EnumerateArray());
        Assert.Equal("보안관", player.GetProperty("nickname").GetString());
        Assert.Equal(0, player.GetProperty("color").GetInt32());
        Assert.Equal(8, room.GetProperty("max").GetInt32());
        Assert.False(room.GetProperty("playing").GetBoolean());
    }

    [Fact]
    public async Task QuickJoin_ReplyThenMap()
    {
        using var ws = await Connect();

        var reply = await Request(ws, """{"type":"quickJoin","nickname":"보안관"}""");
        var map = await Receive(ws);

        // 들어갈 방이 있으면 joined, 없어 만들면 roomCreated (기술 설계서 메시지 quickJoin)
        Assert.Contains(reply.GetProperty("type").GetString(), new[] { "joined", "roomCreated" });
        Assert.False(string.IsNullOrEmpty(reply.GetProperty("code").GetString()));
        AssertFixedMap(map);
    }

    [Fact]
    public async Task QuickJoin_InRoom_LeavesFirst()
    {
        using var ws = await Connect();
        var created = await Request(ws, """{"type":"createRoom","nickname":"보안관"}""");
        string playerId = created.GetProperty("playerId").GetString()!;
        await Receive(ws); // map

        var reply = await Request(ws, """{"type":"quickJoin","nickname":"보안관"}""");
        string code = reply.GetProperty("code").GetString()!;
        await Receive(ws); // map
        var state = await ReceiveWhere(ws, IsState);

        // 혼자 있던 방을 먼저 나가 그 방은 지워짐. 그러지 않으면 내 방을 골라 같은 사람이 둘 들어감.
        // 새로 만든 방이 지워진 방의 코드를 다시 받을 수 있어(아주 드묾) 옛 방이 지워졌는지는 코드가 다를 때만 봄
        int mine = state.GetProperty("players").EnumerateArray().Count(p => p.GetProperty("id").GetString() == playerId);
        Assert.Equal(1, mine);
        string oldCode = created.GetProperty("code").GetString()!;
        if (code != oldCode)
        {
            using var other = await Connect();
            var old = await Request(other, $$"""{"type":"joinRoom","code":"{{oldCode}}","nickname":"떠돌이"}""");
            Assert.Equal("notFound", old.GetProperty("reason").GetString());
        }
    }

    [Fact]
    public async Task CreateRoom_Private_QuickJoinGoesElsewhere()
    {
        using var host = await Connect();
        using var guest = await Connect();
        var created = await Request(host, """{"type":"createRoom","nickname":"보안관","private":true}""");

        var reply = await Request(guest, """{"type":"quickJoin","nickname":"떠돌이"}""");

        // 비공개 방은 랜덤 입장에 나오지 않고 코드로만 들어감 (시스템 기획서 방 화면 방 만들기)
        Assert.NotEqual(created.GetProperty("code").GetString(), reply.GetProperty("code").GetString());
    }

    [Fact]
    public async Task CreateRoom_Private_JoinByCode()
    {
        using var host = await Connect();
        using var guest = await Connect();
        var created = await Request(host, """{"type":"createRoom","nickname":"보안관","private":true}""");
        string code = created.GetProperty("code").GetString()!;

        var joined = await Request(guest, $$"""{"type":"joinRoom","code":"{{code}}","nickname":"떠돌이"}""");

        Assert.Equal("joined", joined.GetProperty("type").GetString());
    }

    [Fact]
    public async Task LeaveRoom_Left_RoomGoneWhenEmpty()
    {
        using var host = await Connect();
        using var other = await Connect();
        var created = await Request(host, """{"type":"createRoom","nickname":"보안관"}""");
        string code = created.GetProperty("code").GetString()!;
        await Receive(host); // map

        var left = await Request(host, """{"type":"leaveRoom"}""");
        var join = await Request(other, $$"""{"type":"joinRoom","code":"{{code}}","nickname":"떠돌이"}""");

        // 마지막 사람이 나가 방이 지워짐 (기술 설계서 메시지 leaveRoom)
        Assert.Equal("left", left.GetProperty("type").GetString());
        Assert.Equal("notFound", join.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task LeaveRoom_NotInRoom_StillLeft()
    {
        using var ws = await Connect();

        var left = await Request(ws, """{"type":"leaveRoom"}""");

        Assert.Equal("left", left.GetProperty("type").GetString());
    }

    [Fact]
    public async Task QuickJoin_BadNickname_Error()
    {
        using var ws = await Connect();

        var reply = await Request(ws, """{"type":"quickJoin","nickname":"보안관!"}""");

        Assert.Equal("error", reply.GetProperty("type").GetString());
        Assert.Equal("nickname", reply.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task JoinRoom_LowercaseCode_Joins()
    {
        using var host = await Connect();
        using var guest = await Connect();
        var created = await Request(host, """{"type":"createRoom","nickname":"보안관"}""");
        string code = created.GetProperty("code").GetString()!;

        var joined = await Request(guest, $$"""{"type":"joinRoom","code":"{{code.ToLowerInvariant()}}","nickname":"떠돌이"}""");

        Assert.Equal("joined", joined.GetProperty("type").GetString());
    }

    [Fact]
    public async Task JoinRoom_UnknownCode_ReturnsNotFound()
    {
        using var ws = await Connect();

        // '-'는 방 코드에 안 쓰는 글자라 이 코드의 방은 절대 없음
        var reply = await Request(ws, """{"type":"joinRoom","code":"NO-ROOM","nickname":"떠돌이"}""");

        Assert.Equal("error", reply.GetProperty("type").GetString());
        Assert.Equal("notFound", reply.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task CreateRoom_SendsMapAfterRoomCreated()
    {
        using var host = await Connect();

        var created = await Request(host, """{"type":"createRoom","nickname":"보안관"}""");
        var map = await Receive(host);

        Assert.Equal("roomCreated", created.GetProperty("type").GetString());
        AssertFixedMap(map);
    }

    [Fact]
    public async Task JoinRoom_SendsMapAfterJoined()
    {
        using var host = await Connect();
        using var guest = await Connect();
        var created = await Request(host, """{"type":"createRoom","nickname":"보안관"}""");
        string code = created.GetProperty("code").GetString()!;

        var joined = await Request(guest, $$"""{"type":"joinRoom","code":"{{code}}","nickname":"떠돌이"}""");
        var map = await Receive(guest);

        Assert.Equal("joined", joined.GetProperty("type").GetString());
        AssertFixedMap(map);
    }

    [Fact]
    public async Task JoinRoom_SameRoomAgain_SendsMapAgain()
    {
        using var host = await Connect();
        var created = await Request(host, """{"type":"createRoom","nickname":"보안관"}""");
        string code = created.GetProperty("code").GetString()!;
        await Receive(host);

        // 이미 들어가 있는 방의 코드로 다시 입장함
        var joined = await Request(host, $$"""{"type":"joinRoom","code":"{{code}}","nickname":"떠돌이"}""");
        var map = await Receive(host);

        Assert.Equal("joined", joined.GetProperty("type").GetString());
        AssertFixedMap(map);
    }

    [Fact]
    public async Task CreateRoomAndJoin_ReplyWithEachPlayerId()
    {
        using var host = await Connect();
        using var guest = await Connect();

        var created = await Request(host, """{"type":"createRoom","nickname":"보안관"}""");
        string code = created.GetProperty("code").GetString()!;
        var joined = await Request(guest, $$"""{"type":"joinRoom","code":"{{code}}","nickname":"떠돌이"}""");

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
        var created = await Request(host, """{"type":"createRoom","nickname":"보안관"}""");
        string hostId = created.GetProperty("playerId").GetString()!;
        string code = created.GetProperty("code").GetString()!;
        var joined = await Request(guest, $$"""{"type":"joinRoom","code":"{{code}}","nickname":"떠돌이"}""");
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
        var created = await Request(host, """{"type":"createRoom","nickname":"보안관"}""");
        string code = created.GetProperty("code").GetString()!;
        var joined = await Request(guest, $$"""{"type":"joinRoom","code":"{{code}}","nickname":"떠돌이"}""");
        string guestId = joined.GetProperty("playerId").GetString()!;

        // 숫자가 아닌 각도와 빠진 각도는 버림. 이것 때문에 연결이 끊기면 다음 turn이 오지 않음
        await Send(guest, """{"type":"turn","angle":"1"}""");
        await Send(guest, """{"type":"turn"}""");
        await Send(guest, """{"type":"turn","angle":2.5}""");

        await ReceiveWhere(host, m => IsState(m) && AngleOf(m, guestId) == 2.5);
    }

    [Fact]
    public async Task Turn_Pitch_OtherPlayerSeesPitchInState()
    {
        using var host = await Connect();
        using var guest = await Connect();
        var created = await Request(host, """{"type":"createRoom","nickname":"보안관"}""");
        string code = created.GetProperty("code").GetString()!;
        var joined = await Request(guest, $$"""{"type":"joinRoom","code":"{{code}}","nickname":"떠돌이"}""");
        string guestId = joined.GetProperty("playerId").GetString()!;

        await Send(guest, """{"type":"turn","angle":1.25,"pitch":0.3}""");
        await ReceiveWhere(host, m => IsState(m) && PitchOf(m, guestId) == 0.3);

        // 숫자가 아닌 pitch는 그것만 버리고 각도는 받음. 위아래 시선은 그대로 (기술 설계서 메시지 turn)
        await Send(guest, """{"type":"turn","angle":2.5,"pitch":"0.1"}""");
        var state = await ReceiveWhere(host, m => IsState(m) && AngleOf(m, guestId) == 2.5);
        Assert.Equal(0.3, PitchOf(state, guestId));
    }

    [Fact]
    public async Task Ready_OtherPlayerSeesReadyThenUnready()
    {
        using var host = await Connect();
        using var guest = await Connect();
        var created = await Request(host, """{"type":"createRoom","nickname":"보안관"}""");
        string hostId = created.GetProperty("playerId").GetString()!;
        string code = created.GetProperty("code").GetString()!;
        var joined = await Request(guest, $$"""{"type":"joinRoom","code":"{{code}}","nickname":"떠돌이"}""");
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
        var created = await Request(host, """{"type":"createRoom","nickname":"보안관"}""");
        string code = created.GetProperty("code").GetString()!;
        var joined = await Request(guest, $$"""{"type":"joinRoom","code":"{{code}}","nickname":"떠돌이"}""");
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
        var created = await Request(ws, """{"type":"createRoom","nickname":"보안관"}""");
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
        await Request(host, """{"type":"createRoom","nickname":"보안관"}""");

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

        var created = await Request(host, """{"type":"createRoom","nickname":"보안관"}""");
        string code = created.GetProperty("code").GetString()!;
        await Request(guest, $$"""{"type":"joinRoom","code":"{{code}}","nickname":"떠돌이"}""");
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
        var created = await Request(host, """{"type":"createRoom","nickname":"보안관"}""");
        string code = created.GetProperty("code").GetString()!;
        await Request(guest, $$"""{"type":"joinRoom","code":"{{code}}","nickname":"떠돌이"}""");
        await Send(host, """{"type":"ready","ready":true}""");
        await Send(guest, """{"type":"ready","ready":true}""");
        await ReceiveWhere(host, IsPhase);

        // 카운트다운이 정해진 뒤에는 들어갈 수 없음 (기술 설계서 메시지 joinRoom)
        var reply = await Request(late, $$"""{"type":"joinRoom","code":"{{code}}","nickname":"떠돌이"}""");

        Assert.Equal("error", reply.GetProperty("type").GetString());
        Assert.Equal("playing", reply.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Fire_NotANumber_IgnoredAndConnectionKeepsWorking()
    {
        using var ws = await Connect();
        await Request(ws, """{"type":"createRoom","nickname":"보안관"}""");

        // 숫자가 아닌 각도, 위아래 각도, 시각과 빠진 필드는 버림. 이것 때문에 연결이 끊기면 아래 clockSync의 답이 오지 않음
        await Send(ws, """{"type":"fire","angle":"1","pitch":0,"at":100}""");
        await Send(ws, """{"type":"fire","angle":1,"pitch":"0","at":100}""");
        await Send(ws, """{"type":"fire","angle":1,"pitch":0,"at":"100"}""");
        await Send(ws, """{"type":"fire","angle":1,"at":100}""");
        await Send(ws, """{"type":"fire"}""");
        await Send(ws, """{"type":"clockSync","t0":7}""");

        // 방을 만든 뒤에 오는 map은 건너뜀
        var reply = await ReceiveWhere(ws, m => IsType(m, "serverTime"));
        Assert.Equal(7, reply.GetProperty("t0").GetDouble());
    }

    [Fact]
    public async Task Stop_NotANumber_IgnoredAndConnectionKeepsWorking()
    {
        using var ws = await Connect();
        await Request(ws, """{"type":"createRoom","nickname":"보안관"}""");

        // 숫자가 아닌 시각과 빠진 시각은 버림. 이것 때문에 연결이 끊기면 아래 clockSync의 답이 오지 않음
        await Send(ws, """{"type":"stop","at":"100"}""");
        await Send(ws, """{"type":"stop"}""");
        await Send(ws, """{"type":"stop","at":100}""");
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

        // host는 먼저 들어와 0번 자리(중심의 +x 쪽), guest는 1번 자리(-x 쪽)에서 바깥을 봄. host가 π를 보고 머리 높이로 쏘면 guest가 쓰러짐
        await Send(host, JsonSerializer.Serialize(new { type = "fire", angle = Math.PI, pitch = HeadAcross, at = duelAt + 10 }));

        // 쏜 사람을 포함해 방 안 모두가 shot을 먼저, eliminated를 그다음에 받음 (기술 설계서 메시지 shot, eliminated).
        // 그 사이에 쏜 사람만 shotResult를 받음. 맞은 사람은 누가 어디를 맞혔는지 받지 않음
        foreach (var ws in new[] { host, guest })
        {
            var shot = await ReceiveWhere(ws, m => IsType(m, "shot"));
            Assert.Equal(hostId, shot.GetProperty("playerId").GetString());
            Assert.Equal(Math.PI, shot.GetProperty("angle").GetDouble(), 1e-9);
            var next = await Receive(ws);
            if (ws == host)
            {
                Assert.Equal("shotResult", next.GetProperty("type").GetString());
                Assert.Equal("head", next.GetProperty("zone").GetString());
                Assert.True(next.GetProperty("down").GetBoolean());
                next = await Receive(ws);
            }
            Assert.Equal("eliminated", next.GetProperty("type").GetString());
            Assert.Equal(guestId, next.GetProperty("playerId").GetString());
        }
    }

    // 실제 판을 끝까지 돌리고 결과 시간까지 기다리므로 10~12초 걸림
    [Fact]
    public async Task Fire_AtLastOpponent_BothGetResultThenNewRoundCanStart()
    {
        using var host = await Connect();
        using var guest = await Connect();
        var (hostId, _, duelAt) = await PlayUntilDuel(host, guest);
        await Send(host, JsonSerializer.Serialize(new { type = "fire", angle = Math.PI, pitch = HeadAcross, at = duelAt + 10 }));

        // 둘 다 eliminated를 받고, W + D가 지난 틱에 result를, 그 바로 뒤에 준비 중으로 돌아가는 phase를 받음 (기술 설계서 메시지 result)
        double lobbyAt = 0;
        foreach (var ws in new[] { host, guest })
        {
            await ReceiveWhere(ws, m => IsType(m, "eliminated"));

            // 쓰러진 guest는 eliminated 바로 뒤에 쓰러뜨린 사람을 받음 (기술 설계서 메시지 downedBy)
            if (ws == guest)
            {
                var downedBy = await Receive(ws);
                Assert.Equal("downedBy", downedBy.GetProperty("type").GetString());
                Assert.Equal(hostId, downedBy.GetProperty("playerId").GetString());
            }
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

    // 실제 판을 끝까지 돌려 "시작!"을 기다리므로 7~9초 걸림
    [Fact]
    public async Task Spectate_Down_SpectatingStandingNoReply()
    {
        using var host = await Connect();
        using var guest = await Connect();
        var (hostId, guestId, duelAt) = await PlayUntilDuel(host, guest);
        await Send(host, JsonSerializer.Serialize(new { type = "fire", angle = Math.PI, pitch = HeadAcross, at = duelAt + 10 }));
        await ReceiveWhere(guest, m => IsType(m, "eliminated"));

        // 쓰러진 guest는 host를 볼 수 있음. 답에 host의 지금 반샷 수와 남은 총알이 옴 (기술 설계서 메시지 spectating)
        await Send(guest, $$"""{"type":"spectate","playerId":"{{hostId}}"}""");
        var spectating = await ReceiveWhere(guest, m => IsType(m, "spectating"));
        Assert.Equal(hostId, spectating.GetProperty("playerId").GetString());
        Assert.Equal(0, spectating.GetProperty("wounds").GetInt32());
        Assert.Equal(Revolver.Capacity - 1, spectating.GetProperty("ammo").GetInt32());

        // 서 있는 host의 spectate에는 답이 없음. 뒤에 보낸 clockSync의 답이 먼저 옴
        await Send(host, $$"""{"type":"spectate","playerId":"{{guestId}}"}""");
        await Send(host, """{"type":"clockSync","t0":7}""");
        var reply = await ReceiveWhere(host, m => IsType(m, "spectating") || IsType(m, "serverTime"));
        Assert.Equal("serverTime", reply.GetProperty("type").GetString());
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
        var created = await Request(host, """{"type":"createRoom","nickname":"보안관"}""");
        string hostId = created.GetProperty("playerId").GetString()!;
        string code = created.GetProperty("code").GetString()!;
        var joined = await Request(guest, $$"""{"type":"joinRoom","code":"{{code}}","nickname":"떠돌이"}""");
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

    // state에서 id가 같은 플레이어의 위아래 시선. 없으면 null
    static double? PitchOf(JsonElement state, string id)
    {
        foreach (var player in state.GetProperty("players").EnumerateArray())
        {
            if (player.GetProperty("id").GetString() == id)
            {
                return player.GetProperty("pitch").GetDouble();
            }
        }
        return null;
    }

    // state에서 id가 같은 플레이어의 닉네임. 없으면 null
    static string? NicknameOf(JsonElement state, string id)
    {
        foreach (var player in state.GetProperty("players").EnumerateArray())
        {
            if (player.GetProperty("id").GetString() == id)
            {
                return player.GetProperty("nickname").GetString();
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
