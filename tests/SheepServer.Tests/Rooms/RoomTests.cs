namespace SheepServer.Tests.Rooms;

using SheepServer.Arena;
using SheepServer.Rooms;
using SheepServer.Rounds;
using SheepServer.Shooting;

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

        // Act
        var atCountdown = _room.Step(countdownAt);
        var atWalk = _room.Step(walkAt);

        // Assert
        // 걷기는 카운트다운이 시작할 때 알림 (기술 설계서 메시지 phase)
        AssertAnnounces(atCountdown, RoundPhase.Walk, walkAt);
        Assert.Empty(atWalk.OfType<PhaseMessage>()); // 결투 시각은 이때 정하지만 Lead 전에야 알림
    }

    [Fact]
    public void Step_DuelAnnouncedOnceLeadBeforeRandomTime()
    {
        // Arrange
        AddReady(_a, _b);
        double walkAt = ScheduleWalk(_room);
        double walkEnd = walkAt + RoundFlow.WalkDuration;

        // Act
        var (announcedAt, duelAt) = StepUntilDuelAnnounced(_room, walkAt);
        var atDuel = _room.Step(duelAt);

        // Assert
        // 걷기가 끝나고 무작위로 기다린 뒤 (컨셉 기획서 규칙 "시작!" 타이밍)
        Assert.InRange(duelAt, walkEnd + RoundFlow.MinDuelWait, walkEnd + RoundFlow.MaxDuelWait);
        // Lead만큼 앞서 알림. 틱은 50ms마다라 그만큼 늦을 수 있지만 결투 시각보다는 앞섬
        Assert.InRange(announcedAt, duelAt - RoundFlow.Lead, duelAt - RoundFlow.Lead + 50);
        Assert.Empty(atDuel.OfType<PhaseMessage>()); // 같은 알림을 되풀이하지 않음. 결투 다음은 이후 이슈
    }

    [Fact]
    public void Step_TenRooms_DuelTimesVary()
    {
        // Arrange
        var waits = new HashSet<double>();

        for (int i = 0; i < 10; i++)
        {
            var room = new Room($"Room{i}", 2);
            var a = Guid.NewGuid();
            var b = Guid.NewGuid();
            room.AddPlayer(a);
            room.AddPlayer(b);
            room.SetReady(a, true);
            room.SetReady(b, true);
            double walkAt = ScheduleWalk(room);

            // Act
            var (_, duelAt) = StepUntilDuelAnnounced(room, walkAt);
            waits.Add(duelAt - walkAt);
        }

        // Assert
        // 판마다 같은 시드를 쓰면 대기 시간이 늘 같아 타이밍을 외울 수 있음
        Assert.True(waits.Count > 1, "열 판의 결투 시각이 모두 걷기 시각에서 같은 만큼 떨어짐");
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
        double walkEnd = walkAt + RoundFlow.WalkDuration;
        _room.Step(walkAt);

        // Act
        var atEnd = StateAt(walkEnd); // 걷기가 끝나는 틱. 결투까지는 서서 무작위로 더 기다림
        var later = StateAt(walkEnd + 1000);

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
    public void SetAngle_DuringCountdown_Ignored()
    {
        // Arrange
        AddReady(_a, _b);
        _room.Step(1000); // 카운트다운이 정해짐
        var before = StateAt(1000 + RoundFlow.Lead); // 카운트다운이 시작함

        // Act
        _room.SetAngle(_a, 1.0);
        var state = StateAt(1000 + RoundFlow.Lead + 50);

        // Assert
        // "시작!" 전에 온 돌기는 버림 (기술 설계서 메시지 turn). 클라도 카운트다운부터 입력을 잠금
        Assert.Equal(Find(before, _a).Angle, Find(state, _a).Angle, Tolerance);
    }

    [Fact]
    public void SetAngle_WalkedButBeforeDuel_Ignored()
    {
        // Arrange
        AddReady(_a, _b);
        double walkAt = ScheduleWalk(_room);
        var (_, duelAt) = StepUntilDuelAnnounced(_room, walkAt); // 다 걸었고 결투 알림도 받음

        // Act
        _room.SetAngle(_a, 1.0);
        var state = StateAt(duelAt - 1);

        // Assert
        // 걷기가 끝나도 "시작!" 전이면 버림. 받으면 신호 전에 미리 돌아 겨눌 수 있음
        AssertFacesOutward(Find(state, _a));
        AssertWalked(2, 0, RoundFlow.WalkDistance, Find(state, _a));
    }

    [Fact]
    public void SetAngle_AtDuel_Applied()
    {
        // Arrange
        AddReady(_a, _b);
        double walkAt = ScheduleWalk(_room);
        var (_, duelAt) = StepUntilDuelAnnounced(_room, walkAt);
        _room.Step(duelAt);

        // Act
        _room.SetAngle(_a, 1.0);
        var state = StateAt(duelAt + 50);

        // Assert
        Assert.Equal(1.0, Find(state, _a).Angle, Tolerance); // "시작!"부터 다시 돌아 겨눌 수 있음
        AssertWalked(2, 0, RoundFlow.WalkDistance, Find(state, _a)); // 돌아도 자리는 그대로 (기술 설계서 명중 판정: 결투 중에는 아무도 걷지 않음)
    }

    [Fact]
    public void Fire_InLobby_Ignored()
    {
        // Arrange
        _room.AddPlayer(_a);
        _room.AddPlayer(_b);

        // Act
        var messages = _room.Fire(_a, Math.PI, 0, 0);

        // Assert
        Assert.Empty(messages); // 결투 단계가 아니면 버림 (기술 설계서 메시지 fire)
    }

    [Fact]
    public void Fire_BeforeDuel_Ignored()
    {
        // Arrange
        AddReady(_a, _b);
        double walkAt = ScheduleWalk(_room);
        var (_, duelAt) = StepUntilDuelAnnounced(_room, walkAt);

        // Act
        var messages = _room.Fire(_a, Math.PI, duelAt - 1, duelAt - 1);

        // Assert
        Assert.Empty(messages); // 다 걷고 "시작!"을 기다리는 동안 쏘면 버림
    }

    [Fact]
    public void Fire_AfterDuelTimeBeforeTick_Accepted()
    {
        // Arrange
        AddReady(_a, _b);
        double walkAt = ScheduleWalk(_room);
        var (_, duelAt) = StepUntilDuelAnnounced(_room, walkAt);

        // Act
        // 결투 시각은 지났지만 그 시각의 틱은 아직 돌지 않음. 클라는 자기 시계로 "시작!"을 보고 쏘므로 틱보다 먼저 올 수 있음
        var messages = _room.Fire(_a, Math.PI, duelAt, duelAt + 10);

        // Assert
        Assert.NotEmpty(messages);
        Assert.IsType<ShotMessage>(messages[0]);
    }

    [Fact]
    public void Fire_AtOpponent_ShotThenEliminated()
    {
        // Arrange
        double duelAt = StartDuel();

        // Act
        var messages = _room.Fire(_a, Math.PI, duelAt + 100, duelAt + 120);
        var state = StateAt(duelAt + 150);

        // Assert
        // 쏜 사람과 각도를 먼저 알리고, 맞은 사람을 그다음에 알림 (기술 설계서 메시지 shot, eliminated)
        Assert.Collection(
            messages,
            m =>
            {
                var shot = Assert.IsType<ShotMessage>(m);
                Assert.Equal(_a, shot.PlayerId);
                Assert.Equal(Math.PI, shot.Angle, Tolerance);
            },
            m => Assert.Equal(_b, Assert.IsType<EliminatedMessage>(m).PlayerId));
        Assert.Equal(Pose.Down, Find(state, _b).Pose);
        Assert.Equal(Pose.Stand, Find(state, _a).Pose);
    }

    [Fact]
    public void Fire_AwayFromEveryone_ShotOnly()
    {
        // Arrange
        double duelAt = StartDuel();

        // Act
        // a는 바깥을 보고 섬. 그대로 쏘면 벽에 맞음. 광선이 a의 원 안에서 출발해도 a 자신은 맞지 않음
        var messages = _room.Fire(_a, 0, duelAt + 100, duelAt + 120);
        var state = StateAt(duelAt + 150);

        // Assert
        var shot = Assert.IsType<ShotMessage>(Assert.Single(messages));
        Assert.Equal(_a, shot.PlayerId);
        Assert.All(state.Players, p => Assert.Equal(Pose.Stand, p.Pose));
    }

    [Fact]
    public void Fire_SeventhShot_Ignored()
    {
        // Arrange
        double duelAt = StartDuel();

        // Act & Assert
        // 컨셉 기획서 규칙: 리볼버 6발, 재장전 없음. 아무도 안 맞게 바깥으로 쏨
        for (int i = 0; i < 6; i++)
        {
            double at = duelAt + i * Revolver.FireInterval;
            Assert.Single(_room.Fire(_a, 0, at, at + 20));
        }
        double seventh = duelAt + 6 * Revolver.FireInterval;
        Assert.Empty(_room.Fire(_a, 0, seventh, seventh + 20));
    }

    [Fact]
    public void Fire_FasterThanInterval_Ignored()
    {
        // Arrange
        double duelAt = StartDuel();
        _room.Fire(_a, 0, duelAt, duelAt + 20);

        // Act
        // 둘 다 첫 발보다 480ms 늦게 도착함. 발사 간격은 도착 시각이 아니라 쏜 시각(at)끼리 재므로 첫 번째만 버림
        var tooSoon = _room.Fire(_a, 0, duelAt + Revolver.FireInterval - 1, duelAt + 500);
        var onTime = _room.Fire(_a, 0, duelAt + Revolver.FireInterval, duelAt + 500);

        // Assert
        Assert.Empty(tooSoon);
        Assert.Single(onTime);
    }

    [Fact]
    public void Fire_IgnoredShot_DoesNotStartInterval()
    {
        // Arrange
        AddReady(_a, _b);
        double walkAt = ScheduleWalk(_room);
        var (_, duelAt) = StepUntilDuelAnnounced(_room, walkAt);
        _room.Fire(_a, 0, duelAt - 1, duelAt - 1); // "시작!" 전이라 버림
        _room.Step(duelAt);

        // Act
        var messages = _room.Fire(_a, 0, duelAt, duelAt + 20);

        // Assert
        Assert.Single(messages); // 버린 발사는 총알도 발사 간격도 쓰지 않음. 쓰면 클라가 센 탄 수와 어긋남
    }

    [Fact]
    public void Fire_ByDownPlayer_Ignored()
    {
        // Arrange
        double duelAt = StartDuel();
        _room.Fire(_a, Math.PI, duelAt + 100, duelAt + 120); // b가 쓰러짐

        // Act
        var messages = _room.Fire(_b, 0, duelAt + 200, duelAt + 220); // 쓰러진 b가 a를 겨눠 쏨
        var state = StateAt(duelAt + 250);

        // Assert
        Assert.Empty(messages);
        Assert.Equal(Pose.Stand, Find(state, _a).Pose);
    }

    [Fact]
    public void Fire_AtDownPlayer_NotEliminatedTwice()
    {
        // Arrange
        double duelAt = StartDuel();
        _room.Fire(_a, Math.PI, duelAt + 100, duelAt + 120); // b가 쓰러짐

        // Act
        double at = duelAt + 100 + Revolver.FireInterval;
        var messages = _room.Fire(_a, Math.PI, at, at + 20);

        // Assert
        // 쓰러진 사람은 다시 맞지 않음. 탈락은 한 판에 한 사람마다 한 번만 보냄 (기술 설계서 메시지 eliminated)
        Assert.IsType<ShotMessage>(Assert.Single(messages));
    }

    // 기술 설계서 메시지 fire: angle은 turn과 같은 범위, at은 숫자. 아니면 버림
    [Theory]
    [InlineData(-0.1, 100)]
    [InlineData(2 * Math.PI, 100)]
    [InlineData(double.NaN, 100)]
    [InlineData(double.PositiveInfinity, 100)]
    [InlineData(Math.PI, double.NaN)]
    [InlineData(Math.PI, double.PositiveInfinity)]
    public void Fire_AngleOrShotTimeInvalid_Ignored(double angle, double afterDuel)
    {
        // Arrange
        double duelAt = StartDuel();

        // Act
        var messages = _room.Fire(_a, angle, duelAt + afterDuel, duelAt + 120);
        var state = StateAt(duelAt + 150);

        // Assert
        Assert.Empty(messages);
        Assert.Equal(Pose.Stand, Find(state, _b).Pose);
    }

    [Fact]
    public void Fire_PlayerNotInRoom_Ignored()
    {
        // Arrange
        double duelAt = StartDuel();

        // Act
        var messages = _room.Fire(_c, Math.PI, duelAt + 100, duelAt + 120);

        // Assert
        Assert.Empty(messages);
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

    // 걷기 시각부터 50ms마다 틱을 돌려 결투 알림을 찾음. 알린 틱의 시각과 알린 결투 시각을 돌려줌
    private static (double AnnouncedAt, double DuelAt) StepUntilDuelAnnounced(Room room, double walkAt)
    {
        for (double now = walkAt; now <= walkAt + RoundFlow.WalkDuration + RoundFlow.MaxDuelWait; now += 50)
        {
            var phase = room.Step(now).OfType<PhaseMessage>().SingleOrDefault();
            if (phase != null)
            {
                Assert.Equal(RoundPhase.Duel, phase.Phase);
                return (now, phase.At);
            }
        }

        Assert.Fail("가장 늦은 결투 시각까지 결투 알림이 오지 않음");
        return default;
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

    // 둘을 준비시켜 결투 시각의 틱까지 돌리고 그 시각을 돌려줌.
    // a는 0번 자리(중심의 +x 쪽), b는 1번 자리(중심의 -x 쪽)에서 바깥을 보고 섬. 그래서 a가 π를 보고 쏘면 b가 맞음
    private double StartDuel()
    {
        AddReady(_a, _b);
        double walkAt = ScheduleWalk(_room);
        var (_, duelAt) = StepUntilDuelAnnounced(_room, walkAt);
        _room.Step(duelAt);
        return duelAt;
    }
}
