namespace SheepServer.Tests.Shooting;

using SheepServer.Arena;
using SheepServer.Shooting;

// 기술 설계서 명중 판정: 도착 순서가 아니라 쏜 시각 순서로 판정함. 맞은 뒤 D 안에 쏜 발사는 유효해 서로 맞히면 함께 쓰러짐.
// a는 (6.5, 10.5)에서 +x(0)를 보면 b를, b는 (14.5, 10.5)에서 -x(π)를 보면 a를 겨눔. 둘 사이에 벽은 없음
public class ShotJudgeTests
{
    private readonly Guid _a = Guid.NewGuid();
    private readonly Guid _b = Guid.NewGuid();
    private readonly Guid _c = Guid.NewGuid();

    [Fact]
    public void ClampAt_LaterThanArrival_Arrival()
    {
        // Act
        // 도착보다 늦게 쐈다는 값은 믿지 않음
        double at = ShotJudge.ClampAt(1500, 1000);

        // Assert
        Assert.Equal(1000, at);
    }

    [Fact]
    public void ClampAt_MoreThanMaxDelayBeforeArrival_ArrivalMinusMaxDelay()
    {
        // Act
        // W보다 오래 걸려 왔다는 값은 W만큼만 앞당겨 줌. 그래야 W만 기다리고 판정해도 순서가 뒤집히지 않음
        double at = ShotJudge.ClampAt(0, 1000);

        // Assert
        Assert.Equal(1000 - ShotJudge.MaxDelay, at);
    }

    [Fact]
    public void ClampAt_WithinMaxDelay_Unchanged()
    {
        // Act
        double at = ShotJudge.ClampAt(1000 - ShotJudge.MaxDelay / 2, 1000);

        // Assert
        Assert.Equal(1000 - ShotJudge.MaxDelay / 2, at);
    }

    [Fact]
    public void Judge_FiredFirstButArrivedLater_FirstShotWins()
    {
        // Arrange
        // b의 발사가 먼저 도착했지만 a가 D보다 더 먼저 쐈음. 목록은 도착 순서
        var shots = new[]
        {
            new Shot(_b, Math.PI, 0, 100 + ShotJudge.MutualWindow + 1),
            new Shot(_a, 0, 0, 100),
        };

        // Act
        var outcomes = ShotJudge.Judge(shots, Duelists(), ArenaMap.Fixed);

        // Assert
        // a가 먼저 맞힘. b는 맞고 D가 지난 뒤에 쏘아 그 발사는 무효라 결과에도 없음
        Assert.Equal(new[] { (_b, 100.0) }, Downs(outcomes));
        Assert.Equal(_a, Assert.Single(outcomes).ShooterId);
    }

    [Fact]
    public void Judge_HitBackWithinMutualWindow_BothDown()
    {
        // Arrange
        var shots = new[]
        {
            new Shot(_b, Math.PI, 0, 100 + ShotJudge.MutualWindow),
            new Shot(_a, 0, 0, 100),
        };

        // Act
        var outcomes = ShotJudge.Judge(shots, Duelists(), ArenaMap.Fixed);

        // Assert
        // b는 맞은 뒤 D 안에 쏘아 그 발사도 유효함. 둘 다 쓰러지고 맞은 시각은 맞힌 발사의 쏜 시각
        Assert.Equal(new[] { (_b, 100.0), (_a, 100 + ShotJudge.MutualWindow) }, Downs(outcomes));
    }

    [Theory]
    [InlineData(ShotJudge.MutualWindow, true)]
    [InlineData(ShotJudge.MutualWindow + 1, false)]
    public void Judge_ShooterDownInEarlierTick_ValidOnlyWithinMutualWindow(double shotAt, bool hit)
    {
        // Arrange
        // a는 앞 틱에서 0에 맞았음. 그 뒤에 쏜 발사를 이번 틱에 판정함
        var players = new List<(Guid Id, double X, double Y, double? DownAt)> { (_a, 6.5, 10.5, 0), (_b, 14.5, 10.5, null) };

        // Act
        var outcomes = ShotJudge.Judge([new Shot(_a, 0, 0, shotAt)], players, ArenaMap.Fixed);

        // Assert
        if (hit)
        {
            Assert.Equal(new[] { (_b, shotAt) }, Downs(outcomes));
        }
        else
        {
            Assert.Empty(outcomes);
        }
    }

    [Fact]
    public void Judge_DownPlayerInTheWay_RayPassesThrough()
    {
        // Arrange
        // b는 이미 쓰러짐. a에서 +x로 쏘면 b를 지나 c에 닿음
        var players = new List<(Guid Id, double X, double Y, double? DownAt)> { (_a, 6.5, 10.5, null), (_b, 10.5, 10.5, 50), (_c, 14.5, 10.5, null) };

        // Act
        var outcomes = ShotJudge.Judge([new Shot(_a, 0, 0, 100)], players, ArenaMap.Fixed);

        // Assert
        Assert.Equal(new[] { (_c, 100.0) }, Downs(outcomes));
    }

    [Fact]
    public void Judge_ShooterLeftRoom_Ignored()
    {
        // Act
        // 판정 전에 나간 사람의 발사는 버림 (기술 설계서 메시지 fire)
        var outcomes = ShotJudge.Judge([new Shot(_c, 0, 0, 100)], Duelists(), ArenaMap.Fixed);

        // Assert
        Assert.Empty(outcomes);
    }

    [Fact]
    public void Judge_Miss_OutcomeWithoutTarget()
    {
        // Act
        // a가 b 반대쪽(-x)으로 쏨. 빗나간 발사도 쏜 사람에게 알려야 해서 결과에 들어감
        var outcomes = ShotJudge.Judge([new Shot(_a, Math.PI, 0, 100)], Duelists(), ArenaMap.Fixed);

        // Assert
        var outcome = Assert.Single(outcomes);
        Assert.Equal(_a, outcome.ShooterId);
        Assert.Equal(100, outcome.At);
        Assert.Null(outcome.TargetId);
        Assert.Null(outcome.Zone);
        Assert.False(outcome.Down);
    }

    [Fact]
    public void Judge_BodyHit_Down()
    {
        // Act
        var outcomes = ShotJudge.Judge([new Shot(_a, 0, 0, 100)], Duelists(), ArenaMap.Fixed);

        // Assert
        // 반샷은 아직 없음(SHEEP-61). 맞으면 어디든 쓰러짐
        var outcome = Assert.Single(outcomes);
        Assert.Equal(_b, outcome.TargetId);
        Assert.Equal(HitZone.Body, outcome.Zone);
        Assert.True(outcome.Down);
    }

    // 쓰러뜨린 결과만 (맞은 사람, 맞은 시각)으로. 맞은 시각은 맞힌 발사의 쏜 시각
    private static IEnumerable<(Guid Id, double At)> Downs(List<ShotOutcome> outcomes) =>
        outcomes.Where(o => o.Down).Select(o => (o.TargetId!.Value, o.At));

    private List<(Guid Id, double X, double Y, double? DownAt)> Duelists() => [(_a, 6.5, 10.5, null), (_b, 14.5, 10.5, null)];
}
