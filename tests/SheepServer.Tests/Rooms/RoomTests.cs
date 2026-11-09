namespace SheepServer.Tests.Rooms;

using SheepServer.Arena;
using SheepServer.Rooms;
using SheepServer.Rounds;
using SheepServer.Shooting;

public class RoomTests
{
    private const double Tolerance = 1e-9;

    // 셋이 다 걸었을 때 a(0번, +x 쪽)에서 b(1번)와 c(2번)를 겨누는 각도, b에서 a와 c를, c에서 b를 겨누는 각도.
    // 셋은 중심에서 같은 거리에 120°씩 떨어져 있어 서로 정삼각형을 이룸
    private const double AToB = 5 * Math.PI / 6;
    private const double AToC = 7 * Math.PI / 6;
    private const double BToA = 11 * Math.PI / 6;
    private const double BToC = 3 * Math.PI / 2;
    private const double CToB = Math.PI / 2;
    // 넷이 다 걸었을 때 b(1번, +y 쪽)에서 a(0번, +x 쪽)를, c(2번, -x 쪽)에서 d(3번, -y 쪽)를, d에서 c를 겨누는 각도.
    // 넷은 정사각형을 이뤄 이웃끼리 √2(R + 걸은 거리)만큼 떨어짐
    private const double SquareBToA = 7 * Math.PI / 4;
    private const double SquareCToD = 7 * Math.PI / 4;
    private const double SquareDToC = 3 * Math.PI / 4;
    private static readonly double CenterX = ArenaMap.Fixed.Width / 2.0;
    private static readonly double CenterY = ArenaMap.Fixed.Height / 2.0;

    // 다 걸었을 때 머리 가운데를 겨누는 위아래 각도. 둘이면 중심을 사이에 두고 2(R + 걸은 거리), 셋이면 정삼각형 한 변만큼 떨어짐.
    // 위아래 0으로 쏘면 몸에 맞아 반샷이고, 이 각도면 머리에 맞아 바로 쓰러짐 (컨셉 기획서 규칙 머리는 즉사, 몸은 반샷)
    private static readonly double HeadAcross = HeadPitch(2 * (StartPlacement.Radius + RoundFlow.WalkDistance));
    private static readonly double HeadInTriangle = HeadPitch(Math.Sqrt(3) * (StartPlacement.Radius + RoundFlow.WalkDistance));
    private static readonly double HeadInSquare = HeadPitch(Math.Sqrt(2) * (StartPlacement.Radius + RoundFlow.WalkDistance));

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

    // 기술 설계서 메시지 turn: pitch는 위아래 시선. 라디안, 위가 +. -0.5 이상 0.5 이하 (시스템 기획서 조작 위아래 보기)
    [Theory]
    [InlineData(0.3)]
    [InlineData(Room.MaxPitch)]
    [InlineData(-Room.MaxPitch)]
    public void SetAngle_Pitch_InState(double pitch)
    {
        // Arrange
        _room.AddPlayer(_a);

        // Act
        _room.SetAngle(_a, 1.0, pitch);
        var state = StateAt(0);

        // Assert
        // 관전하는 사람이 그 사람의 화면을 위아래 시선까지 그리는 데 씀 (기술 설계서 메시지 state)
        Assert.Equal(pitch, Find(state, _a).Pitch, Tolerance);
    }

    [Fact]
    public void SetAngle_NoPitch_KeepsPitch()
    {
        // Arrange
        _room.AddPlayer(_a);
        _room.SetAngle(_a, 1.0, 0.3);

        // Act
        _room.SetAngle(_a, 2.0);
        var state = StateAt(0);

        // Assert
        // pitch를 보내지 않는 클라도 돌 수 있음. 그때 위아래 시선은 그대로 둠
        Assert.Equal(2.0, Find(state, _a).Angle, Tolerance);
        Assert.Equal(0.3, Find(state, _a).Pitch, Tolerance);
    }

