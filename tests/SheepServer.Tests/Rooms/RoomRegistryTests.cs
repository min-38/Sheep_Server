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
    public void AddRoom_MaxPlayersOver7_Throws()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => _registry.AddRoom(_hostId, 8));
    }
}