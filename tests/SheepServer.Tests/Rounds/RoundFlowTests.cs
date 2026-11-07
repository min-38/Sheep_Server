namespace SheepServer.Tests.Rounds;

using SheepServer.Rounds;

public class RoundFlowTests
{
    // 시드는 걷기가 시작할 때 무작위 대기를 정하는 데만 쓰임. 다른 단계에서는 결과에 영향이 없음
    private const int Seed = 7;

    // 3, 2, 1을 카운트다운 간격마다 보여 주므로 카운트다운은 간격의 3배 (시스템 기획서 판 흐름 시간)
    private static readonly double CountdownDuration = 3 * RoundFlow.CountdownInterval;
    private static readonly Round Lobby = new(RoundPhase.Lobby, null, 0);

    private static readonly Guid A = Guid.NewGuid();
    private static readonly Guid B = Guid.NewGuid();
    private static readonly Guid C = Guid.NewGuid();
    private static readonly Guid D = Guid.NewGuid();

    [Theory]
    [InlineData(2)]
    [InlineData(7)]
    public void Advance_AllReady_SchedulesCountdownAfterLead(int players)
    {
        // Act
        var round = RoundFlow.Advance(Lobby, 1000, players, players, Seed);

        // Assert
        // 카운트다운은 바로 시작하지 않음. 지연이 큰 사람도 시작 전에 알림을 받도록 Lead 뒤로 잡음
        Assert.Equal(new Round(RoundPhase.Lobby, RoundPhase.Countdown, 1000 + RoundFlow.Lead), round);
    }

    [Theory]
    [InlineData(1, 1)] // 혼자서는 시작하지 않음
    [InlineData(2, 1)]
    [InlineData(3, 2)]
    [InlineData(2, 0)]
    [InlineData(0, 0)]
    public void Advance_AloneOrSomeoneNotReady_StaysInLobby(int players, int ready)
    {
        // Act
        var round = RoundFlow.Advance(Lobby, 1000, players, ready, Seed);

        // Assert
        Assert.Equal(Lobby, round);
    }

    [Fact]
    public void Advance_BeforeScheduledTime_Unchanged()
    {
        // Arrange
        var scheduled = new Round(RoundPhase.Lobby, RoundPhase.Countdown, 1500);

        // Act
        var round = RoundFlow.Advance(scheduled, 1499.9, 2, 2, Seed);

        // Assert
        Assert.Equal(scheduled, round);
    }

    [Fact]
    public void Advance_AtCountdownTime_EntersCountdownAndSchedulesWalk()
    {
        // Act
        var round = RoundFlow.Advance(new Round(RoundPhase.Lobby, RoundPhase.Countdown, 1500), 1500, 2, 2, Seed);

        // Assert
        Assert.Equal(new Round(RoundPhase.Countdown, RoundPhase.Walk, 1500 + CountdownDuration), round);
    }

    [Fact]
    public void Advance_AtWalkTime_DecidesDuelTimeButDoesNotAnnounceYet()
    {
        // Act
        var round = RoundFlow.Advance(new Round(RoundPhase.Countdown, RoundPhase.Walk, 4500), 4500, 2, 2, Seed);

        // Assert
        // 결투 시각은 걷기가 끝난 뒤 무작위로 기다린 시각. 아직 알리지 않아 Next는 비워 둠 (기술 설계서 메시지 phase)
        Assert.Equal(new Round(RoundPhase.Walk, null, 4500 + RoundFlow.WalkDuration + RoundFlow.DuelWait(Seed)), round);
    }

    [Fact]
    public void Advance_WaitingForDuelBeforeLead_Unchanged()
    {
        // Arrange
        var waiting = new Round(RoundPhase.Walk, null, 10_000);

        // Act
        var round = RoundFlow.Advance(waiting, 10_000 - RoundFlow.Lead - 0.1, 2, 2, Seed);

        // Assert
        Assert.Equal(waiting, round);
    }