    [Theory]
    [InlineData(0.6)]
    [InlineData(-0.6)]
    [InlineData(double.NaN)]
    public void SetAngle_PitchOutOfRange_AngleKept(double pitch)
    {
        // Arrange
        _room.AddPlayer(_a);
        _room.SetAngle(_a, 1.0, 0.3);

        // Act
        _room.SetAngle(_a, 2.0, pitch);
        var state = StateAt(0);

        // Assert
        // 위아래 시선만 버리고 돌기는 받음 (기술 설계서 메시지 turn)
        Assert.Equal(2.0, Find(state, _a).Angle, Tolerance);
        Assert.Equal(0.3, Find(state, _a).Pitch, Tolerance);
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
    public void AddPlayer_ColorsInJoinOrder()
    {
        // Arrange
        _room.AddPlayer(_a);
        _room.AddPlayer(_b);
        _room.AddPlayer(_c);

        // Act
        var state = StateAt(0);

        // Assert
        // 컨셉 기획서 사람 구별: 들어온 순서대로 8가지 색 중 하나를 받음
        Assert.Equal(0, Find(state, _a).Color);
        Assert.Equal(1, Find(state, _b).Color);
        Assert.Equal(2, Find(state, _c).Color);
    }

    [Fact]
    public void AddPlayer_LeaverColorFreed_NextJoinerTakesLowest()
    {
        // Arrange
        _room.AddPlayer(_a);
        _room.AddPlayer(_b);
        _room.AddPlayer(_c);
        _room.RemovePlayer(_b);

        // Act
        _room.AddPlayer(_d);
        var state = StateAt(0);

        // Assert
        // 나간 사람의 색은 비워 두고, 다음 사람이 비어 있는 가장 앞 색을 받음. 남은 사람의 색은 그대로
        Assert.Equal(0, Find(state, _a).Color);
        Assert.Equal(2, Find(state, _c).Color);
        Assert.Equal(1, Find(state, _d).Color);
    }

    [Fact]
    public void RemovePlayer_DuringRound_OthersKeepColor()
    {
        // Arrange
        AddReady(_a, _b, _c);
        ScheduleWalk(_room);

        // Act
        _room.RemovePlayer(_a);
        var state = StateAt(2000);

        // Assert
        Assert.Equal(1, Find(state, _b).Color);
        Assert.Equal(2, Find(state, _c).Color);
    }

    [Fact]
    public void AddPlayer_Nickname_InState()
    {
        // Arrange
        _room.AddPlayer(_a, null, "보안관");

        // Act
        var state = StateAt(0);

        // Assert
        Assert.Equal("보안관", Find(state, _a).Nickname);
    }

    [Fact]
    public void AddPlayer_Character_InState()
    {
        // Arrange
        int last = Character.Count - 1;
        _room.AddPlayer(_a, null, "보안관", last);
        _room.AddPlayer(_b, null, "떠돌이");

        // Act
        var state = StateAt(0);

        // Assert
        // 방에 들어갈 때 고른 캐릭터. 보내지 않으면 0번 (기술 설계서 메시지 createRoom character)
        Assert.Equal(last, Find(state, _a).Character);
        Assert.Equal(0, Find(state, _b).Character);
    }

    [Fact]
    public void AddPlayer_NoNickname_Placeholder()
    {
        // Arrange
        // 첫 화면(닉네임 칸)이 생기기 전의 클라는 닉네임을 보내지 않음
        _room.AddPlayer(_a);

        // Act
        var state = StateAt(0);

        // Assert
        Assert.Equal(Nickname.Placeholder, Find(state, _a).Nickname);
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
    public void SetAngle_DuringWalk_PitchIgnored()
    {
        // Arrange
        AddReady(_a, _b);
        double walkAt = ScheduleWalk(_room);
        _room.Step(walkAt);

        // Act
        _room.SetAngle(_a, 1.0, 0.3);
        var state = StateAt(walkAt + 100);

        // Assert
        // 카운트다운부터 "시작!" 전까지는 돌기와 같이 버림 (기술 설계서 메시지 turn)
        Assert.Equal(0, Find(state, _a).Pitch);
    }

    [Fact]
    public void Stop_DuringWalk_StaysAtStopDistance()
    {
        // Arrange
        AddReady(_a, _b);
        double walkAt = ScheduleWalk(_room);
        _room.Step(walkAt);

        // Act
        _room.Stop(_a, walkAt + 1000, walkAt + 1010);
        var atEnd = StateAt(walkAt + RoundFlow.WalkDuration);
        var later = StateAt(walkAt + RoundFlow.WalkDuration + 1000);

        // Assert
        // 걷기 1초에 멈춤. 걷는 시간의 3분의 1이라 최대 걸음의 3분의 1에 섬. 멈추지 않은 b는 끝까지 걸음
        AssertWalked(2, 0, RoundFlow.WalkDistance / 3, Find(atEnd, _a));
        AssertWalked(2, 0, RoundFlow.WalkDistance / 3, Find(later, _a));
        AssertFacesOutward(Find(later, _a));
        AssertWalked(2, 1, RoundFlow.WalkDistance, Find(later, _b));
    }

    [Fact]
    public void Stop_StoppedPlayer_PoseStand()
    {
        // Arrange
        AddReady(_a, _b);
        double walkAt = ScheduleWalk(_room);
        _room.Step(walkAt);

        // Act
        _room.Stop(_a, walkAt + 1000, walkAt + 1010);
        var state = StateAt(walkAt + 1500);

        // Assert
        // 멈춘 사람은 걷기 단계에도 서 있음. 그래서 다른 사람 화면에서 발소리가 그침 (기술 설계서 메시지 state)
        Assert.Equal(Pose.Stand, Find(state, _a).Pose);
        Assert.Equal(Pose.Walk, Find(state, _b).Pose);
    }

    [Fact]
    public void Stop_BeforeWalk_Ignored()
    {
        // Arrange
        AddReady(_a, _b);
        double walkAt = ScheduleWalk(_room); // 카운트다운 중

        // Act
        _room.Stop(_a, walkAt - 100, walkAt - 100);
        _room.Step(walkAt);
        var state = StateAt(walkAt + RoundFlow.WalkDuration);

        // Assert
        AssertWalked(2, 0, RoundFlow.WalkDistance, Find(state, _a));
    }

    [Fact]
    public void Stop_AfterWalkEnds_Ignored()
    {
        // Arrange
        AddReady(_a, _b);
        double walkAt = ScheduleWalk(_room);
        double walkEnd = walkAt + RoundFlow.WalkDuration;
        _room.Step(walkAt);

        // Act
        // 걷기가 끝나는 시각에 도착함. 쏜 시각처럼 at을 앞당겨 주지 않고 도착 시각으로 가름
        _room.Stop(_a, walkEnd - 20, walkEnd);
        var state = StateAt(walkEnd + 50);

        // Assert
        AssertWalked(2, 0, RoundFlow.WalkDistance, Find(state, _a));
    }

    [Fact]
    public void Stop_Twice_FirstWins()
    {
        // Arrange
        AddReady(_a, _b);
        double walkAt = ScheduleWalk(_room);
        _room.Step(walkAt);

        // Act
        _room.Stop(_a, walkAt + 1000, walkAt + 1010);
        _room.Stop(_a, walkAt + 2000, walkAt + 2010);
        var state = StateAt(walkAt + RoundFlow.WalkDuration);

        // Assert
        // 한 번 멈추면 다시 걷지 않음. 두 번째 stop으로 더 가지도 않음
        AssertWalked(2, 0, RoundFlow.WalkDistance / 3, Find(state, _a));
    }

    [Fact]
    public void Stop_AtLaterThanArrival_ClampedToArrival()
    {
        // Arrange
        AddReady(_a, _b);
        double walkAt = ScheduleWalk(_room);
        _room.Step(walkAt);

        // Act
        _room.Stop(_a, walkAt + 2500, walkAt + 1500);
        var state = StateAt(walkAt + RoundFlow.WalkDuration);

        // Assert
        // 도착보다 늦게 누를 수는 없어 도착 시각에 멈춘 것으로 봄 (fire의 쏜 시각과 같음)
        AssertWalked(2, 0, RoundFlow.WalkDistance * 1500 / RoundFlow.WalkDuration, Find(state, _a));
    }

    [Fact]
    public void Stop_AtMoreThanMaxDelayBeforeArrival_Clamped()
    {
        // Arrange
        AddReady(_a, _b);
        double walkAt = ScheduleWalk(_room);
        _room.Step(walkAt);

        // Act
        _room.Stop(_a, walkAt + 500, walkAt + 1500);
        var state = StateAt(walkAt + RoundFlow.WalkDuration);

        // Assert
        // 지연 보정은 W까지만 해 줌. 그래서 도착 시각 - W에 멈춘 것으로 봄
        AssertWalked(2, 0, RoundFlow.WalkDistance * (1500 - ShotJudge.MaxDelay) / RoundFlow.WalkDuration, Find(state, _a));
    }

    [Fact]
    public void Stop_AtBeforeWalkStart_StaysAtStart()
    {
        // Arrange
        AddReady(_a, _b);
        double walkAt = ScheduleWalk(_room);
        _room.Step(walkAt);

        // Act
        _room.Stop(_a, walkAt - 100, walkAt + 50);
        var state = StateAt(walkAt + RoundFlow.WalkDuration);

        // Assert
        // 걷기 전에 누른 것은 걷기 시각에 누른 것으로 봄. 한 걸음도 걷지 않음
        AssertWalked(2, 0, 0, Find(state, _a));
    }

    [Fact]
    public void Stop_BeforeWalkStartTick_Applied()
    {
        // Arrange
        AddReady(_a, _b);
        double walkAt = ScheduleWalk(_room);

        // Act
        // 걷기 시각은 지났지만 그 뒤 첫 틱이 아직 오지 않았을 때 도착함
        _room.Stop(_a, walkAt + 10, walkAt + 20);
        _room.Step(walkAt + 40);
        var state = StateAt(walkAt + RoundFlow.WalkDuration);

        // Assert
        AssertWalked(2, 0, RoundFlow.WalkDistance * 10 / RoundFlow.WalkDuration, Find(state, _a));
    }

    [Fact]
    public void Stop_PlayerNotInRoom_Ignored()
    {
        // Arrange
        AddReady(_a, _b);
        double walkAt = ScheduleWalk(_room);
        _room.Step(walkAt);

        // Act
        _room.Stop(Guid.NewGuid(), walkAt + 1000, walkAt + 1010);
        var state = StateAt(walkAt + RoundFlow.WalkDuration);

        // Assert
        AssertWalked(2, 0, RoundFlow.WalkDistance, Find(state, _a));
        AssertWalked(2, 1, RoundFlow.WalkDistance, Find(state, _b));
    }

    [Fact]
    public void Stop_NextRound_WalksAgainFully()
    {
        // Arrange
        // 첫 라운드에 a가 걷다 멈추고, 아무도 쏘지 않아 둘 다 시간 초과로 쓰러져 다음 라운드로 감
        AddReady(_a, _b);
        double walkAt = ScheduleWalk(_room);
        _room.Step(walkAt);
        _room.Stop(_a, walkAt + 1000, walkAt + 1010);
        var (_, duelAt) = StepUntilDuelAnnounced(_room, walkAt + 50);
        _room.Step(duelAt);
        double deadline = duelAt + RoundFlow.DuelLimit;
        _room.Step(deadline + ShotJudge.MaxDelay);
        double decidedAt = deadline + ShotJudge.MaxDelay + ShotJudge.MutualWindow;
        _room.Step(decidedAt);
        double countdownAt = decidedAt + RoundFlow.ResultDuration;
        _room.Step(countdownAt);
        double nextWalkAt = countdownAt + 3 * RoundFlow.CountdownInterval;
        _room.Step(nextWalkAt);

        // Act
        var state = StateAt(nextWalkAt + RoundFlow.WalkDuration);

        // Assert
        // 멈춘 자리는 그 라운드에만 씀. 다음 라운드에는 다시 최대 걸음까지 걸음
        AssertWalked(2, 0, RoundFlow.WalkDistance, Find(state, _a));
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
        _room.Fire(_a, AToB, HeadInTriangle, duelAt + 100, duelAt + 120);

        // Act
        var messages = _room.Step(duelAt + 100 + ShotJudge.MaxDelay);

        // Assert
        // 명중을 정한 틱에 쏜 사람에게만 사격 결과를, 모두에게 탈락을, 쓰러진 사람에게만 쓰러뜨린 사람을 알리고, 같은 틱의 state부터 쓰러짐.
        // 둘이 남고 c에게 총알이 있어 결과는 없음
        Assert.Collection(
            messages,
            m => Assert.Equal(_a, Assert.IsType<Direct>(m).To),
            m =>
            {
                var eliminated = Assert.IsType<EliminatedMessage>(m);
                Assert.Equal(_b, eliminated.PlayerId);
                Assert.Equal(EliminationReason.Shot, eliminated.Reason);
            },
            m => Assert.Equal(_b, Assert.IsType<Direct>(m).To),
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
        _room.Fire(_b, 0, HeadAcross, bShot, bShot + 10); // b가 a를 겨눠 쏨. 지연이 작아 먼저 도착함
        _room.Fire(_a, Math.PI, HeadAcross, aShot, aShot + ShotJudge.MaxDelay); // a는 D보다 더 먼저 쐈지만 지연이 W만큼 커 늦게 도착함

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
        _room.Fire(_a, Math.PI, HeadAcross, aShot, aShot + 20);
        _room.Step(aShot + ShotJudge.MaxDelay); // b가 쓰러졌다고 정함

        // Act
        // b의 발사는 b가 쓰러진 뒤에 도착했지만 맞은 시각에서 D 안에 쐈음 (기술 설계서 명중 판정 함께 쓰러짐)
        var fired = _room.Fire(_b, 0, HeadAcross, bShot, aShot + ShotJudge.MaxDelay + 20);
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
        _room.Fire(_b, 0, HeadAcross, duelAt + 150, duelAt + 160);
        _room.Fire(_a, Math.PI, HeadAcross, duelAt, duelAt + 500); // a는 결투 시각에 쐈다지만 500ms 걸려 옴. 쏜 시각은 duelAt + 300으로 잘림

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
        _room.Fire(_a, Math.PI, HeadAcross, duelAt + 1000, duelAt + 20); // 도착보다 늦게 쐈다는 값. 쏜 시각은 duelAt + 20으로 잘림
        _room.Fire(_b, 0, HeadAcross, duelAt + 200, duelAt + 210);

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
        _room.Fire(_a, AToB, HeadInTriangle, hitAt, hitAt + 20);
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
        _room.Fire(_a, AToB, HeadInTriangle, duelAt + 100, duelAt + 120);
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
        _room.Fire(_a, AToB, HeadInTriangle, duelAt + 100, duelAt + 120);

        // Act
        var messages = _room.Step(duelAt + 100 + ShotJudge.MaxDelay);

        // Assert
        // 사격 결과는 쏜 사람 a에게만 감. 머리 가운데 높이에 맞아 바로 쓰러짐. 탈락은 지금처럼 모두에게 감 (기술 설계서 메시지 shotResult)
        var direct = Assert.Single(messages.OfType<Direct>(), d => d.Message is ShotResultMessage);
        Assert.Equal(_a, direct.To);
        var result = Assert.IsType<ShotResultMessage>(direct.Message);
        Assert.Equal(HitZone.Head, result.Zone);
        Assert.Equal((HitScan.BodyTop + HitScan.HeadTop) / 2, result.Height!.Value, Tolerance);
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
    public void Step_BodyHit_TargetGetsWoundedOnly_NoEliminated()
    {
        // Arrange
        double duelAt = StartDuel();
        _room.Fire(_a, Math.PI, 0, duelAt + 100, duelAt + 120); // b의 몸을 맞힘

        // Act
        var messages = _room.Step(duelAt + 100 + ShotJudge.MaxDelay);

        // Assert
        // 쏜 a에게는 사격 결과(몸, 안 쓰러짐)를, 맞은 b에게만 반샷을 알림. 탈락은 없고 b는 서 있음 (기술 설계서 메시지 wounded)
        Assert.Collection(
            messages,
            m =>
            {
                var direct = Assert.IsType<Direct>(m);
                Assert.Equal(_a, direct.To);
                var result = Assert.IsType<ShotResultMessage>(direct.Message);
                Assert.Equal(HitZone.Body, result.Zone);
                Assert.False(result.Down);
            },
            m =>
            {
                var direct = Assert.IsType<Direct>(m);
                Assert.Equal(_b, direct.To);
                Assert.IsType<WoundedMessage>(direct.Message);
            },
            m => Assert.Equal(Pose.Stand, Find(Assert.IsType<StateMessage>(m), _b).Pose));
    }

    [Fact]
    public void Step_WoundedSurvivor_KeepsWound()
    {
        // Arrange
        // 첫 라운드: a가 b의 몸을 맞히고 b는 -x 쪽 바깥으로 쏨. 둘 다 살아 다음 라운드로 감
        double duelAt = StartDuel();
        _room.Fire(_a, Math.PI, 0, duelAt, duelAt + 20);
        _room.Fire(_b, Math.PI, 0, duelAt, duelAt + 20);
        double now = duelAt + ShotJudge.MaxDelay;
        _room.Step(now);
        double next = StartNextRound(now + RoundFlow.ResultDuration);

        // Act
        // 다음 라운드에 a가 b의 몸을 다시 맞힘
        _room.Fire(_a, Math.PI, 0, next + 100, next + 120);
        var messages = _room.Step(next + 100 + ShotJudge.MaxDelay);

        // Assert
        // 반샷은 판이 끝날 때까지 남아 두 번째 몸 명중에 쓰러짐 (컨셉 기획서 규칙)
        Assert.Equal(_b, Assert.Single(messages.OfType<EliminatedMessage>()).PlayerId);
        Assert.DoesNotContain(messages.OfType<Direct>(), d => d.Message is WoundedMessage);
    }

    [Fact]
    public void Step_CoFallenRevived_WoundsRevertToRoundStart()
    {
        // Arrange
        // c와 a가 차례로 b의 몸을 맞혀 b가 쓰러지고, b는 쓰러진 뒤 D 안에 a의 머리를 맞힘.
        // 살아남은 c가 나가 아무도 안 남고, 함께 쓰러진 a와 b가 다음 라운드로 감
        double duelAt = StartDuelWith(_a, _b, _c);
        _room.Fire(_c, CToB, 0, duelAt + 100, duelAt + 120);
        _room.Fire(_a, AToB, 0, duelAt + 150, duelAt + 170);
        _room.Fire(_b, BToA, HeadInTriangle, duelAt + 200, duelAt + 220);
        var hit = _room.Step(duelAt + 200 + ShotJudge.MaxDelay);
        _room.RemovePlayer(_c);
        double decided = duelAt + 200 + ShotJudge.MaxDelay + ShotJudge.MutualWindow;
        var result = _room.Step(decided);
        double next = StartNextRound(decided + RoundFlow.ResultDuration);

        // Act
        // 다음 라운드에 a가 b의 몸을 맞힘
        _room.Fire(_a, Math.PI, 0, next + 100, next + 120);
        var messages = _room.Step(next + 100 + ShotJudge.MaxDelay);

        // Assert
        // b가 그 라운드에 맞은 반샷 둘은 라운드 전(0)으로 되돌아가, 이번 몸 명중은 반샷 하나임 (컨셉 기획서 규칙 함께 쓰러짐)
        Assert.Equal(new[] { _b, _a }, hit.OfType<EliminatedMessage>().Select(m => m.PlayerId));
        AssertResult(Assert.Single(result.OfType<ResultMessage>()), Outcome.Rematch, [_a, _b], 1);
        Assert.Empty(messages.OfType<EliminatedMessage>());
        Assert.Equal(_b, Assert.Single(messages.OfType<Direct>(), d => d.Message is WoundedMessage).To);
    }

    [Fact]
    public void Step_CoFallenRevived_KeepsWoundFromEarlierRound()
    {
        // Arrange
        // 첫 라운드: a가 b의 몸을 맞히고 b는 빗나감. b는 반샷 하나를 안고 둘째 라운드를 시작함
        double duelAt = StartDuel();
        _room.Fire(_a, Math.PI, 0, duelAt, duelAt + 20);
        _room.Fire(_b, Math.PI, 0, duelAt, duelAt + 20);
        double now = duelAt + ShotJudge.MaxDelay;
        _room.Step(now);

        // 둘째 라운드: a가 b의 몸을 맞혀 쓰러뜨리고, b는 D 안에 a의 머리를 맞혀 함께 쓰러짐
        double second = StartNextRound(now + RoundFlow.ResultDuration);
        _room.Fire(_a, Math.PI, 0, second, second + 20);
        _room.Fire(_b, 0, HeadAcross, second + 50, second + 70);
        double decided = second + 50 + ShotJudge.MaxDelay + ShotJudge.MutualWindow;
        var result = _room.Step(decided);
        double third = StartNextRound(decided + RoundFlow.ResultDuration);

        // Act
        _room.Fire(_a, Math.PI, 0, third, third + 20);
        var messages = _room.Step(third + ShotJudge.MaxDelay);

        // Assert
        // 되돌리는 값은 0이 아니라 둘째 라운드가 시작할 때의 반샷 하나임. 그래서 몸을 다시 맞으면 쓰러짐
        AssertResult(Assert.Single(result.OfType<ResultMessage>()), Outcome.Rematch, [_a, _b], 1);
        Assert.Equal(_b, Assert.Single(messages.OfType<EliminatedMessage>()).PlayerId);
    }

    [Fact]
    public void Step_GameEnds_WoundsReset()
    {
        // Arrange
        // 첫 라운드: a가 b의 몸을 맞히고 b는 빗나감. 둘째 라운드: b가 a의 머리를 맞혀 반샷을 안은 채 이김
        double duelAt = StartDuel();
        _room.Fire(_a, Math.PI, 0, duelAt, duelAt + 20);
        _room.Fire(_b, Math.PI, 0, duelAt, duelAt + 20);
        double now = duelAt + ShotJudge.MaxDelay;
        _room.Step(now);
        double second = StartNextRound(now + RoundFlow.ResultDuration);
        _room.Fire(_b, 0, HeadAcross, second, second + 20);
        double won = second + ShotJudge.MaxDelay + ShotJudge.MutualWindow;
        _room.Step(won);
        double lobbyAt = won + RoundFlow.ResultDuration;
        _room.Step(lobbyAt);
        _room.SetReady(_a, true);
        _room.SetReady(_b, true);
        var (_, newGameAt) = StepUntilDuelAnnounced(_room, ScheduleWalk(_room, lobbyAt + 50));
        _room.Step(newGameAt);

        // Act
        // 새 판에서 a가 b의 몸을 맞힘
        _room.Fire(_a, Math.PI, 0, newGameAt + 100, newGameAt + 120);
        var messages = _room.Step(newGameAt + 100 + ShotJudge.MaxDelay);

        // Assert
        // 판이 끝나면 반샷이 모두 사라져 새 판의 첫 몸 명중은 반샷 하나임
        Assert.Empty(messages.OfType<EliminatedMessage>());
        Assert.Equal(_b, Assert.Single(messages.OfType<Direct>(), d => d.Message is WoundedMessage).To);
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
        _room.Fire(_a, Math.PI, HeadAcross, hitAt, hitAt + 20);
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
    public void Step_Win_WinnerWinsUp()
    {
        // Arrange
        double duelAt = StartDuel();
        double hitAt = duelAt + 100;
        _room.Fire(_a, Math.PI, HeadAcross, hitAt, hitAt + 20);
        var hit = StateAt(hitAt + ShotJudge.MaxDelay);

        // Act
        var decided = StateAt(hitAt + ShotJudge.MaxDelay + ShotJudge.MutualWindow);

        // Assert
        // 승리를 정한 틱의 state부터 이긴 사람만 1 오름. 클라는 이 값 × $100을 현상금으로 적음 (시스템 기획서 방 화면 현상금)
        Assert.Equal(0, Find(hit, _a).Wins);
        Assert.Equal(1, Find(decided, _a).Wins);
        Assert.Equal(0, Find(decided, _b).Wins);
    }

    [Fact]
    public void Step_Rematch_WinsUnchanged()
    {
        // Arrange
        double duelAt = StartDuel();
        double shotAt = duelAt + 100;
        _room.Fire(_a, Math.PI, HeadAcross, shotAt, shotAt + 20);
        _room.Fire(_b, 0, HeadAcross, shotAt, shotAt + 20);
        _room.Step(shotAt + ShotJudge.MaxDelay);

        // Act
        var messages = _room.Step(shotAt + ShotJudge.MaxDelay + ShotJudge.MutualWindow);

        // Assert
        // 함께 쓰러져 다음 라운드로 감. 판이 끝나지 않아 아무도 오르지 않음
        AssertResult(Assert.Single(messages.OfType<ResultMessage>()), Outcome.Rematch, [_a, _b], 1);
        var state = messages.OfType<StateMessage>().Single();
        Assert.Equal(0, Find(state, _a).Wins);
        Assert.Equal(0, Find(state, _b).Wins);
    }

    [Fact]
    public void RemovePlayer_WinnerRejoins_WinsBackToZero()
    {
        // Arrange
        double duelAt = StartDuel();
        double hitAt = duelAt + 100;
        _room.Fire(_a, Math.PI, HeadAcross, hitAt, hitAt + 20);
        _room.Step(hitAt + ShotJudge.MaxDelay);
        double decidedAt = hitAt + ShotJudge.MaxDelay + ShotJudge.MutualWindow;
        _room.Step(decidedAt);
        var lobby = StateAt(decidedAt + RoundFlow.ResultDuration); // 준비 중으로 돌아감

        // Act
        _room.RemovePlayer(_a);
        _room.AddPlayer(_a);
        var rejoined = StateAt(decidedAt + RoundFlow.ResultDuration + 50);

        // Assert
        // 판이 끝나도 방에 있는 동안은 남고, 나가면 사라짐. 계정이 없어 저장하지 않음 (시스템 기획서 방 화면 현상금)
        Assert.Equal(1, Find(lobby, _a).Wins);
        Assert.Equal(0, Find(rejoined, _a).Wins);
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
    public void Step_Lobby_PitchReset()
    {
        // Arrange
        double duelAt = StartDuel();
        _room.SetAngle(_a, Math.PI, 0.3);
        double lobbyAt = AWins(duelAt);
        var showingResult = StateAt(lobbyAt - 50);

        // Act
        var state = StateAt(lobbyAt);

        // Assert
        // 결과를 보는 동안은 그대로이고, 준비 중으로 돌아가 다시 서면 정면을 봄 (시스템 기획서 조작 위아래 보기)
        Assert.Equal(0.3, Find(showingResult, _a).Pitch, Tolerance);
        Assert.Equal(0, Find(state, _a).Pitch);
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
    public void Step_CountdownAfterTurnInLobby_EveryoneFacesCenter()
    {
        // Arrange
        double duelAt = StartDuel();
        double lobbyAt = AWins(duelAt);
        _room.Step(lobbyAt);
        _room.SetAngle(_a, 1.0, 0.3); // 판 끝 직전에 보내 준비 중에 늦게 온 turn
        _room.SetReady(_a, true);
        _room.SetReady(_b, true);
        _room.Step(lobbyAt + 50); // 카운트다운이 정해짐

        // Act
        var state = StateAt(lobbyAt + 50 + RoundFlow.Lead); // 카운트다운이 시작함

        // Assert
        // 장전할 때 모두 처음 자리에서 서로 마주 봄 (기술 설계서 결투장 다시 서기)
        AssertFacesCenterAtStart(state, _a, _b);
    }

    [Fact]
    public void Step_CountdownAfterThirdJoins_EveryoneFacesCenter()
    {
        // Arrange
        AddReady(_a, _b, _c); // c가 들어오면 a와 b는 셋의 자리로 옮겨 섬
        _room.Step(1000); // 카운트다운이 정해짐

        // Act
        var state = StateAt(1000 + RoundFlow.Lead); // 카운트다운이 시작함

        // Assert
        AssertFacesCenterAtStart(state, _a, _b, _c);
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
        _room.Fire(_a, Math.PI, HeadAcross, duelAt, duelAt + 20);
        double second = duelAt + 2 * ShotJudge.MutualWindow;
        _room.Fire(_b, 3 * Math.PI / 2, HeadAcross, second, second + 20);
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

    [Fact]
    public void Step_DeadlinePassed_UnfiredStandingPlayersTimeOut()
    {
        // Arrange
        // a만 바깥(+x 쪽)으로 쏘고 b와 c는 쏘지 않음
        double duelAt = StartDuelWith(_a, _b, _c);
        _room.Fire(_a, 0, 0, duelAt + 100, duelAt + 120);
        _room.Step(duelAt + 100 + ShotJudge.MaxDelay);
        double deadline = duelAt + RoundFlow.DuelLimit;

        // Act
        var messages = _room.Step(deadline + ShotJudge.MaxDelay);

        // Assert
        // 쏘지 않은 둘만 자살로 쓰러지고 모두에게 사유와 함께 알림. 같은 틱의 state부터 쓰러짐 (컨셉 기획서 규칙 제한 시간)
        var eliminated = messages.OfType<EliminatedMessage>().ToList();
        Assert.Equal(new[] { _b, _c }, eliminated.Select(m => m.PlayerId));
        Assert.All(eliminated, m => Assert.Equal(EliminationReason.Timeout, m.Reason));
        var state = messages.OfType<StateMessage>().Single();
        Assert.Equal(Pose.Stand, Find(state, _a).Pose);
        Assert.Equal(Pose.Down, Find(state, _b).Pose);
        Assert.Equal(Pose.Down, Find(state, _c).Pose);
    }

    [Fact]
    public void Step_BeforeDeadlinePlusW_NoTimeoutYet()
    {
        // Arrange
        double duelAt = StartDuel();
        double deadline = duelAt + RoundFlow.DuelLimit;

        // Act
        var messages = _room.Step(deadline + ShotJudge.MaxDelay - 1);

        // Assert
        // 제한 시각 직전에 쏜 발사가 W 안에 아직 올 수 있어, W가 지나기 전에는 아무도 쓰러뜨리지 않음
        Assert.Empty(messages.OfType<EliminatedMessage>());
        Assert.All(messages.OfType<StateMessage>().Single().Players, p => Assert.Equal(Pose.Stand, p.Pose));
    }

    [Fact]
    public void Step_FiredJustBeforeDeadline_JudgedNotTimedOut()
    {
        // Arrange
        // a는 제한 시각 10ms 전에 b의 몸을 겨눠 쏨. 지연 때문에 제한 시각이 지난 뒤에 도착함
        double duelAt = StartDuel();
        double deadline = duelAt + RoundFlow.DuelLimit;
        _room.Step(deadline);
        var fired = _room.Fire(_a, Math.PI, 0, deadline - 10, deadline + 50);

        // Act
        var messages = _room.Step(deadline + ShotJudge.MaxDelay);

        // Assert
        // 쏜 시각이 제한 시각 안이라 발사를 받아 판정함. a는 쐈으므로 시간 초과가 아니고, 쏘지 않은 b만 자살로 쓰러짐
        Assert.IsType<ShotMessage>(Assert.Single(fired));
        Assert.Equal(_a, Assert.Single(messages.OfType<Direct>(), d => d.Message is ShotResultMessage).To);
        var eliminated = Assert.Single(messages.OfType<EliminatedMessage>());
        Assert.Equal(_b, eliminated.PlayerId);
        Assert.Equal(EliminationReason.Timeout, eliminated.Reason);
        Assert.Equal(Pose.Stand, Find(messages.OfType<StateMessage>().Single(), _a).Pose);
    }

    [Fact]
    public void Fire_AfterDeadline_Ignored()
    {
        // Arrange
        double duelAt = StartDuel();
        double deadline = duelAt + RoundFlow.DuelLimit;
        _room.Step(deadline);

        // Act
        var messages = _room.Fire(_a, Math.PI, HeadAcross, deadline + 1, deadline + 20);

        // Assert
        Assert.Empty(messages); // 제한 시각 뒤에 쏜 발사는 버림. 그 사람은 W 뒤에 자살로 쓰러짐
    }

    [Fact]
    public void Fire_TimedOutPlayerAtDeadline_Ignored()
    {
        // Arrange
        double duelAt = StartDuel();
        double deadline = duelAt + RoundFlow.DuelLimit;
        _room.Step(deadline + ShotJudge.MaxDelay); // 둘 다 자살로 쓰러짐

        // Act
        // 제한 시각 바로 그때 쏜 발사가 시간 초과를 정한 틱 뒤에 도착함
        var messages = _room.Fire(_a, Math.PI, HeadAcross, deadline, deadline + ShotJudge.MaxDelay);

        // Assert
        // 자살에 한 발을 써서 남은 총알이 없음. 맞은 시각에서 D 안의 발사처럼 받아 주면 안 됨
        Assert.Empty(messages);
    }

    [Fact]
    public void Step_AllTimeOut_NextRoundAmongThem()
    {
        // Arrange
        double duelAt = StartDuel();
        double deadline = duelAt + RoundFlow.DuelLimit;

        // Act
        var timedOut = _room.Step(deadline + ShotJudge.MaxDelay);
        double now = deadline + ShotJudge.MaxDelay + ShotJudge.MutualWindow;
        var decided = _room.Step(now);
        var state = StateAt(now + RoundFlow.ResultDuration);

        // Assert
        // 아무도 쏘지 않아 둘 다 제한 시각에 쓰러짐. 함께 쓰러진 것이라 둘이 한 발씩 들고 다음 라운드로 감 (컨셉 기획서 규칙 함께 쓰러짐)
        Assert.Equal(new[] { _a, _b }, timedOut.OfType<EliminatedMessage>().Select(m => m.PlayerId));
        AssertResult(Assert.Single(decided.OfType<ResultMessage>()), Outcome.Rematch, [_a, _b], 1);
        AssertAnnounces(decided, RoundPhase.Countdown, now + RoundFlow.ResultDuration);
        Assert.All(state.Players, p => Assert.Equal(Pose.Stand, p.Pose));
    }

    [Fact]
    public void Step_MutualHitNearDeadline_CoFallenWithTimedOut()
    {
        // Arrange
        // 제한 시각 직전에 a와 b가 D 안에 서로 머리를 맞히고, c는 쏘지 않음
        double duelAt = StartDuelWith(_a, _b, _c);
        double deadline = duelAt + RoundFlow.DuelLimit;
        _room.Fire(_a, AToB, HeadInTriangle, deadline - 50, deadline - 30);
        _room.Fire(_b, BToA, HeadInTriangle, deadline - 40, deadline - 20);
        _room.Step(deadline - 40 + ShotJudge.MaxDelay); // a와 b가 쓰러짐

        // Act
        var timedOut = _room.Step(deadline + ShotJudge.MaxDelay);
        double now = deadline + ShotJudge.MaxDelay + ShotJudge.MutualWindow;
        var decided = _room.Step(now);

        // Assert
        // c는 제한 시각에 쓰러져 D 안에 쓰러진 a, b와 함께 쓰러진 것임. 셋이 다음 라운드로 감 (컨셉 기획서 규칙 함께 쓰러짐)
        Assert.Equal(_c, Assert.Single(timedOut.OfType<EliminatedMessage>()).PlayerId);
        AssertResult(Assert.Single(decided.OfType<ResultMessage>()), Outcome.Rematch, [_a, _b, _c], 1);
    }

    [Fact]
    public void Step_ResultShownPastDeadline_WinnerNotTimedOut()
    {
        // Arrange
        // b가 제한 시각 직전에 나가 a 혼자 남음. a는 총알을 든 채 이기고, 결과를 보는 동안 제한 시각이 지남
        double duelAt = StartDuel();
        double deadline = duelAt + RoundFlow.DuelLimit;
        _room.RemovePlayer(_b);
        _room.Step(deadline - 50); // 승리가 정해짐

        // Act
        var messages = _room.Step(deadline + ShotJudge.MaxDelay);

        // Assert
        // 결과가 정해지면 결투가 끝난 것이라 시간 초과를 보지 않음
        Assert.Empty(messages.OfType<EliminatedMessage>());
        Assert.Equal(Pose.Stand, Find(messages.OfType<StateMessage>().Single(), _a).Pose);
    }

    [Fact]
    public void Spectate_Down_ReturnsTargetHud()
    {
        // Arrange
        ADownByB(); // c는 반샷 하나에 아직 쏘지 않아 한 발이 남음

        // Act
        var spectating = _room.Spectate(_a, _c);

        // Assert
        // 관전을 시작할 때 그 사람의 혈흔과 실린더를 그대로 그림 (기술 설계서 메시지 spectating)
        Assert.NotNull(spectating);
        Assert.Equal(_c, spectating.PlayerId);
        Assert.Equal(1, spectating.Wounds);
        Assert.Equal(1, spectating.Ammo);
    }

    [Fact]
    public void Spectate_Standing_Ignored()
    {
        // Arrange
        StartDuel();

        // Act
        var spectating = _room.Spectate(_a, _b);

        // Assert
        // 서 있는 사람이 관전으로 누가 반샷인지 엿보지 못함 (컨셉 기획서 규칙)
        Assert.Null(spectating);
    }

    [Fact]
    public void Spectate_Lobby_Ignored()
    {
        // Arrange
        double duelAt = StartDuel();
        double lobbyAt = AWins(duelAt);
        _room.Step(lobbyAt); // 쓰러졌던 b도 준비 중으로 돌아가 다시 섬

        // Act
        var spectating = _room.Spectate(_b, _a);

        // Assert
        Assert.Null(spectating);
    }

    [Fact]
    public void Spectate_Self_Ignored()
    {
        // Arrange
        ADownByB();

        // Act
        var spectating = _room.Spectate(_a, _a);

        // Assert
        Assert.Null(spectating);
    }

    [Fact]
    public void Spectate_Unknown_Ignored()
    {
        // Arrange
        ADownByB();

        // Act
        var unknownTarget = _room.Spectate(_a, Guid.NewGuid());
        var unknownSpectator = _room.Spectate(Guid.NewGuid(), _c);

        // Assert
        Assert.Null(unknownTarget);
        Assert.Null(unknownSpectator);
    }

    [Fact]
    public void Step_DirectToTarget_CopiedToSpectatorInOrder()
    {
        // Arrange
        // 넷이 결투해 b가 a의 머리를 맞혀 쓰러뜨리고, 쓰러진 a가 c를 봄. 그다음 c와 d가 서로 몸을 맞힘
        double duelAt = StartDuelWith(_a, _b, _c, _d);
        _room.Fire(_b, SquareBToA, HeadInSquare, duelAt + 100, duelAt + 120);
        double downAt = duelAt + 100 + ShotJudge.MaxDelay;
        _room.Step(downAt);
        _room.Spectate(_a, _c);
        _room.Fire(_c, SquareCToD, 0, downAt + 100, downAt + 120);
        _room.Fire(_d, SquareDToC, 0, downAt + 150, downAt + 170);

        // Act
        var messages = _room.Step(downAt + 150 + ShotJudge.MaxDelay);

        // Assert
        // a는 c가 받는 것을 같은 순서로 받음: c의 사격 결과, c가 맞은 반샷, 그 밖에 모두에게 가는 것 (기술 설계서 메시지 spectate)
        var watched = TickLoop.MessagesFor(_c, messages).ToList();
        Assert.Equal(watched, TickLoop.MessagesFor(_a, messages));
        Assert.IsType<ShotResultMessage>(watched[0]);
        Assert.IsType<WoundedMessage>(watched[1]);
    }

    [Fact]
    public void Spectate_Null_StopsCopies()
    {
        // Arrange
        double now = ADownByB();
        _room.Spectate(_a, _c);

        // Act
        var stopped = _room.Spectate(_a, null);
        _room.Fire(_c, CToB, 0, now + 100, now + 120);
        var messages = _room.Step(now + 100 + ShotJudge.MaxDelay);

        // Assert
        // 그만두면 답이 없고 더 받지 않음. c는 그대로 받음
        Assert.Null(stopped);
        Assert.Empty(TickLoop.MessagesFor(_a, messages).OfType<ShotResultMessage>());
        Assert.Single(TickLoop.MessagesFor(_c, messages).OfType<ShotResultMessage>());
    }

    [Fact]
    public void RemovePlayer_Target_SpectatorsUnwatched()
    {
        // Arrange
        double now = ADownByB();
        _room.Spectate(_a, _c);
        _room.Fire(_c, CToB, 0, now + 100, now + 120);

        // Act
        _room.RemovePlayer(_c);
        var messages = _room.Step(now + 100 + ShotJudge.MaxDelay);

        // Assert
        // 대상이 나가도 틱은 이어지고, 나간 사람의 개인 메시지 복사본은 없음
        Assert.DoesNotContain(TickLoop.MessagesFor(_a, messages), m => m is ShotResultMessage or WoundedMessage);
    }

    [Fact]
    public void Spectate_DownTarget_Ignored()
    {
        // Arrange
        // 둘이 서로 머리를 맞혀 함께 쓰러짐
        double duelAt = StartDuel();
        double shotAt = duelAt + 100;
        _room.Fire(_a, Math.PI, HeadAcross, shotAt, shotAt + 20);
        _room.Fire(_b, 0, HeadAcross, shotAt, shotAt + 20);
        _room.Step(shotAt + ShotJudge.MaxDelay);

        // Act
        var spectating = _room.Spectate(_a, _b);

        // Assert
        // 관전은 살아 있는 사람의 시점만 봄 (컨셉 기획서 규칙 탈락한 뒤). 함께 쓰러져 다시 설 사람끼리 반샷을 엿보지 못함
        Assert.Null(spectating);
    }

    [Fact]
    public void Spectate_RematchStands_Unwatched()
    {
        // Arrange
        // b가 a의 머리를 먼저 맞혀 a가 쓰러지고, 아직 서 있는 b를 a가 봄. 그다음 a가 D 안에 쏜 발사가 b를 맞혀 함께 쓰러지고 다음 라운드에 둘 다 다시 섬
        double duelAt = StartDuel();
        double shotAt = duelAt + 100;
        _room.Fire(_b, 0, HeadAcross, shotAt, shotAt + 20);
        _room.Fire(_a, Math.PI, HeadAcross, shotAt + 50, shotAt + 70);
        _room.Step(shotAt + ShotJudge.MaxDelay); // a만 쓰러짐
        var spectating = _room.Spectate(_a, _b);
        _room.Step(shotAt + 50 + ShotJudge.MaxDelay); // b도 쓰러짐
        double decided = shotAt + 50 + ShotJudge.MaxDelay + ShotJudge.MutualWindow;
        _room.Step(decided);
        double next = StartNextRound(decided + RoundFlow.ResultDuration);

        // Act
        _room.Fire(_b, Math.PI, 0, next + 100, next + 120); // b는 -x 쪽 바깥으로 쏨
        var messages = _room.Step(next + 100 + ShotJudge.MaxDelay);

        // Assert
        // 다시 서면 관전이 풀려 서 있는 동안 b의 사격 결과를 받지 않음
        Assert.NotNull(spectating);
        Assert.Single(TickLoop.MessagesFor(_b, messages).OfType<ShotResultMessage>());
        Assert.Empty(TickLoop.MessagesFor(_a, messages).OfType<ShotResultMessage>());
    }

    [Fact]
    public void Step_HeadShot_DownedByToTargetOnly()
    {
        // Arrange
        double duelAt = StartDuelWith(_a, _b, _c); // 셋이어야 b가 쓰러져도 결투가 이어짐
        _room.Fire(_a, AToB, HeadInTriangle, duelAt + 100, duelAt + 120);

        // Act
        var messages = _room.Step(duelAt + 100 + ShotJudge.MaxDelay);

        // Assert
        // 쓰러진 b에게만 쓰러뜨린 a와 그 발사의 쏜 시각을 eliminated 바로 뒤에 알림. 다시 보기가 a의 눈으로 그림 (기술 설계서 메시지 downedBy)
        var toB = TickLoop.MessagesFor(_b, messages).ToList();
        int eliminated = toB.FindIndex(m => m is EliminatedMessage);
        var downedBy = Assert.IsType<DownedByMessage>(toB[eliminated + 1]);
        Assert.Equal(_a, downedBy.PlayerId);
        Assert.Equal(duelAt + 100, downedBy.At, Tolerance);
        Assert.Empty(TickLoop.MessagesFor(_a, messages).OfType<DownedByMessage>());
        Assert.Empty(TickLoop.MessagesFor(_c, messages).OfType<DownedByMessage>());
    }

    [Fact]
    public void Step_SecondWound_DownedByLastShooter()
    {
        // Arrange
        // a가 먼저 c의 몸을, b가 나중에 c의 몸을 맞힘. 두 번째 반샷에 c가 쓰러짐
        double duelAt = StartDuelWith(_a, _b, _c);
        _room.Fire(_a, AToC, 0, duelAt + 100, duelAt + 120);
        _room.Fire(_b, BToC, 0, duelAt + 150, duelAt + 170);

        // Act
        var messages = _room.Step(duelAt + 150 + ShotJudge.MaxDelay);

        // Assert
        // 쓰러뜨린 발사는 쏜 시각 순서로 마지막에 맞힌 b의 것임
        var downedBy = Assert.Single(TickLoop.MessagesFor(_c, messages).OfType<DownedByMessage>());
        Assert.Equal(_b, downedBy.PlayerId);
        Assert.Equal(duelAt + 150, downedBy.At, Tolerance);
    }

    [Fact]
    public void Step_Timeout_NoDownedBy()
    {
        // Arrange
        double duelAt = StartDuel(); // 아무도 쏘지 않음
        double deadline = duelAt + RoundFlow.DuelLimit;

        // Act
        var messages = _room.Step(deadline + ShotJudge.MaxDelay);

        // Assert
        // 자살은 쏜 사람이 없어 알리지 않음. 클라는 내 눈으로 다시 봄
        Assert.Equal(2, messages.OfType<EliminatedMessage>().Count());
        Assert.DoesNotContain(messages.OfType<Direct>(), d => d.Message is DownedByMessage);
    }

    private static PlayerState Find(StateMessage state, Guid id) => state.Players.Single(p => p.Id == id);

    // distance만큼 떨어진 사람의 머리 가운데를 겨누는 위아래 각도. 머리 옆면(앞으로 distance - HeadRadius)에서 눈높이보다 머리 가운데만큼 위
    private static double HeadPitch(double distance) =>
        Math.Atan(((HitScan.BodyTop + HitScan.HeadTop) / 2 - HitScan.EyeHeight) / (distance - HitScan.HeadRadius));

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

    // ids의 i번째 사람이 그 인원수의 i번 시작 자리에 서서 중심을 보고, 위아래로는 정면을 보는지 봄
    private static void AssertFacesCenterAtStart(StateMessage state, params Guid[] ids)
    {
        for (int i = 0; i < ids.Length; i++)
        {
            var spot = StartPlacement.At(ids.Length, i, CenterX, CenterY);
            var player = Find(state, ids[i]);
            AssertStandsAt(spot, player);
            Assert.Equal(spot.Angle, player.Angle, Tolerance);
            Assert.Equal(0, player.Pitch);
        }
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

    // 다음 라운드 카운트다운 시각부터 그 라운드의 결투 시각 틱까지 돌리고 결투 시각을 돌려줌
    private double StartNextRound(double countdownAt)
    {
        _room.Step(countdownAt);
        var (_, duelAt) = StepUntilDuelAnnounced(_room, countdownAt + 3 * RoundFlow.CountdownInterval);
        _room.Step(duelAt);
        return duelAt;
    }

    // 둘의 결투에서 a가 b를 맞혀 이기게 하고, 준비 중으로 돌아가는 시각을 돌려줌. 승리를 정하는 틱까지 부르고 그 시각의 틱은 부르지 않음
    private double AWins(double duelAt)
    {
        double hitAt = duelAt + 100;
        _room.Fire(_a, Math.PI, HeadAcross, hitAt, hitAt + 20);
        double now = hitAt + ShotJudge.MaxDelay + ShotJudge.MutualWindow;
        _room.Step(now);
        return now + RoundFlow.ResultDuration;
    }

    // 셋이 결투해 a가 c를 맞히고 b는 빗나가, 살아남은 a와 b가 다음 라운드로 가게 함. 다음 라운드 카운트다운 시각을 돌려주고 그 시각의 틱은 부르지 않음.
    // 다 걸으면 a는 0번(+x 쪽), b는 1번(2π/3 쪽), c는 2번(4π/3 쪽) 자리에서 바깥을 봄
    private double RematchAAndBWithCDown()
    {
        double duelAt = StartDuelWith(_a, _b, _c);
        _room.Fire(_a, AToC, HeadInTriangle, duelAt, duelAt + 20); // c의 머리를 맞힘
        _room.Fire(_b, 2 * Math.PI / 3, 0, duelAt, duelAt + 20); // 바깥으로 쏨
        double now = duelAt + ShotJudge.MaxDelay + ShotJudge.MutualWindow;
        _room.Step(now); // c가 맞고 W + D가 지나 다음 라운드가 정해짐
        return now + RoundFlow.ResultDuration;
    }

    // 셋이 결투해 a가 c의 몸을 맞히고 b가 a의 머리를 맞히게 하고, 둘을 판정한 틱의 시각을 돌려줌.
    // a는 쓰러지고, b는 총알이 없고, c는 반샷 하나에 한 발이 남아 결투가 이어짐
    private double ADownByB()
    {
        double duelAt = StartDuelWith(_a, _b, _c);
        _room.Fire(_a, AToC, 0, duelAt + 100, duelAt + 120);
        _room.Fire(_b, BToA, HeadInTriangle, duelAt + 150, duelAt + 170);
        double now = duelAt + 150 + ShotJudge.MaxDelay;
        _room.Step(now);
        return now;
    }
}
