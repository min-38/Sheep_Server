namespace SheepServer.Tests.Rooms;

using SheepServer.Rooms;

public class RoomRegistryTests
{
    private readonly RoomRegistry _registry = new();
    private readonly Guid _hostId = Guid.NewGuid();

    [Fact]
    public void AddRoom_ReturnsCode_WithHostInside()
    {
        // Arrange
        int maxPlayers = 4;

        // Act
        string roomId = _registry.AddRoom(_hostId, maxPlayers);
        var createdRoom = _registry.GetRoomById(roomId);

        // Assert
        Assert.NotNull(createdRoom);
        Assert.Equal(roomId, createdRoom.RoomId);
        Assert.Equal(maxPlayers, createdRoom.MaxPlayers);
        Assert.Equal(1, createdRoom.CurrentPlayers); // Host should be automatically added
    }

    [Fact]
    public void AddRoom_CodeCollision_RetriesNewCode()
    {
        // Arrange
        var codes = new Queue<string>(["AAAAAA", "AAAAAA", "BBBBBB"]);
        var registry = new RoomRegistry(() => codes.Dequeue());

        // Act
        string firstRoomId = registry.AddRoom(Guid.NewGuid(), 2);
        string secondRoomId = registry.AddRoom(Guid.NewGuid(), 2);

        // Assert
        Assert.Equal("AAAAAA", firstRoomId);
        Assert.Equal("BBBBBB", secondRoomId); // "AAAAAA" is taken, so a new code is drawn
    }

    [Fact]
    public void JoinRoom_ExistingCode_ReturnsJoined()
    {
        // Arrange
        int maxPlayers = 4;
        string roomId = _registry.AddRoom(_hostId, maxPlayers);
        var createdRoom = _registry.GetRoomById(roomId);

        // Act
        JoinResult joinResult = _registry.JoinRoom(roomId, Guid.NewGuid());

        // Assert
        Assert.NotNull(createdRoom);
        Assert.Equal(2, createdRoom.CurrentPlayers);
        Assert.Equal(JoinResult.Joined, joinResult);
    }

    [Fact]
    public void JoinRoom_FullRoom_ReturnsFull()
    {
        // Arrange
        int maxPlayers = 1;
        string roomId = _registry.AddRoom(_hostId, maxPlayers);
        var createdRoom = _registry.GetRoomById(roomId);

        // Act
        JoinResult joinResult = _registry.JoinRoom(roomId, Guid.NewGuid());

        // Assert
        Assert.Equal(JoinResult.Full, joinResult);
        Assert.NotNull(createdRoom);
        Assert.Equal(1, createdRoom.CurrentPlayers); // Should still be 1
    }

    [Fact]
    public void JoinRoom_UnknownCode_ReturnsNotFound()
    {
        // Arrange
        int maxPlayers = 4;
        string roomId = _registry.AddRoom(_hostId, maxPlayers);
        string unknownRoomId = "U@KNOWN";


        // Act
        var joinResult = _registry.JoinRoom(unknownRoomId, Guid.NewGuid());

        // Assert
        Assert.Equal(JoinResult.NotFound, joinResult);
    }

    [Fact]
    public void LeaveRoom_LastPlayer_RemovesRoom()
    {
        // Arrange
        string roomId = _registry.AddRoom(_hostId, 2);
        var createdRoom = _registry.GetRoomById(roomId);

        var newPlayerId = Guid.NewGuid();
        _registry.JoinRoom(roomId, newPlayerId);

        // Act
        _registry.LeaveRoom(roomId, _hostId); // Host leaves
        _registry.LeaveRoom(roomId, newPlayerId); // New player leaves

        // Assert
        Assert.Null(_registry.GetRoomById(roomId)); // Room should be removed
    }

    [Fact]
    public void LeaveRoom_NullCode_ReturnsFalse()
    {
        // Arrange
        string roomId = _registry.AddRoom(_hostId, 2);

        // Act
        bool leaveResult = _registry.LeaveRoom(null, _hostId);

        // Assert
        Assert.False(leaveResult);
        Assert.Equal(1, _registry.GetRoomById(roomId)!.CurrentPlayers); // Host should still be in the room
    }