    [Fact]
    public void Advance_LeadBeforeDuel_AnnouncesDuel()
    {
        // Act
        var round = RoundFlow.Advance(new Round(RoundPhase.Walk, null, 10_000), 10_000 - RoundFlow.Lead, 2, 2, Seed);

        // Assert
        // 카운트다운처럼 Lead만큼 앞서 알림. 시각은 걷기가 시작할 때 정한 그대로임
        Assert.Equal(new Round(RoundPhase.Walk, RoundPhase.Duel, 10_000), round);
    }

    [Fact]
    public void Advance_WaitingForDuelWithEveryoneReady_DoesNotStartAgain()
    {
        // Arrange
        var waiting = new Round(RoundPhase.Walk, null, 10_000);

        // Act
        var round = RoundFlow.Advance(waiting, 5000, 2, 2, Seed);

        // Assert
        Assert.Equal(waiting, round); // Next가 비어 있어도 판 도중임. 모두 준비했는지는 준비 중에만 봄
    }

    [Fact]
    public void Advance_AtDuelTime_EntersDuelWithNothingNext()
    {
        // Act
        var round = RoundFlow.Advance(new Round(RoundPhase.Walk, RoundPhase.Duel, 7500), 7500, 2, 2, Seed);

        // Assert
        Assert.Equal(RoundPhase.Duel, round.Phase);
        Assert.Null(round.Next); // 다음 단계는 결과(승리, 재대결)가 정해질 때 정함
    }

    [Fact]
    public void Advance_InDuelWithEveryoneReady_DoesNotStartAgain()
    {
        // Arrange
        var duel = new Round(RoundPhase.Duel, null, 0);

        // Act
        var round = RoundFlow.Advance(duel, 100_000, 2, 2, Seed);

        // Assert
        Assert.Equal(duel, round); // 모두 준비했는지는 준비 중에만 봄
    }

    [Fact]
    public void Advance_TickLaterThanScheduled_NextTimeFollowsSchedule()
    {
        // Act
        // 틱은 50ms마다라 정한 시각보다 늦게 옴. 늦게 온 틱을 기준으로 다음 시각을 잡으면 알린 시각과 어긋남
        var round = RoundFlow.Advance(new Round(RoundPhase.Lobby, RoundPhase.Countdown, 1500), 1540, 2, 2, Seed);

        // Assert
        Assert.Equal(new Round(RoundPhase.Countdown, RoundPhase.Walk, 1500 + CountdownDuration), round);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(1, 0)]
    public void Advance_ScheduledThenSomeoneLeft_GoesOn(int players, int ready)
    {
        // Act
        // 판이 정해진 뒤에는 인원과 준비를 보지 않음. 나간 사람은 판에서 빠질 뿐임
        var round = RoundFlow.Advance(new Round(RoundPhase.Lobby, RoundPhase.Countdown, 1500), 1500, players, ready, Seed);

        // Assert
        Assert.Equal(new Round(RoundPhase.Countdown, RoundPhase.Walk, 1500 + CountdownDuration), round);
    }

    [Fact]
    public void Advance_AtLobbyTime_BackToLobbyWithNothingNext()
    {
        // Act
        var round = RoundFlow.Advance(new Round(RoundPhase.Duel, RoundPhase.Lobby, 20_000), 20_000, 2, 0, Seed);

        // Assert
        // 승리 결과를 보여 준 뒤 준비 중으로 돌아감. 그 뒤 모두 준비하면 새 판이 정해짐 (기술 설계서 메시지 phase)
        Assert.Equal(Lobby, round);
    }

    [Fact]
    public void Advance_AtRematchCountdownTime_EntersCountdownAndSchedulesWalk()
    {
        // Act
        var round = RoundFlow.Advance(new Round(RoundPhase.Duel, RoundPhase.Countdown, 20_000), 20_000, 3, 0, Seed);

        // Assert
        // 재대결은 결과를 보여 준 뒤 첫 판처럼 카운트다운부터 다시 함 (컨셉 기획서 게임 루프)
        Assert.Equal(new Round(RoundPhase.Countdown, RoundPhase.Walk, 20_000 + CountdownDuration), round);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(0)] // 총알이 없어도 혼자 남으면 이김
    public void Result_OneAlive_Win(int ammo)
    {
        // Act
        var result = RoundFlow.Result([(A, ammo)], [B]);

        // Assert
        // 컨셉 기획서 게임 루프: 1명만 남으면 승리. 승리면 장전 수는 0 (기술 설계서 메시지 result)
        AssertResult(result, Outcome.Win, [A], 0);
    }

