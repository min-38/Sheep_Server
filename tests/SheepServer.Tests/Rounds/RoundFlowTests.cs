namespace SheepServer.Tests.Rounds;

using SheepServer.Rounds;

public class RoundFlowTests
{
    // 3, 2, 1을 카운트다운 간격마다 보여 주므로 카운트다운은 간격의 3배 (시스템 기획서 판 흐름 시간)
    private static readonly double CountdownDuration = 3 * RoundFlow.CountdownInterval;
    private static readonly Round Lobby = new(RoundPhase.Lobby, null, 0);

    [Theory]
    [InlineData(2)]
    [InlineData(7)]
    public void Advance_AllReady_SchedulesCountdownAfterLead(int players)
    {
        // Act
        var round = RoundFlow.Advance(Lobby, 1000, players, players);

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
        var round = RoundFlow.Advance(Lobby, 1000, players, ready);

        // Assert
        Assert.Equal(Lobby, round);
    }

    [Fact]
    public void Advance_BeforeScheduledTime_Unchanged()
    {
        // Arrange
        var scheduled = new Round(RoundPhase.Lobby, RoundPhase.Countdown, 1500);

        // Act
        var round = RoundFlow.Advance(scheduled, 1499.9, 2, 2);

        // Assert
        Assert.Equal(scheduled, round);
    }

    [Fact]
    public void Advance_AtCountdownTime_EntersCountdownAndSchedulesWalk()
    {
        // Act
        var round = RoundFlow.Advance(new Round(RoundPhase.Lobby, RoundPhase.Countdown, 1500), 1500, 2, 2);

        // Assert
        Assert.Equal(new Round(RoundPhase.Countdown, RoundPhase.Walk, 1500 + CountdownDuration), round);
    }

    [Fact]
    public void Advance_AtWalkTime_EntersWalkAndSchedulesDuel()
    {
        // Act
        var round = RoundFlow.Advance(new Round(RoundPhase.Countdown, RoundPhase.Walk, 4500), 4500, 2, 2);

        // Assert
        Assert.Equal(new Round(RoundPhase.Walk, RoundPhase.Duel, 4500 + RoundFlow.WalkDuration), round);
    }

    [Fact]
    public void Advance_AtDuelTime_EntersDuelWithNothingNext()
    {
        // Act
        var round = RoundFlow.Advance(new Round(RoundPhase.Walk, RoundPhase.Duel, 7500), 7500, 2, 2);

        // Assert
        Assert.Equal(RoundPhase.Duel, round.Phase);
        Assert.Null(round.Next); // 결투 다음(승리, 재대결)은 이후 이슈
    }

    [Fact]
    public void Advance_InDuelWithEveryoneReady_DoesNotStartAgain()
    {
        // Arrange
        var duel = new Round(RoundPhase.Duel, null, 0);

        // Act
        var round = RoundFlow.Advance(duel, 100_000, 2, 2);

        // Assert
        Assert.Equal(duel, round); // 모두 준비했는지는 준비 중에만 봄
    }

    [Fact]
    public void Advance_TickLaterThanScheduled_NextTimeFollowsSchedule()
    {
        // Act
        // 틱은 50ms마다라 정한 시각보다 늦게 옴. 늦게 온 틱을 기준으로 다음 시각을 잡으면 알린 시각과 어긋남
        var round = RoundFlow.Advance(new Round(RoundPhase.Lobby, RoundPhase.Countdown, 1500), 1540, 2, 2);

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
        var round = RoundFlow.Advance(new Round(RoundPhase.Lobby, RoundPhase.Countdown, 1500), 1500, players, ready);

        // Assert
        Assert.Equal(new Round(RoundPhase.Countdown, RoundPhase.Walk, 1500 + CountdownDuration), round);
    }
}
