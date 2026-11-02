namespace SheepServer.Tests.Rooms;

using SheepServer.Rooms;

public class RoomTests
{
    private readonly Room _room = new("TestRoom", 4);

    [Fact]
    public void Step_IncrementsTickNumber()
    {
        // Arrange
        int initialTickNumber = _room.TickNumber;

        // Act
        _room.Step();

        // Assert
        Assert.Equal(initialTickNumber + 1, _room.TickNumber);
    }

    [Fact]
    public void Step_Over20Ticks()
    {
        // Arrange
        for (int i = 0; i < 20; i++)
        {
            _room.Step();
        }

        // Act
        _room.Step();

        // Assert
        Assert.Equal(21, _room.TickNumber);
    }
}