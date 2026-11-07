namespace SheepServer.Tests.Rooms;

using SheepServer.Arena;
using SheepServer.Rooms;
using SheepServer.Rounds;
using SheepServer.Shooting;

public class RoomTests
{
    private const double Tolerance = 1e-9;

    // 셋이 다 걸었을 때 a(0번, +x 쪽)에서 b(1번)와 c(2번)를 겨누는 각도, b에서 a를, c에서 b를 겨누는 각도.
    // 셋은 중심에서 같은 거리에 120°씩 떨어져 있어 서로 정삼각형을 이룸
    private const double AToB = 5 * Math.PI / 6;
    private const double AToC = 7 * Math.PI / 6;
    private const double BToA = 11 * Math.PI / 6;
    private const double CToB = Math.PI / 2;
    private static readonly double CenterX = ArenaMap.Fixed.Width / 2.0;
    private static readonly double CenterY = ArenaMap.Fixed.Height / 2.0;

    private readonly Room _room = new("TestRoom", 4);
    private readonly Guid _a = Guid.NewGuid();
    private readonly Guid _b = Guid.NewGuid();
    private readonly Guid _c = Guid.NewGuid();
    private readonly Guid _d = Guid.NewGuid();

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
        Assert.Empty(atDuel.OfType<PhaseMessage>()); // 같은 알림을 되풀이하지 않음. 결투 다음은 결과가 정해질 때 알림
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
        Assert.True(Find(state, _a).Ready); // 판 도중에는 준비를 풀 수 없음. 승리가 정해질 때 모두 풂
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
    public void Step_EightFinishWalk_AllOnEmptyCells()
    {
        // Arrange
        var room = new Room("Eight", 8);
        var ids = new List<Guid>();
        for (int i = 0; i < 8; i++)
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
        // 기술 설계서 결투장 크기: 8명이 걷기를 마쳐도 벽에 닿지 않음
        for (int i = 0; i < 8; i++)
        {
            var player = Find(state, ids[i]);
            AssertWalked(8, i, RoundFlow.WalkDistance, player);
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
        var messages = _room.Fire(_a, Math.PI, 0, 0, 0);

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
        var messages = _room.Fire(_a, Math.PI, 0, duelAt - 1, duelAt - 1);

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
        var messages = _room.Fire(_a, Math.PI, 0, duelAt, duelAt + 10);

        // Assert
        Assert.NotEmpty(messages);
        Assert.IsType<ShotMessage>(messages[0]);
    }

    [Fact]
    public void Fire_AtOpponent_ShotOnlyUntilMaxDelay()
    {
        // Arrange
        double duelAt = StartDuelWith(_a, _b, _c); // 셋이어야 b가 쓰러져도 결과 없이 결투가 이어짐

        // Act
        var messages = _room.Fire(_a, AToB, 0, duelAt + 100, duelAt + 120);
        var state = StateAt(duelAt + 100 + ShotJudge.MaxDelay - 1);

        // Assert
        // 쏜 사람과 각도는 바로 알림. 명중은 쏜 시각에서 W가 지나야 정함 (기술 설계서 메시지 shot, 명중 판정 확정 대기)
        var shot = Assert.IsType<ShotMessage>(Assert.Single(messages));
        Assert.Equal(_a, shot.PlayerId);
        Assert.Equal(AToB, shot.Angle, Tolerance);
        Assert.Equal(Pose.Stand, Find(state, _b).Pose);
    }

    [Fact]
    public void Step_MaxDelayAfterShotTime_EliminatedThenStateDown()
    {
        // Arrange
        double duelAt = StartDuelWith(_a, _b, _c); // 셋이어야 b가 쓰러져도 결과 없이 결투가 이어짐
        _room.Fire(_a, AToB, 0, duelAt + 100, duelAt + 120);

        // Act
        var messages = _room.Step(duelAt + 100 + ShotJudge.MaxDelay);

        // Assert
        // 명중을 정한 틱에 쏜 사람에게만 사격 결과를, 모두에게 탈락을 알리고, 같은 틱의 state부터 쓰러짐. 둘이 남고 c에게 총알이 있어 결과는 없음
        Assert.Collection(
            messages,
            m => Assert.Equal(_a, Assert.IsType<Direct>(m).To),
            m => Assert.Equal(_b, Assert.IsType<EliminatedMessage>(m).PlayerId),
            m =>
            {
                var state = Assert.IsType<StateMessage>(m);
                Assert.Equal(Pose.Down, Find(state, _b).Pose);
                Assert.Equal(Pose.Stand, Find(state, _a).Pose);
            });
    }

    [Fact]
    public void Step_FiredFirstButArrivedLater_Wins()
    {
        // Arrange
        double duelAt = StartDuel();
        double aShot = duelAt + 100;
        double bShot = aShot + ShotJudge.MutualWindow + 1;
        _room.Fire(_b, 0, 0, bShot, bShot + 10); // b가 a를 겨눠 쏨. 지연이 작아 먼저 도착함
        _room.Fire(_a, Math.PI, 0, aShot, aShot + ShotJudge.MaxDelay); // a는 D보다 더 먼저 쐈지만 지연이 W만큼 커 늦게 도착함

        // Act
        var messages = _room.Step(bShot + ShotJudge.MaxDelay);

        // Assert
        // 도착 순서가 아니라 쏜 시각 순서로 판정함. b는 맞고 D가 지난 뒤에 쏘아 그 발사는 무효 (기술 설계서 명중 판정)
        Assert.Equal(_b, Assert.Single(messages.OfType<EliminatedMessage>()).PlayerId);
        var state = messages.OfType<StateMessage>().Single();
        Assert.Equal(Pose.Stand, Find(state, _a).Pose);
        Assert.Equal(Pose.Down, Find(state, _b).Pose);
    }

    [Fact]
    public void Step_HitBackWithinMutualWindow_BothDownThenRematch()
    {
        // Arrange
        double duelAt = StartDuel();
        double aShot = duelAt + 100;
        double bShot = aShot + ShotJudge.MutualWindow;
        _room.Fire(_a, Math.PI, 0, aShot, aShot + 20);
        _room.Step(aShot + ShotJudge.MaxDelay); // b가 쓰러졌다고 정함

        // Act
        // b의 발사는 b가 쓰러진 뒤에 도착했지만 맞은 시각에서 D 안에 쐈음 (기술 설계서 명중 판정 함께 쓰러짐)
        var fired = _room.Fire(_b, 0, 0, bShot, aShot + ShotJudge.MaxDelay + 20);
        var hit = _room.Step(bShot + ShotJudge.MaxDelay);
        double now = bShot + ShotJudge.MaxDelay + ShotJudge.MutualWindow;
        var decided = _room.Step(now);

        // Assert
        // 아무도 안 남아 함께 쓰러진 둘이 한 발씩 들고 다음 라운드로 감. 결과는 마지막으로 맞은 시각에서 W + D가 지난 틱에 정함
        Assert.IsType<ShotMessage>(Assert.Single(fired));
        Assert.Equal(_a, Assert.Single(hit.OfType<EliminatedMessage>()).PlayerId);
        Assert.Empty(hit.OfType<ResultMessage>());
        AssertResult(Assert.Single(decided.OfType<ResultMessage>()), Outcome.Rematch, [_a, _b], 1);
        AssertAnnounces(decided, RoundPhase.Countdown, now + RoundFlow.ResultDuration);
    }

    [Fact]
    public void Fire_ShotTimeBeforeDuel_Ignored()
    {
        // Arrange
        double duelAt = StartDuel();

        // Act
        // 결투 시각이 지나 도착했지만 쏜 시각은 그 전임 (기술 설계서 명중 판정 쏜 시각 검사)
        var messages = _room.Fire(_a, Math.PI, 0, duelAt - 1, duelAt + 10);

        // Assert
        Assert.Empty(messages);
    }

    [Fact]
    public void Fire_ShotTimeMoreThanMaxDelayBeforeArrival_Clamped()
    {
        // Arrange
        double duelAt = StartDuel();
        _room.Fire(_b, 0, 0, duelAt + 150, duelAt + 160);
        _room.Fire(_a, Math.PI, 0, duelAt, duelAt + 500); // a는 결투 시각에 쐈다지만 500ms 걸려 옴. 쏜 시각은 duelAt + 300으로 잘림

        // Act
        var state = StateAt(duelAt + 500);

        // Assert
        // 자르지 않으면 a가 먼저 쏜 것이라 b가 쓰러짐. 잘라서 b가 먼저이고, a는 맞고 D가 지난 뒤라 무효
        Assert.Equal(Pose.Down, Find(state, _a).Pose);
        Assert.Equal(Pose.Stand, Find(state, _b).Pose);
    }

    [Fact]
    public void Fire_ShotTimeAfterArrival_ClampedToArrival()
    {
        // Arrange
        double duelAt = StartDuel();
        _room.Fire(_a, Math.PI, 0, duelAt + 1000, duelAt + 20); // 도착보다 늦게 쐈다는 값. 쏜 시각은 duelAt + 20으로 잘림
        _room.Fire(_b, 0, 0, duelAt + 200, duelAt + 210);

        // Act
        var state = StateAt(duelAt + 200 + ShotJudge.MaxDelay);

        // Assert
        // 자르지 않으면 b가 먼저 쏜 것이라 a가 쓰러짐. 잘라서 a가 먼저이고, b는 맞고 D가 지난 뒤라 무효
        Assert.Equal(Pose.Stand, Find(state, _a).Pose);
        Assert.Equal(Pose.Down, Find(state, _b).Pose);
    }

    [Fact]
    public void Fire_AwayFromEveryone_ShotOnly()
    {
        // Arrange
        double duelAt = StartDuel();

        // Act
        // a는 바깥을 보고 섬. 그대로 쏘면 벽에 맞음. 광선이 a의 원 안에서 출발해도 a 자신은 맞지 않음
        var messages = _room.Fire(_a, 0, 0, duelAt + 100, duelAt + 120);
        var state = StateAt(duelAt + 150);

        // Assert
        var shot = Assert.IsType<ShotMessage>(Assert.Single(messages));
        Assert.Equal(_a, shot.PlayerId);
        Assert.All(state.Players, p => Assert.Equal(Pose.Stand, p.Pose));
    }

    [Fact]
    public void Fire_SecondShot_Ignored()
    {
        // Arrange
        double duelAt = StartDuel();
        _room.Fire(_a, 0, 0, duelAt, duelAt + 20); // 아무도 안 맞게 바깥으로 쏨

        // Act
        double next = duelAt + Revolver.FireInterval;
        var messages = _room.Fire(_a, 0, 0, next, next + 20);

        // Assert
        Assert.Empty(messages); // 컨셉 기획서 규칙: 라운드마다 한 발. 발사 간격이 지나도 더 쏠 수 없음
    }

    [Fact]
    public void Fire_IgnoredShot_DoesNotStartInterval()
    {
        // Arrange
        AddReady(_a, _b);
        double walkAt = ScheduleWalk(_room);
        var (_, duelAt) = StepUntilDuelAnnounced(_room, walkAt);
        _room.Fire(_a, 0, 0, duelAt - 1, duelAt - 1); // "시작!" 전이라 버림
        _room.Step(duelAt);

        // Act
        var messages = _room.Fire(_a, 0, 0, duelAt, duelAt + 20);

        // Assert
        Assert.Single(messages); // 버린 발사는 총알도 발사 간격도 쓰지 않음. 쓰면 클라가 센 탄 수와 어긋남
    }

    [Fact]
    public void Fire_ByDownPlayerAfterMutualWindow_Ignored()
    {
        // Arrange
        // 셋이어야 b가 쓰러져도 결투가 이어짐. 둘이면 a가 이겨 결과 뒤의 발사로 버려짐
        double duelAt = StartDuelWith(_a, _b, _c);
        double hitAt = duelAt + 100;
        _room.Fire(_a, AToB, 0, hitAt, hitAt + 20);
        _room.Step(hitAt + ShotJudge.MaxDelay); // b가 쓰러짐

        // Act
        double late = hitAt + ShotJudge.MutualWindow + 1;
        var messages = _room.Fire(_b, BToA, 0, late, late + 20); // 쓰러진 b가 맞고 D가 지난 뒤에 a를 겨눠 쏨
        var state = StateAt(late + ShotJudge.MaxDelay);

        // Assert
        Assert.Empty(messages);
        Assert.Equal(Pose.Stand, Find(state, _a).Pose);
    }

    [Fact]
    public void Fire_AtDownPlayer_NotEliminatedTwice()
    {
        // Arrange
        double duelAt = StartDuelWith(_a, _b, _c); // 셋이어야 b가 쓰러져도 결투가 이어짐
        _room.Fire(_a, AToB, 0, duelAt + 100, duelAt + 120);
        _room.Step(duelAt + 100 + ShotJudge.MaxDelay); // b가 쓰러짐

        // Act
        double at = duelAt + 100 + ShotJudge.MaxDelay;
        _room.Fire(_c, CToB, 0, at, at + 20); // a는 한 발을 다 써서 c가 쓰러진 b를 겨눠 쏨
        var messages = _room.Step(at + ShotJudge.MaxDelay);

        // Assert
        // 쓰러진 사람은 다시 맞지 않음. 탈락은 한 결투에 한 사람마다 한 번만 보냄 (기술 설계서 메시지 eliminated)
        Assert.Empty(messages.OfType<EliminatedMessage>());
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
        var messages = _room.Fire(_a, angle, 0, duelAt + afterDuel, duelAt + 120);
        var state = StateAt(duelAt + 150);

        // Assert
        Assert.Empty(messages);
        Assert.Equal(Pose.Stand, Find(state, _b).Pose);
    }

    // 기술 설계서 메시지 fire: pitch는 -π/2보다 크고 π/2보다 작은 숫자. tan이 무한대가 되거나 뒤로 넘어가는 값은 버림
    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(Math.PI / 2)]
    [InlineData(-Math.PI / 2)]
    public void Fire_PitchOutOfRange_Ignored(double pitch)
    {
        // Arrange
        double duelAt = StartDuel();

        // Act
        var messages = _room.Fire(_a, Math.PI, pitch, duelAt + 100, duelAt + 120);
        var state = StateAt(duelAt + 100 + ShotJudge.MaxDelay);

        // Assert
        Assert.Empty(messages);
        Assert.Equal(Pose.Stand, Find(state, _b).Pose);
    }

