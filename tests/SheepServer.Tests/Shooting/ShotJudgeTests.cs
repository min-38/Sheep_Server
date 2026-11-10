namespace SheepServer.Tests.Shooting;

using SheepServer.Arena;
using SheepServer.Shooting;

// 기술 설계서 명중 판정: 도착 순서가 아니라 쏜 시각 순서로 판정함. 맞은 뒤에 쏜 발사는 근소한 차이여도 무효라 먼저 쏜 사람만 맞힘.
// 쏜 시각이 ms까지 같으면 둘 다 유효해 함께 쓰러짐 (사용자 결정 2026-11-11, SHEEP-160).
// a는 (6.5, 10.5)에서 +x(0)를 보면 b를, b는 (14.5, 10.5)에서 -x(π)를 보면 a를 겨눔. 둘 사이에 벽은 없음.
// 위아래 0으로 쏘면 몸(반샷), Head로 쏘면 머리(즉사)에 맞음 (컨셉 기획서 규칙 머리는 즉사, 몸은 반샷)
public class ShotJudgeTests
{
    // 8칸 떨어진 사람의 머리 가운데를 겨누는 위아래 각도. 머리 옆면(앞으로 8 - HeadRadius)에서 눈높이보다 머리 가운데만큼 위
    private static readonly double Head = Math.Atan(((HitScan.BodyTop + HitScan.HeadTop) / 2 - HitScan.EyeHeight) / (8 - HitScan.HeadRadius));

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
        // b의 발사가 먼저 도착했지만 a가 더 먼저 쐈음. 목록은 도착 순서
        var shots = new[]
        {
            new Shot(_b, Math.PI, Head, 101),
            new Shot(_a, 0, Head, 100),
        };

        // Act
        var outcomes = ShotJudge.Judge(shots, Duelists(), ArenaMap.Fixed);