    [Fact]
    public void Result_TwoAliveSomeoneHasAmmo_GoesOn()
    {
        // Act
        var bothArmed = RoundFlow.Result([(A, 1), (B, 1)], [C]);
        var oneArmed = RoundFlow.Result([(A, 0), (B, 1)], [C]);

        // Assert
        // 결과가 없으면 null. 한 사람이라도 총알이 있으면 결투가 이어짐
        Assert.Null(bothArmed);
        Assert.Null(oneArmed);
    }

    [Fact]
    public void Result_AliveAllFired_NextRoundWithOneBullet()
    {
        // Act
        var result = RoundFlow.Result([(A, 0), (B, 0), (C, 0)], [D]);

        // Assert
        // 컨셉 기획서 규칙: 살아 있는 사람이 모두 쏘면 살아남은 사람끼리 다음 라운드로 감. 인원과 상관없이 라운드마다 한 발.
        // 다음 라운드로 가는 사람은 받은 순서대로
        AssertResult(result, Outcome.Rematch, [A, B, C], 1);
    }

    [Fact]
    public void Result_NobodyAlive_RematchAmongLastDown()
    {
        // Act
        var result = RoundFlow.Result([], [B, C]);

        // Assert
        // 컨셉 기획서 규칙: 아무도 안 남으면 무승부로 보고 마지막에 함께 쓰러진 사람끼리 다음 라운드로 감. 이때도 한 발
        AssertResult(result, Outcome.Rematch, [B, C], 1);
    }

    [Fact]
    public void Result_NobodyAliveOneLastDown_ThatOneWins()
    {
        // Act
        // 살아 있던 사람이 모두 나가고, 가장 늦게 쓰러진 사람이 혼자 방에 남음 (M4에서 아무도 안 남는 유일한 경우)
        var result = RoundFlow.Result([], [C]);

        // Assert
        // 혼자서는 재대결할 수 없음. 상대가 모두 나가 기권한 것으로 보고 그 사람이 이김
        AssertResult(result, Outcome.Win, [C], 0);
    }

    [Fact]
    public void DuelWait_SameSeed_SameWait()
    {
        // Act
        double first = RoundFlow.DuelWait(42);
        double second = RoundFlow.DuelWait(42);

        // Assert
        Assert.Equal(first, second); // 시드를 고정하면 테스트에서 결투 시각을 미리 알 수 있음
    }

    [Fact]
    public void DuelWait_AnySeed_WithinRange()
    {
        for (int seed = 0; seed < 1000; seed++)
        {
            // Act
            double wait = RoundFlow.DuelWait(seed);

            // Assert
            Assert.InRange(wait, RoundFlow.MinDuelWait, RoundFlow.MaxDuelWait); // 시스템 기획서 판 흐름 시간
        }
    }

    [Fact]
    public void DuelWait_DifferentSeeds_Vary()
    {
        // Act
        var waits = new HashSet<double>();
        for (int seed = 0; seed < 100; seed++)
        {
            waits.Add(RoundFlow.DuelWait(seed));
        }

        // Assert
        // 늘 같은 값이면 타이밍을 외워 미리 반응할 수 있음 (컨셉 기획서 규칙 "시작!" 타이밍)
        Assert.True(waits.Count > 1, "시드가 달라도 대기 시간이 모두 같음");
    }

    private static void AssertResult(ResultMessage? actual, Outcome outcome, Guid[] playerIds, int rounds)
    {
        Assert.NotNull(actual);
        Assert.Equal(outcome, actual.Outcome);
        Assert.Equal(playerIds, actual.PlayerIds);
        Assert.Equal(rounds, actual.Rounds);
    }
}