    [Fact]
    public void Step_ShotJudged_ShooterGetsDirectShotResult()
    {
        // Arrange
        double duelAt = StartDuelWith(_a, _b, _c); // 셋이어야 b가 쓰러져도 결과 없이 결투가 이어짐
        _room.Fire(_a, AToB, 0, duelAt + 100, duelAt + 120);

        // Act
        var messages = _room.Step(duelAt + 100 + ShotJudge.MaxDelay);

        // Assert
        // 사격 결과는 쏜 사람 a에게만 감. 수평으로 쏴 몸에 맞고, 반샷이 없어 쓰러짐. 탈락은 지금처럼 모두에게 감 (기술 설계서 메시지 shotResult)
        var direct = Assert.Single(messages.OfType<Direct>());
        Assert.Equal(_a, direct.To);
        var result = Assert.IsType<ShotResultMessage>(direct.Message);
        Assert.Equal(HitZone.Body, result.Zone);
        Assert.Equal(HitScan.EyeHeight, result.Height!.Value, Tolerance);
        Assert.True(result.Down);
        Assert.Equal(_b, Assert.Single(messages.OfType<EliminatedMessage>()).PlayerId);
    }

    [Fact]
    public void Step_ShotMissed_ShooterGetsMissResult()
    {
        // Arrange
        double duelAt = StartDuel();
        _room.Fire(_a, 0, 0, duelAt + 100, duelAt + 120); // a는 바깥을 보고 쏨

        // Act
        var messages = _room.Step(duelAt + 100 + ShotJudge.MaxDelay);

        // Assert
        var direct = Assert.Single(messages.OfType<Direct>());
        Assert.Equal(_a, direct.To);
        var result = Assert.IsType<ShotResultMessage>(direct.Message);
        Assert.Null(result.Zone);
        Assert.False(result.Down);
        Assert.Empty(messages.OfType<EliminatedMessage>());
    }

