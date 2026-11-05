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

    [Fact]
    public void AddPlayer_NewcomerStands()
    {
        // Arrange
        _room.AddPlayer(_a);

        // Act
        var state = StateAt(0);

        // Assert
        Assert.Equal(Pose.Stand, Find(state, _a).Pose); // 기술 설계서 메시지 state: 들어올 때는 stand
    }

    [Fact]
    public void Step_WalkStarts_EveryoneTurnsOutwardAndWalks()
    {
        // Arrange
        AddReady(_a, _b, _c);
        _room.SetAngle(_a, 1.0); // 준비 중에 돌아 둔 각도는 걷는 방향과 상관없음
        double walkAt = ScheduleWalk(_room);

        // Act
        var state = StateAt(walkAt);

        // Assert
        foreach (var player in state.Players)
        {
            AssertFacesOutward(player);
            Assert.Equal(Pose.Walk, player.Pose);
        }
        AssertWalked(3, 0, 0, Find(state, _a)); // 막 출발해 아직 시작 자리임
    }

    [Fact]
    public void Step_HalfwayThroughWalk_HalfTheDistanceOutward()
    {
        // Arrange
        AddReady(_a, _b);
        double walkAt = ScheduleWalk(_room);
        _room.Step(walkAt);

        // Act
        var state = StateAt(walkAt + RoundFlow.WalkDuration / 2);

        // Assert
        // 같은 속도로 걸으니 시간이 절반이면 거리도 절반
        AssertWalked(2, 0, RoundFlow.WalkDistance / 2, Find(state, _a));
        AssertWalked(2, 1, RoundFlow.WalkDistance / 2, Find(state, _b));
        Assert.All(state.Players, p => Assert.Equal(Pose.Walk, p.Pose));
    }

    [Fact]
    public void Step_WalkStartTickLate_WalkFollowsScheduledTime()
    {
        // Arrange
        AddReady(_a, _b);
        double walkAt = ScheduleWalk(_room);
        _room.Step(walkAt + 40); // 틱은 50ms마다라 걷기 시각보다 늦게 옴

        // Act
        var state = StateAt(walkAt + RoundFlow.WalkDuration / 2);

        // Assert
        // 늦게 온 틱부터 재면 덜 걸어서, 걷기가 끝날 때 R + 걷는 거리에 못 미침 (RoundFlow가 NextAt에 더하는 것과 같은 이유)
        AssertWalked(2, 0, RoundFlow.WalkDistance / 2, Find(state, _a));
    }

    [Fact]
    public void Step_WalkEnds_StopsAtWalkDistanceAndStands()
    {
        // Arrange
        AddReady(_a, _b);
        double walkAt = ScheduleWalk(_room);
        double duelAt = walkAt + RoundFlow.WalkDuration;
        _room.Step(walkAt);

        // Act
        var atEnd = StateAt(duelAt); // 걷기가 끝나는 틱. 판은 이 틱에 결투로 넘어감
        var later = StateAt(duelAt + 1000);

        // Assert
        AssertWalked(2, 0, RoundFlow.WalkDistance, Find(atEnd, _a));
        AssertWalked(2, 1, RoundFlow.WalkDistance, Find(atEnd, _b));
        Assert.All(atEnd.Players, p => Assert.Equal(Pose.Stand, p.Pose));
        AssertWalked(2, 0, RoundFlow.WalkDistance, Find(later, _a)); // 걷는 거리에 닿으면 더 가지 않음
        AssertFacesOutward(Find(later, _a)); // 멈춘 뒤에도 바깥을 보고 섬
    }

    [Fact]
    public void Step_SevenFinishWalk_AllOnEmptyCells()
    {
        // Arrange
        var room = new Room("Seven", 7);
        var ids = new List<Guid>();
        for (int i = 0; i < 7; i++)
        {
            var id = Guid.NewGuid();
            ids.Add(id);
            room.AddPlayer(id);
            room.SetReady(id, true);
        }
        double walkAt = ScheduleWalk(room);
        room.Step(walkAt);

        // Act
        var state = room.Step(walkAt + RoundFlow.WalkDuration).OfType<StateMessage>().Single();

        // Assert
        // 기술 설계서 결투장 크기: 7명이 걷기를 마쳐도 벽에 닿지 않음
        for (int i = 0; i < 7; i++)
        {
            var player = Find(state, ids[i]);
            AssertWalked(7, i, RoundFlow.WalkDistance, player);
            Assert.Equal(0, ArenaMap.Fixed.Cells[(int)player.Y][(int)player.X]);
        }
    }

    [Fact]
    public void SetAngle_WhileWalking_Ignored()
    {
        // Arrange
        AddReady(_a, _b);
        double walkAt = ScheduleWalk(_room);
        _room.Step(walkAt);

        // Act
        _room.SetAngle(_a, 1.0);
        var state = StateAt(walkAt + 100);

        // Assert
        // 걷는 동안에는 서버가 방향을 정함 (기술 설계서 메시지 turn). 받은 각도로 걸으면 출발한 길에서 벗어남
        AssertFacesOutward(Find(state, _a));
        AssertWalked(2, 0, RoundFlow.WalkDistance * 100 / RoundFlow.WalkDuration, Find(state, _a));
    }

    [Fact]
    public void SetAngle_AfterWalk_Applied()
    {
        // Arrange
        AddReady(_a, _b);
        double walkAt = ScheduleWalk(_room);
        double duelAt = walkAt + RoundFlow.WalkDuration;
        _room.Step(walkAt);
        _room.Step(duelAt);

        // Act
        _room.SetAngle(_a, 1.0);
        var state = StateAt(duelAt + 50);

        // Assert
        Assert.Equal(1.0, Find(state, _a).Angle, Tolerance); // 걷기가 끝나면 다시 돌아 겨눌 수 있음
        AssertWalked(2, 0, RoundFlow.WalkDistance, Find(state, _a)); // 돌아도 자리는 그대로 (기술 설계서 명중 판정: 결투 중에는 아무도 걷지 않음)
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

    // 모두 준비한 방을 카운트다운 시작까지 돌리고, 걷기가 시작하는 시각을 돌려줌. 그 시각의 틱은 부르지 않음
    private static double ScheduleWalk(Room room)
    {
        room.Step(1000); // 카운트다운이 정해짐
        double countdownAt = 1000 + RoundFlow.Lead;
        room.Step(countdownAt); // 카운트다운이 시작하고 걷기가 정해짐
        return countdownAt + 3 * RoundFlow.CountdownInterval;
    }

    // count명 중 index번이 시작 자리에서 바깥으로 walked칸 걸어간 자리에 있는지 봄.
    // 바깥은 중심에서 시작 자리로 가는 방향이라, 중심에서 R + walked만큼 떨어짐
    private static void AssertWalked(int count, int index, double walked, PlayerState player)
    {
        var spot = StartPlacement.At(count, index, CenterX, CenterY);
        double scale = (StartPlacement.Radius + walked) / StartPlacement.Radius;
        Assert.Equal(CenterX + (spot.X - CenterX) * scale, player.X, Tolerance);
        Assert.Equal(CenterY + (spot.Y - CenterY) * scale, player.Y, Tolerance);
    }

    // 시선과 (자리 - 중심)의 내적이 중심까지 거리와 같으면 정확히 바깥을 봄 (StartPlacementTests의 FacesCenter와 반대)
    private static void AssertFacesOutward(PlayerState player)
    {
        double dx = player.X - CenterX;
        double dy = player.Y - CenterY;
        Assert.Equal(Math.Sqrt(dx * dx + dy * dy), Math.Cos(player.Angle) * dx + Math.Sin(player.Angle) * dy, Tolerance);
        Assert.True(player.Angle >= 0 && player.Angle < 2 * Math.PI, $"angle {player.Angle}는 turn과 같은 범위(0 이상 2π 미만)여야 함");
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
