namespace SheepServer.Tests.Rooms;

using SheepServer.Arena;
using SheepServer.Rooms;
using SheepServer.Rounds;

public class RoomTests
{
    private const double Tolerance = 1e-9;
    private static readonly double CenterX = ArenaMap.Fixed.Width / 2.0;
    private static readonly double CenterY = ArenaMap.Fixed.Height / 2.0;

    private readonly Room _room = new("TestRoom", 4);
    private readonly Guid _a = Guid.NewGuid();
    private readonly Guid _b = Guid.NewGuid();
    private readonly Guid _c = Guid.NewGuid();

    [Fact]
    public void Step_IncrementsTickNumber()
    {
        // Arrange
        int initialTickNumber = _room.TickNumber;

        // Act
        _room.Step(0);

        // Assert
        Assert.Equal(initialTickNumber + 1, _room.TickNumber);
    }

    [Fact]
    public void Step_Over20Ticks()
    {
        // Arrange
        for (int i = 0; i < 20; i++)
        {
            _room.Step(0);
        }

        // Act
        _room.Step(0);

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
        var state = StateAt(0);

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
        _room.Step(0);

        // Act
        _room.RemovePlayer(_a);
        var state = StateAt(0);

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
        var state = StateAt(0);

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
        var state = StateAt(0);

        // Assert
        Assert.Equal(1.0, Find(state, _a).Angle, Tolerance);
    }

    [Fact]
    public void AddPlayer_NewcomerNotReady()
    {
        // Arrange
        _room.AddPlayer(_a);

        // Act
        var state = StateAt(0);

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
        var ready = StateAt(0);
        _room.SetReady(_a, false);
        var unready = StateAt(0);

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
        var state = StateAt(0);

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
        var afterLeave = StateAt(0);
        _room.AddPlayer(_a);
        var afterRejoin = StateAt(0);

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
        var state = StateAt(0);

        // Assert
        Assert.Null(ex); // 클라가 보낸 값이라 던지면 연결이 끊김 (SetAngle과 같음)
        Assert.False(Find(state, _a).Ready);
    }

    [Fact]
    public void Step_TwoReady_AnnouncesCountdownOnce()
    {
        // Arrange
        AddReady(_a, _b);

        // Act
        var first = _room.Step(1000);
        var second = _room.Step(1050);

        // Assert
        AssertAnnounces(first, RoundPhase.Countdown, 1000 + RoundFlow.Lead);
        Assert.Single(first.OfType<StateMessage>()); // state는 알림과 별개로 틱마다 감
        Assert.Empty(second.OfType<PhaseMessage>()); // 같은 알림을 틱마다 되풀이하지 않음
    }

    [Fact]
    public void Step_OneOfTwoReady_NoAnnouncement()
    {
        // Arrange
        _room.AddPlayer(_a);
        _room.AddPlayer(_b);
        _room.SetReady(_a, true);

        // Act
        var messages = _room.Step(1000);

        // Assert
        Assert.Empty(messages.OfType<PhaseMessage>());
    }

    [Fact]
    public void Step_EachPhaseStarts_AnnouncesNextPhase()
    {
        // Arrange
        AddReady(_a, _b);
        _room.Step(1000);
        double countdownAt = 1000 + RoundFlow.Lead;
        double walkAt = countdownAt + 3 * RoundFlow.CountdownInterval;
        double duelAt = walkAt + RoundFlow.WalkDuration;

        // Act
        var atCountdown = _room.Step(countdownAt);
        var atWalk = _room.Step(walkAt);
        var atDuel = _room.Step(duelAt);

        // Assert
        // 다음 단계는 앞 단계가 시작할 때 알림 (기술 설계서 메시지 phase)
        AssertAnnounces(atCountdown, RoundPhase.Walk, walkAt);
        AssertAnnounces(atWalk, RoundPhase.Duel, duelAt);
        Assert.Empty(atDuel.OfType<PhaseMessage>()); // 결투 다음은 이후 이슈
    }

    [Fact]
    public void SetReady_AfterRoundScheduled_Ignored()
    {
        // Arrange
        AddReady(_a, _b);
        _room.Step(1000); // 카운트다운이 정해짐. 아직 시작 전임

        // Act
        _room.SetReady(_a, false);
        var state = StateAt(1050);

        // Assert
        Assert.True(Find(state, _a).Ready); // 판 도중에는 준비를 풀 수 없음. 판이 끝나면 모두 풂 (이후 이슈)
    }

    [Fact]
    public void RemovePlayer_DuringRound_RoundGoesOnAndOthersStayPut()
    {
        // Arrange
        AddReady(_a, _b, _c);
        var before = StateAt(1000); // 판이 정해짐
        double countdownAt = 1000 + RoundFlow.Lead;

        // Act
        _room.RemovePlayer(_c);
        var messages = _room.Step(countdownAt);
        var state = messages.OfType<StateMessage>().Single();

        // Assert
        AssertAnnounces(messages, RoundPhase.Walk, countdownAt + 3 * RoundFlow.CountdownInterval); // 남은 둘로 판이 이어짐
        Assert.Equal(2, state.Players.Count);
        Assert.DoesNotContain(state.Players, p => p.Id == _c);
        // 남은 사람을 다시 세우면 판 도중에 자리가 바뀜
        AssertSamePlace(Find(before, _a), Find(state, _a));
        AssertSamePlace(Find(before, _b), Find(state, _b));
    }

    private static PlayerState Find(StateMessage state, Guid id) => state.Players.Single(p => p.Id == id);

    private static void AssertStandsAt((double X, double Y, double Angle) spot, PlayerState player)
    {
        Assert.Equal(spot.X, player.X, Tolerance);
        Assert.Equal(spot.Y, player.Y, Tolerance);
    }

    // 이번 틱에 보낼 메시지 중 판 단계 알림이 하나이고, 단계와 시각이 맞는지 봄
    private static void AssertAnnounces(IEnumerable<object> messages, RoundPhase phase, double at)
    {
        var message = Assert.Single(messages.OfType<PhaseMessage>());
        Assert.Equal(phase, message.Phase);
        Assert.Equal(at, message.At, Tolerance);
    }

    private static void AssertSamePlace(PlayerState expected, PlayerState actual)
    {
        Assert.Equal(expected.X, actual.X, Tolerance);
        Assert.Equal(expected.Y, actual.Y, Tolerance);
    }

    // 틱 하나를 돌리고 그 틱의 state를 돌려줌
    private StateMessage StateAt(double now) => _room.Step(now).OfType<StateMessage>().Single();

    // 들어오는 대로 준비시킴. 다음 Step에서 모두 준비했는지 봄
    private void AddReady(params Guid[] ids)
    {
        foreach (var id in ids)
        {
            _room.AddPlayer(id);
            _room.SetReady(id, true);
        }
    }
}