    [Fact]
    public void Fire_AimedOverOpponentsHead_Miss()
    {
        // Arrange
        double duelAt = StartDuelWith(_a, _b, _c);

        // Act
        // b를 겨눴지만 위를 보고 쏨. 위아래 각도도 판정에 들어감
        _room.Fire(_a, AToB, 0.4, duelAt + 100, duelAt + 120);
        var state = StateAt(duelAt + 100 + ShotJudge.MaxDelay);

        // Assert
        Assert.Equal(Pose.Stand, Find(state, _b).Pose);
    }

    [Fact]
    public void Fire_PlayerNotInRoom_Ignored()
    {
        // Arrange
        double duelAt = StartDuel();

        // Act
        var messages = _room.Fire(_c, Math.PI, 0, duelAt + 100, duelAt + 120);

        // Assert
        Assert.Empty(messages);
    }

    [Fact]
    public void Step_LastOpponentDown_ResultWinAfterMutualWindow()
    {
        // Arrange
        double duelAt = StartDuel();
        double hitAt = duelAt + 100;
        _room.Fire(_a, Math.PI, 0, hitAt, hitAt + 20);
        var hit = _room.Step(hitAt + ShotJudge.MaxDelay);

        // Act
        var waiting = _room.Step(hitAt + ShotJudge.MaxDelay + ShotJudge.MutualWindow - 1);
        double now = hitAt + ShotJudge.MaxDelay + ShotJudge.MutualWindow;
        var messages = _room.Step(now);

        // Assert
        // b가 맞은 시각에서 D 안에 쏜 발사가 아직 올 수 있어 W + D가 지나야 승리를 정함.
        // 결과 바로 뒤에 준비 중으로 돌아가는 시각을 알림. 그 시각은 결과를 정한 시각에 결과 시간을 더한 값 (기술 설계서 메시지 result)
        Assert.Equal(_b, Assert.Single(hit.OfType<EliminatedMessage>()).PlayerId);
        Assert.Empty(hit.OfType<ResultMessage>());
        Assert.Empty(waiting.OfType<ResultMessage>());
        Assert.Collection(
            messages,
            m => AssertResult(Assert.IsType<ResultMessage>(m), Outcome.Win, [_a], 0),
            m => AssertPhase(m, RoundPhase.Lobby, now + RoundFlow.ResultDuration),
            m => Assert.IsType<StateMessage>(m));
    }

