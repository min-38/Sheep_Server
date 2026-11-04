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

    [Fact]
    public void AddPlayer_NewcomerNotReady()
    {
        // Arrange
        _room.AddPlayer(_a);

        // Act
        var state = _room.Step();

        // Assert
        Assert.False(Find(state, _a).Ready);
    }

    [Fact]
    public void SetReady_ThenUnready_ReflectedInState()
    {
        // Arrange
        _room.AddPlayer(_a);
        _room.AddPlayer(_b);

        // Act
        _room.SetReady(_a, true);
        var ready = _room.Step();
        _room.SetReady(_a, false);
        var unready = _room.Step();

        // Assert
        Assert.True(Find(ready, _a).Ready);
        Assert.False(Find(ready, _b).Ready); // 다른 사람의 준비는 그대로
        Assert.False(Find(unready, _a).Ready);
    }

    [Fact]
    public void SetReady_SameValueTwice_StaysReady()
    {
        // Arrange
        _room.AddPlayer(_a);

        // Act
        _room.SetReady(_a, true);
        _room.SetReady(_a, true);
        var state = _room.Step();

        // Assert
        Assert.True(Find(state, _a).Ready); // 값을 보내는 메시지라 두 번 받아도 뒤집히지 않음
    }

    [Fact]
    public void RemovePlayer_ReadyLeaverGone_RejoinsNotReady()
    {
        // Arrange
        _room.AddPlayer(_a);
        _room.AddPlayer(_b);
        _room.SetReady(_a, true);

        // Act
        _room.RemovePlayer(_a);
        var afterLeave = _room.Step();
        _room.AddPlayer(_a);
        var afterRejoin = _room.Step();

        // Assert
        Assert.Equal(_b, Assert.Single(afterLeave.Players).Id);
        Assert.False(Find(afterRejoin, _a).Ready); // 나갈 때 준비도 함께 빠짐
    }

    [Fact]
    public void SetReady_PlayerNotInRoom_Ignored()
    {
        // Arrange
        _room.AddPlayer(_a);

        // Act
        var ex = Record.Exception(() => _room.SetReady(_b, true));
        var state = _room.Step();

        // Assert
        Assert.Null(ex); // 클라가 보낸 값이라 던지면 연결이 끊김 (SetAngle과 같음)
        Assert.False(Find(state, _a).Ready);
    }

    private static PlayerState Find(StateMessage state, Guid id) => state.Players.Single(p => p.Id == id);

    private static void AssertStandsAt((double X, double Y, double Angle) spot, PlayerState player)
    {
        Assert.Equal(spot.X, player.X, Tolerance);
        Assert.Equal(spot.Y, player.Y, Tolerance);
    }
}