        // Assert
        // a가 먼저 맞힘. b는 맞은 뒤에 쏘아 그 발사는 무효라 결과에도 없음
        Assert.Equal(new[] { (_b, 100.0) }, Downs(outcomes));
        Assert.Equal(_a, Assert.Single(outcomes).ShooterId);
    }

    [Fact]
    public void Judge_HitBackJustAfter_OnlyFirstShooterHits()
    {
        // Arrange
        var shots = new[]
        {
            new Shot(_b, Math.PI, Head, 150),
            new Shot(_a, 0, Head, 100),
        };

        // Act
        var outcomes = ShotJudge.Judge(shots, Duelists(), ArenaMap.Fixed);

        // Assert
        // b는 맞은 뒤 0.05초 만에 쐈지만 그 발사는 무효라 a만 맞힘. 재대결 없이 먼저 쏜 a가 이김 (SHEEP-160)
        Assert.Equal(new[] { (_b, 100.0) }, Downs(outcomes));
        Assert.Equal(_a, Assert.Single(outcomes).ShooterId);
    }

    [Fact]
    public void Judge_SameMillisecond_BothDown()
    {
        // Arrange
        var shots = new[]
        {
            new Shot(_b, Math.PI, Head, 100),
            new Shot(_a, 0, Head, 100),
        };

        // Act
        var outcomes = ShotJudge.Judge(shots, Duelists(), ArenaMap.Fixed);

        // Assert
        // 쏜 시각이 ms까지 같으면 누가 먼저인지 가를 수 없어 둘 다 유효함. 함께 쓰러져 재대결로 감 (SHEEP-160)
        Assert.Equal(new[] { (_a, 100.0), (_b, 100.0) }, Downs(outcomes));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(100, false)]
    public void Judge_ShooterDownInEarlierTick_ValidOnlyAtSameMillisecond(double shotAt, bool hit)
    {
        // Arrange
        // a는 앞 틱에서 0에 맞았음. 그 뒤에 쏜 발사를 이번 틱에 판정함
        var players = new List<(Guid Id, double X, double Y, double? DownAt, int Wounds)> { (_a, 6.5, 10.5, 0, 0), (_b, 14.5, 10.5, null, 0) };

        // Act
        var outcomes = ShotJudge.Judge([new Shot(_a, 0, Head, shotAt)], players, ArenaMap.Fixed);

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
        var players = new List<(Guid Id, double X, double Y, double? DownAt, int Wounds)> { (_a, 6.5, 10.5, null, 0), (_b, 10.5, 10.5, 50, 0), (_c, 14.5, 10.5, null, 0) };

        // Act
        var outcomes = ShotJudge.Judge([new Shot(_a, 0, Head, 100)], players, ArenaMap.Fixed);

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
    public void Judge_BodyOnce_WoundedNotDown()
    {
        // Act
        var outcomes = ShotJudge.Judge([new Shot(_a, 0, 0, 100)], Duelists(), ArenaMap.Fixed);

        // Assert
        // 다치지 않은 사람의 몸은 반샷 하나라 쓰러지지 않음
        var outcome = Assert.Single(outcomes);
        Assert.Equal(_b, outcome.TargetId);
        Assert.Equal(HitZone.Body, outcome.Zone);
        Assert.False(outcome.Down);
    }

    [Fact]
    public void Judge_BodyTwiceInOneBatch_Down()
    {
        // Arrange
        // c는 (10.5, 6.5)에서 π/4를 보면 b를 겨눔. a보다 늦게 쏨
        var players = new List<(Guid Id, double X, double Y, double? DownAt, int Wounds)> { (_a, 6.5, 10.5, null, 0), (_b, 14.5, 10.5, null, 0), (_c, 10.5, 6.5, null, 0) };
        var shots = new[] { new Shot(_c, Math.PI / 4, 0, 150), new Shot(_a, 0, 0, 100) };

        // Act
        var outcomes = ShotJudge.Judge(shots, players, ArenaMap.Fixed);

        // Assert
        // 반샷만 맞은 b는 같은 판정 안의 다음 발사에도 표적이고, 두 번째 몸 명중에 쓰러짐. 맞은 시각은 두 번째 발사의 쏜 시각
        Assert.Collection(
            outcomes,
            o => Assert.Equal((_a, _b, HitZone.Body, false), (o.ShooterId, o.TargetId!.Value, o.Zone!.Value, o.Down)),
            o => Assert.Equal((_c, _b, HitZone.Body, true), (o.ShooterId, o.TargetId!.Value, o.Zone!.Value, o.Down)));
        Assert.Equal(new[] { (_b, 150.0) }, Downs(outcomes));
    }

    [Fact]
    public void Judge_AlreadyWoundedBodyHit_Down()
    {
        // Arrange
        // b는 앞 라운드나 앞 틱에서 반샷을 하나 맞았음
        var players = new List<(Guid Id, double X, double Y, double? DownAt, int Wounds)> { (_a, 6.5, 10.5, null, 0), (_b, 14.5, 10.5, null, 1) };

        // Act
        var outcomes = ShotJudge.Judge([new Shot(_a, 0, 0, 100)], players, ArenaMap.Fixed);

        // Assert
        var outcome = Assert.Single(outcomes);
        Assert.Equal(HitZone.Body, outcome.Zone);
        Assert.True(outcome.Down);
    }

    [Fact]
    public void Judge_HeadFromFull_Down()
    {
        // Act
        var outcomes = ShotJudge.Judge([new Shot(_a, 0, Head, 100)], Duelists(), ArenaMap.Fixed);

        // Assert
        // 머리는 반샷 수와 상관없이 한 발에 쓰러짐
        var outcome = Assert.Single(outcomes);
        Assert.Equal(_b, outcome.TargetId);
        Assert.Equal(HitZone.Head, outcome.Zone);
        Assert.True(outcome.Down);
    }

    // 쓰러뜨린 결과만 (맞은 사람, 맞은 시각)으로. 맞은 시각은 맞힌 발사의 쏜 시각
    private static IEnumerable<(Guid Id, double At)> Downs(List<ShotOutcome> outcomes) =>
        outcomes.Where(o => o.Down).Select(o => (o.TargetId!.Value, o.At));

    private List<(Guid Id, double X, double Y, double? DownAt, int Wounds)> Duelists() => [(_a, 6.5, 10.5, null, 0), (_b, 14.5, 10.5, null, 0)];
}