    [Fact]
    public void Fire_AfterResult_Ignored()
    {
        // Arrange
        double duelAt = StartDuel();
        double decidedAt = AWins(duelAt) - RoundFlow.ResultDuration;

        // Act
        var messages = _room.Fire(_a, 0, 0, decidedAt, decidedAt + 20);

        // Assert
        Assert.Empty(messages); // 결과가 정해진 뒤 다음 단계 시각까지 온 발사는 버림
    }

    [Fact]
    public void Fire_Win_EveryoneUnready()
    {
        // Arrange
        double duelAt = StartDuel(); // 둘 다 준비한 채로 판이 돎

        // Act
        double decidedAt = AWins(duelAt) - RoundFlow.ResultDuration;
        var state = StateAt(decidedAt + 50);

        // Assert
        // 승리가 정해질 때 모두 풂. 결과를 보는 동안에도 준비 수가 0으로 보임 (기술 설계서 메시지 ready)
        Assert.All(state.Players, p => Assert.False(p.Ready));
    }

    [Fact]
    public void SetReady_WhileShowingResult_Ignored()
    {
        // Arrange
        double duelAt = StartDuel();
        double lobbyAt = AWins(duelAt);

        // Act
        _room.SetReady(_a, true);
        var state = StateAt(lobbyAt - 50);

        // Assert
        Assert.False(Find(state, _a).Ready); // 준비 중으로 돌아가기 전에는 버림
        Assert.True(_room.InRound); // 들어올 수도 없음 (기술 설계서 메시지 joinRoom)
    }