    [Fact]
    public void AddRoom_EightPlayers_Ok()
    {
        // Act
        string roomId = _registry.AddRoom(_hostId, 8);

        // Assert
        Assert.Equal(8, _registry.GetRoomById(roomId)!.MaxPlayers); // 컨셉 기획서: 한 방 2~8명
    }

    [Fact]
    public void AddRoom_NinePlayers_Throws()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => _registry.AddRoom(_hostId, 9));
    }

    [Fact]
    public void AddRoom_Private_IsPrivate()
    {
        // Act
        string privateId = _registry.AddRoom(_hostId, 8, isPrivate: true);
        string publicId = _registry.AddRoom(Guid.NewGuid(), 8);

        // Assert
        Assert.True(_registry.GetRoomById(privateId)!.IsPrivate);
        Assert.False(_registry.GetRoomById(publicId)!.IsPrivate); // 고르지 않으면 공개 방
    }

    [Fact]
    public void QuickJoin_NoRooms_CreatesPublicRoom()
    {
        // Act
        var (code, created) = _registry.QuickJoin(_hostId, 8);

        // Assert
        // 시스템 기획서 방 화면 랜덤 입장: 들어갈 방이 없으면 공개 방을 새로 만듦
        Assert.True(created);
        var room = _registry.GetRoomById(code)!;
        Assert.False(room.IsPrivate);
        Assert.Equal(1, room.CurrentPlayers);
    }

    [Fact]
    public void QuickJoin_PicksFullestJoinablePublicRoom()
    {
        // Arrange
        string small = _registry.AddRoom(Guid.NewGuid(), 8);
        string big = _registry.AddRoom(Guid.NewGuid(), 8);
        _registry.JoinRoom(big, Guid.NewGuid());

        // Act
        var (code, created) = _registry.QuickJoin(_hostId, 8);

        // Assert
        // 사람이 많은 방에 넣어 판이 빨리 시작하게 함
        Assert.False(created);
        Assert.Equal(big, code);
        Assert.Equal(3, _registry.GetRoomById(big)!.CurrentPlayers);
        Assert.Equal(1, _registry.GetRoomById(small)!.CurrentPlayers);
    }

    [Fact]
    public void QuickJoin_SkipsPrivateFullAndPlaying()
    {
        // Arrange
        // 비공개 방, 꽉 찬 방, 판이 진행 중인 방은 모두 사람이 더 많아도 고르지 않음
        string hidden = _registry.AddRoom(Guid.NewGuid(), 8, isPrivate: true);
        _registry.JoinRoom(hidden, Guid.NewGuid());
        _registry.JoinRoom(hidden, Guid.NewGuid());
        string full = _registry.AddRoom(Guid.NewGuid(), 2);
        _registry.JoinRoom(full, Guid.NewGuid());
        var playingHost = Guid.NewGuid();
        var playingGuest = Guid.NewGuid();
        string playing = _registry.AddRoom(playingHost, 8);
        _registry.JoinRoom(playing, playingGuest);
        var playingRoom = _registry.GetRoomById(playing)!;
        playingRoom.SetReady(playingHost, true);
        playingRoom.SetReady(playingGuest, true);
        playingRoom.Step(0); // 모두 준비해 카운트다운이 정해짐
        string open = _registry.AddRoom(Guid.NewGuid(), 8);

        // Act
        var (code, created) = _registry.QuickJoin(_hostId, 8);

        // Assert
        Assert.False(created);
        Assert.Equal(open, code);
    }

    [Fact]
    public void QuickJoin_Nickname_InRoom()
    {
        // Act
        var (code, _) = _registry.QuickJoin(_hostId, 8, null, "보안관");
        var state = _registry.GetRoomById(code)!.Step(0).OfType<StateMessage>().Single();

        // Assert
        Assert.Equal("보안관", state.Players.Single().Nickname);
    }

    [Fact]
    public void List_PrivateRoomHidden()
    {
        // Arrange
        string open = _registry.AddRoom(Guid.NewGuid(), 8);
        _registry.AddRoom(Guid.NewGuid(), 8, isPrivate: true);

        // Act
        var rooms = _registry.List();

        // Assert
        // 시스템 기획서 방 화면 방 만들기: 비공개 방은 대기실 보기에 나오지 않음
        Assert.Equal(open, Assert.Single(rooms).Code);
    }

    [Fact]
    public void List_JoinableFirstThenByCount()
    {
        // Arrange
        // 들어갈 수 있는 1명 방, 들어갈 수 있는 2명 방, 꽉 찬 2명 방, 판 진행 중인 3명 방
        var registry = new RoomRegistry(new Queue<string>(["AAAAAA", "BBBBBB", "CCCCCC", "DDDDDD"]).Dequeue);
        string one = registry.AddRoom(Guid.NewGuid(), 8);
        string two = registry.AddRoom(Guid.NewGuid(), 8);
        registry.JoinRoom(two, Guid.NewGuid());
        string full = registry.AddRoom(Guid.NewGuid(), 2);
        registry.JoinRoom(full, Guid.NewGuid());
        var players = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        string playing = registry.AddRoom(players[0], 8);
        registry.JoinRoom(playing, players[1]);
        registry.JoinRoom(playing, players[2]);
        var playingRoom = registry.GetRoomById(playing)!;
        foreach (var id in players)
        {
            playingRoom.SetReady(id, true);
        }
        playingRoom.Step(0); // 모두 준비해 카운트다운이 정해짐

        // Act
        var rooms = registry.List();

        // Assert
        // 시스템 기획서 방 화면 대기실 보기: 들어갈 수 있는 방을 위에, 그중 사람이 많은 방을 먼저
        Assert.Equal([two, one], rooms.Take(2).Select(r => r.Code));
        Assert.Equal(new[] { full, playing }.Order(), rooms.Skip(2).Select(r => r.Code).Order());
    }

    [Fact]
    public void List_PlayersWithNicknameAndColor()
    {
        // Arrange
        string code = _registry.AddRoom(Guid.NewGuid(), 8, null, "보안관");
        _registry.JoinRoom(code, Guid.NewGuid(), null, "떠돌이");

        // Act
        var room = Assert.Single(_registry.List());

        // Assert
        Assert.Equal([new RoomMember("보안관", 0), new RoomMember("떠돌이", 1)], room.Players);
        Assert.Equal(8, room.Max);
        Assert.False(room.Playing);
    }

    [Fact]
    public void List_RoundScheduled_Playing()
    {
        // Arrange
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        string code = _registry.AddRoom(a, 8);
        _registry.JoinRoom(code, b);
        var room = _registry.GetRoomById(code)!;
        room.SetReady(a, true);
        room.SetReady(b, true);
        room.Step(0); // 모두 준비해 카운트다운이 정해짐

        // Act
        var listed = Assert.Single(_registry.List());

        // Assert
        Assert.True(listed.Playing);
    }

    [Fact]
    public void AddRoom_Name_InList()
    {
        // Act
        _registry.AddRoom(_hostId, 4, null, "보안관", false, "먼지 낀 술집");
        var room = Assert.Single(_registry.List());

        // Assert
        Assert.Equal("먼지 낀 술집", room.Name);
        Assert.Equal(4, room.Max);
    }

    [Fact]
    public void AddRoom_NoName_NicknameRoom()
    {
        // Act
        _registry.AddRoom(_hostId, 8, null, "보안관");

        // Assert
        Assert.Equal("보안관's room", Assert.Single(_registry.List()).Name);
    }

    [Fact]
    public void AddRoom_NoNameNoNickname_PlaceholderRoom()
    {
        // Act
        _registry.AddRoom(_hostId, 8);

        // Assert
        Assert.Equal(RoomName.Default(Nickname.Placeholder), Assert.Single(_registry.List()).Name);
    }

    [Fact]
    public void QuickJoin_Creates_NicknameRoom()
    {
        // Act
        _registry.QuickJoin(_hostId, 8, null, "떠돌이");

        // Assert
        // 시스템 기획서 방 화면 랜덤 입장: 새로 만드는 방은 "닉네임's room", 8명, 공개
        var room = Assert.Single(_registry.List());
        Assert.Equal("떠돌이's room", room.Name);
        Assert.Equal(8, room.Max);
    }
}
