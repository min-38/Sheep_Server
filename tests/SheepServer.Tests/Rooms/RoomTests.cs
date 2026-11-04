namespace SheepServer.Tests.Rooms;

using SheepServer.Arena;
using SheepServer.Rooms;

public class RoomTests
{
    private const double Tolerance = 1e-9;
    private static readonly double CenterX = ArenaMap.Fixed.Width / 2.0;
    private static readonly double CenterY = ArenaMap.Fixed.Height / 2.0;

    private readonly Room _room = new("TestRoom", 4);
    private readonly Guid _a = Guid.NewGuid();
    private readonly Guid _b = Guid.NewGuid();

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

    [Fact]
    public void Step_TwoPlayers_BothInStateAtStartPlacement()
    {
        // Arrange
        _room.AddPlayer(_a);
        _room.AddPlayer(_b);

        // Act
        var state = _room.Step();

        // Assert
        Assert.Equal(_room.TickNumber, state.Tick);
        Assert.Equal(2, state.Players.Count);
        AssertStandsAt(StartPlacement.At(2, 0, CenterX, CenterY), Find(state, _a));
        AssertStandsAt(StartPlacement.At(2, 1, CenterX, CenterY), Find(state, _b));
    }

    [Fact]
    public void Step_AfterRemove_LeaverGoneAndRestRearranged()
    {
        // Arrange
        _room.AddPlayer(_a);
        _room.AddPlayer(_b);
        _room.Step();

        // Act
        _room.RemovePlayer(_a);
        var state = _room.Step();

        // Assert
        var b = Assert.Single(state.Players);
        Assert.Equal(_b, b.Id);
        AssertStandsAt(StartPlacement.At(1, 0, CenterX, CenterY), b); // 혼자 남으면 1명 배치로 다시 섬
    }

    [Fact]
    public void AddPlayer_NewcomerFacesStartAngle_OthersKeepTheirAngle()
    {
        // Arrange
        _room.AddPlayer(_a);
        _room.SetAngle(_a, 1.0);

        // Act
        _room.AddPlayer(_b);
        var state = _room.Step();

        // Assert
        Assert.Equal(1.0, Find(state, _a).Angle, Tolerance);
        Assert.Equal(StartPlacement.At(2, 1, CenterX, CenterY).Angle, Find(state, _b).Angle, Tolerance);
    }

    // 기술 설계서 메시지 turn: 라디안, 0 이상 2π 미만. 숫자가 아니거나 범위 밖이면 버림
    [Theory]
    [InlineData(-0.1)]
    [InlineData(2 * Math.PI)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void SetAngle_OutOfRangeOrNotNumber_KeepsPreviousAngle(double angle)
    {
        // Arrange
        _room.AddPlayer(_a);
        _room.SetAngle(_a, 1.0);

        // Act
        _room.SetAngle(_a, angle);
        var state = _room.Step();

        // Assert
        Assert.Equal(1.0, Find(state, _a).Angle, Tolerance);
    }

    private static PlayerState Find(StateMessage state, Guid id) => state.Players.Single(p => p.Id == id);

    private static void AssertStandsAt((double X, double Y, double Angle) spot, PlayerState player)
    {
        Assert.Equal(spot.X, player.X, Tolerance);
        Assert.Equal(spot.Y, player.Y, Tolerance);
    }
}