    [Fact]
    public void Step_AtLobbyTime_EveryoneBackAtStartPlacementFacingCenter()
    {
        // Arrange
        double duelAt = StartDuel();
        double lobbyAt = AWins(duelAt);

        // Act
        var state = StateAt(lobbyAt);

        // Assert
        // 쓰러진 b도 일어나 모두 처음 자리에서 서로 마주 봄 (기술 설계서 결투장 다시 서기)
        Assert.False(_room.InRound);
        Guid[] ids = [_a, _b];
        for (int i = 0; i < ids.Length; i++)
        {
            var spot = StartPlacement.At(ids.Length, i, CenterX, CenterY);
            var player = Find(state, ids[i]);
            AssertStandsAt(spot, player);
            Assert.Equal(spot.Angle, player.Angle, Tolerance);
            Assert.Equal(Pose.Stand, player.Pose);
        }
    }

    [Fact]
    public void SetReady_AfterLobby_Applied()
    {
        // Arrange
        double duelAt = StartDuel();
        double lobbyAt = AWins(duelAt);
        _room.Step(lobbyAt);

        // Act
        _room.SetReady(_b, true);
        var state = StateAt(lobbyAt + 50);

        // Assert
        Assert.True(Find(state, _b).Ready);
    }

    [Fact]
    public void Step_NewRoundAfterLobby_FullRevolverAgain()
    {
        // Arrange
        double duelAt = StartDuel();
        double lobbyAt = AWins(duelAt); // a는 첫 판에 한 발 씀
        _room.Step(lobbyAt);
        _room.SetReady(_a, true);
        _room.SetReady(_b, true);
        double walkAt = ScheduleWalk(_room, lobbyAt + 50);
        var (_, secondDuelAt) = StepUntilDuelAnnounced(_room, walkAt);
        _room.Step(secondDuelAt);

        // Act & Assert
        // 새 판에는 다시 한 발 (컨셉 기획서 규칙). 아무도 안 맞게 바깥으로 쏨
        Assert.Single(_room.Fire(_a, 0, 0, secondDuelAt, secondDuelAt + 20));
        double next = secondDuelAt + Revolver.FireInterval;
        Assert.Empty(_room.Fire(_a, 0, 0, next, next + 20));
    }

