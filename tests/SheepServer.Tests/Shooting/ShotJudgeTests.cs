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
            new Shot(_b, Math.PI, 100 + ShotJudge.MutualWindow + 1),
            new Shot(_a, 0, 100),
        };

        // Act
        var hits = ShotJudge.Judge(shots, Duelists(), ArenaMap.Fixed);

        // Assert
        // a가 먼저 맞힘. b는 맞고 D가 지난 뒤에 쏘아 그 발사는 무효
        Assert.Equal(new[] { (_b, 100.0) }, hits);
    }

    [Fact]
    public void Judge_HitBackWithinMutualWindow_BothDown()
    {
        // Arrange
        var shots = new[]
        {
            new Shot(_b, Math.PI, 100 + ShotJudge.MutualWindow),
            new Shot(_a, 0, 100),
        };

        // Act
        var hits = ShotJudge.Judge(shots, Duelists(), ArenaMap.Fixed);

        // Assert
        // b는 맞은 뒤 D 안에 쏘아 그 발사도 유효함. 둘 다 쓰러지고 맞은 시각은 맞힌 발사의 쏜 시각
        Assert.Equal(new[] { (_b, 100.0), (_a, 100 + ShotJudge.MutualWindow) }, hits);
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
        var hits = ShotJudge.Judge([new Shot(_a, 0, shotAt)], players, ArenaMap.Fixed);

        // Assert
        if (hit)
        {
            Assert.Equal(new[] { (_b, shotAt) }, hits);
        }
        else
        {
            Assert.Empty(hits);
        }
    }

    [Fact]
    public void Judge_DownPlayerInTheWay_RayPassesThrough()
    {
        // Arrange
        // b는 이미 쓰러짐. a에서 +x로 쏘면 b를 지나 c에 닿음
        var players = new List<(Guid Id, double X, double Y, double? DownAt)> { (_a, 6.5, 10.5, null), (_b, 10.5, 10.5, 50), (_c, 14.5, 10.5, null) };

        // Act
        var hits = ShotJudge.Judge([new Shot(_a, 0, 100)], players, ArenaMap.Fixed);

        // Assert
        Assert.Equal(new[] { (_c, 100.0) }, hits);
    }

    [Fact]
    public void Judge_ShooterLeftRoom_Ignored()
    {
        // Act
        // 판정 전에 나간 사람의 발사는 버림 (기술 설계서 메시지 fire)
        var hits = ShotJudge.Judge([new Shot(_c, 0, 100)], Duelists(), ArenaMap.Fixed);

        // Assert
        Assert.Empty(hits);
    }

    private List<(Guid Id, double X, double Y, double? DownAt)> Duelists() => [(_a, 6.5, 10.5, null), (_b, 14.5, 10.5, null)];
}