    [Fact]
    public void Step_EveryoneOutOfAmmo_ResultRematchAfterLastShotJudged()
    {
        // Arrange
        double duelAt = StartDuel();
        _room.Fire(_a, 0, 0, duelAt, duelAt + 20); // a는 +x 쪽 바깥으로 쏨
        double last = duelAt + 50;
        _room.Fire(_b, Math.PI, 0, last, last + 20); // b는 -x 쪽 바깥으로 쏨

        // Act
        var waiting = _room.Step(last + ShotJudge.MaxDelay - 1);
        double now = last + ShotJudge.MaxDelay;
        var messages = _room.Step(now);

        // Assert
        // 판정하지 않은 마지막 발사가 누군가를 맞힐 수 있어 그 발사를 판정한 틱에 정함.
        // 둘 다 살았는데 모두 쏘았으니 다음 라운드, 각자 한 발. 다음 라운드 카운트다운은 결과 시간 뒤 (기술 설계서 메시지 result).
        // 결과보다 먼저 b에게 빗나갔다고 알림
        Assert.Empty(waiting.OfType<ResultMessage>());
        Assert.Collection(
            messages,
            m => Assert.Equal(_b, Assert.IsType<Direct>(m).To),
            m => AssertResult(Assert.IsType<ResultMessage>(m), Outcome.Rematch, [_a, _b], 1),
            m => AssertPhase(m, RoundPhase.Countdown, now + RoundFlow.ResultDuration),
            m => Assert.IsType<StateMessage>(m));
    }

    [Fact]
    public void Step_RematchCountdown_RematchersBackOnCircleOthersStayDown()
    {
        // Arrange
        double countdownAt = RematchAAndBWithCDown();
        var downBefore = Find(StateAt(countdownAt - 50), _c);

        // Act
        var state = StateAt(countdownAt);

        // Assert
        // 재대결하는 둘만 그 인원수로 시작 배치에 다시 서서 마주 봄. c는 쓰러진 자리에 그대로 (기술 설계서 결투장 다시 서기)
        Guid[] rematchers = [_a, _b];
        for (int i = 0; i < rematchers.Length; i++)
        {
            var spot = StartPlacement.At(rematchers.Length, i, CenterX, CenterY);
            var player = Find(state, rematchers[i]);
            AssertStandsAt(spot, player);
            Assert.Equal(spot.Angle, player.Angle, Tolerance);
            Assert.Equal(Pose.Stand, player.Pose);
        }
        var down = Find(state, _c);
        Assert.Equal(Pose.Down, down.Pose);
        AssertSamePlace(downBefore, down);
    }

    [Fact]
    public void Step_RematchWalk_DownPlayerStaysPut()
    {
        // Arrange
        double countdownAt = RematchAAndBWithCDown();
        var downBefore = Find(StateAt(countdownAt), _c);
        double walkAt = countdownAt + 3 * RoundFlow.CountdownInterval;
        _room.Step(walkAt);

        // Act
        var state = StateAt(walkAt + RoundFlow.WalkDuration / 2);

        // Assert
        // 걷기는 재대결하는 사람만 함. 쓰러진 c는 돌지도 걷지도 일어서지도 않음
        AssertWalked(2, 0, RoundFlow.WalkDistance / 2, Find(state, _a));
        Assert.Equal(Pose.Walk, Find(state, _a).Pose);
        var down = Find(state, _c);
        Assert.Equal(Pose.Down, down.Pose);
        AssertSamePlace(downBefore, down);
        Assert.Equal(downBefore.Angle, down.Angle, Tolerance);
    }

    [Fact]
    public void Fire_InRematch_LoadedWithRounds()
    {
        // Arrange
        double countdownAt = RematchAAndBWithCDown();
        _room.Step(countdownAt);
        double walkAt = countdownAt + 3 * RoundFlow.CountdownInterval;
        var (_, duelAt) = StepUntilDuelAnnounced(_room, walkAt);
        _room.Step(duelAt);

        // Act & Assert
        // 다음 라운드에도 한 발만 장전됨 (컨셉 기획서 규칙 라운드마다 한 발). a는 +x 쪽 바깥으로 쏨
        Assert.Single(_room.Fire(_a, 0, 0, duelAt, duelAt + 20));
        double second = duelAt + Revolver.FireInterval;
        Assert.Empty(_room.Fire(_a, 0, 0, second, second + 20));
    }

    [Fact]
    public void RemovePlayer_InDuelLeavingOneAlive_NextStepAnnouncesWin()
    {
        // Arrange
        double duelAt = StartDuel();

        // Act
        _room.RemovePlayer(_b);
        var messages = _room.Step(duelAt + 50);
        var later = _room.Step(duelAt + 100);

        // Assert
        // 판 도중 나간 사람은 탈락으로 봄. 발사로 정해진 게 아니라 그다음 틱에 한 번만 알림 (기술 설계서 메시지 result)
        AssertResult(Assert.Single(messages.OfType<ResultMessage>()), Outcome.Win, [_a], 0);
        AssertAnnounces(messages, RoundPhase.Lobby, duelAt + 50 + RoundFlow.ResultDuration);
        Assert.Empty(later.OfType<ResultMessage>());
    }

    [Fact]
    public void RemovePlayer_AllAliveLeave_LatestDownWins()
    {
        // Arrange
        // 넷이 다 걸으면 a는 +x, b는 +y, c는 -x, d는 -y 쪽에 섬. a가 c를, 그다음 b가 d를 맞힘
        double duelAt = StartDuelWith(_a, _b, _c, _d);
        _room.Fire(_a, Math.PI, 0, duelAt, duelAt + 20);
        double second = duelAt + 2 * ShotJudge.MutualWindow;
        _room.Fire(_b, 3 * Math.PI / 2, 0, second, second + 20);
        _room.Step(second + ShotJudge.MaxDelay); // c와 d가 쓰러짐

        // Act
        // 살아 있던 a와 b가 같은 틱 안에 나감
        _room.RemovePlayer(_a);
        _room.RemovePlayer(_b);
        double now = second + ShotJudge.MaxDelay + ShotJudge.MutualWindow;
        var messages = _room.Step(now);

        // Assert
        // c는 d보다 D 넘게 먼저 쓰러져 함께 쓰러진 것이 아님. 가장 늦게 쓰러진 d 혼자라 재대결할 수 없음. 상대가 모두 기권한 것으로 보고 d가 이김
        AssertResult(Assert.Single(messages.OfType<ResultMessage>()), Outcome.Win, [_d], 0);
        AssertAnnounces(messages, RoundPhase.Lobby, now + RoundFlow.ResultDuration);
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

    private static void AssertPhase(object message, RoundPhase phase, double at)
    {
        var announced = Assert.IsType<PhaseMessage>(message);
        Assert.Equal(phase, announced.Phase);
        Assert.Equal(at, announced.At, Tolerance);
    }

    private static void AssertResult(ResultMessage actual, Outcome outcome, Guid[] playerIds, int rounds)
    {
        Assert.Equal(outcome, actual.Outcome);
        Assert.Equal(playerIds, actual.PlayerIds);
        Assert.Equal(rounds, actual.Rounds);
    }

    private static void AssertSamePlace(PlayerState expected, PlayerState actual)
    {
        Assert.Equal(expected.X, actual.X, Tolerance);
        Assert.Equal(expected.Y, actual.Y, Tolerance);
    }

    // 모두 준비한 방을 from부터 카운트다운 시작까지 돌리고, 걷기가 시작하는 시각을 돌려줌. 그 시각의 틱은 부르지 않음
    private static double ScheduleWalk(Room room, double from = 1000)
    {
        room.Step(from); // 카운트다운이 정해짐
        double countdownAt = from + RoundFlow.Lead;
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
    private double StartDuel() => StartDuelWith(_a, _b);

    // 들어온 순서대로 준비시켜 결투 시각의 틱까지 돌리고 그 시각을 돌려줌. i번째 사람은 i번 자리에서 바깥을 보고 섬
    private double StartDuelWith(params Guid[] ids)
    {
        AddReady(ids);
        double walkAt = ScheduleWalk(_room);
        var (_, duelAt) = StepUntilDuelAnnounced(_room, walkAt);
        _room.Step(duelAt);
        return duelAt;
    }

    // 둘의 결투에서 a가 b를 맞혀 이기게 하고, 준비 중으로 돌아가는 시각을 돌려줌. 승리를 정하는 틱까지 부르고 그 시각의 틱은 부르지 않음
    private double AWins(double duelAt)
    {
        double hitAt = duelAt + 100;
        _room.Fire(_a, Math.PI, 0, hitAt, hitAt + 20);
        double now = hitAt + ShotJudge.MaxDelay + ShotJudge.MutualWindow;
        _room.Step(now);
        return now + RoundFlow.ResultDuration;
    }

    // 셋이 결투해 a가 c를 맞히고 b는 빗나가, 살아남은 a와 b가 다음 라운드로 가게 함. 다음 라운드 카운트다운 시각을 돌려주고 그 시각의 틱은 부르지 않음.
    // 다 걸으면 a는 0번(+x 쪽), b는 1번(2π/3 쪽), c는 2번(4π/3 쪽) 자리에서 바깥을 봄
    private double RematchAAndBWithCDown()
    {
        double duelAt = StartDuelWith(_a, _b, _c);
        _room.Fire(_a, AToC, 0, duelAt, duelAt + 20); // c를 맞힘
        _room.Fire(_b, 2 * Math.PI / 3, 0, duelAt, duelAt + 20); // 바깥으로 쏨
        double now = duelAt + ShotJudge.MaxDelay + ShotJudge.MutualWindow;
        _room.Step(now); // c가 맞고 W + D가 지나 다음 라운드가 정해짐
        return now + RoundFlow.ResultDuration;
    }
}
