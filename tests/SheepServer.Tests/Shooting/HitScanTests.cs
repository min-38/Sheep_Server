namespace SheepServer.Tests.Shooting;

using SheepServer.Arena;
using SheepServer.Shooting;

// 기술 설계서 명중 판정: 쏜 사람 위치에서 그 각도로 광선을 쏴, 벽보다 먼저 닿는 첫 번째 사람이 맞음.
// 사람은 반지름 HitScan.Radius인 원. 각도는 turn과 같음 (0은 +x, π/2는 +y)
public class HitScanTests
{
    private readonly Guid _b = Guid.NewGuid();
    private readonly Guid _c = Guid.NewGuid();

    [Fact]
    public void FirstHit_TargetStraightAhead_Hit()
    {
        // Act
        var hit = HitScan.FirstHit(2.5, 5.5, 0, [(_b, 6.5, 5.5)], Map());

        // Assert
        Assert.Equal(_b, hit);
    }

    [Fact]
    public void FirstHit_TargetBehind_Miss()
    {
        // Act
        // 광선은 앞으로만 나감. 반대쪽(π)을 보고 쏘면 등 뒤의 사람은 맞지 않음
        var hit = HitScan.FirstHit(2.5, 5.5, Math.PI, [(_b, 6.5, 5.5)], Map());

        // Assert
        Assert.Null(hit);
    }

    [Fact]
    public void FirstHit_ShootingDown_HitsTargetBelow()
    {
        // Act
        // π/2는 +y. 격자에서는 아래쪽임
        var hit = HitScan.FirstHit(5.5, 2.5, Math.PI / 2, [(_b, 5.5, 7.5)], Map());

        // Assert
        Assert.Equal(_b, hit);
    }

    // 광선에서 옆으로 반지름의 0.9배 떨어진 사람은 스치듯 맞고, 1.1배면 빗나감
    [Theory]
    [InlineData(0.9, true)]
    [InlineData(1.1, false)]
    public void FirstHit_TargetBesideRay_HitOnlyWithinRadius(double radii, bool hit)
    {
        // Arrange
        Guid? expected = hit ? _b : null;

        // Act
        var result = HitScan.FirstHit(2.5, 5.5, 0, [(_b, 6.5, 5.5 + radii * HitScan.Radius)], Map());

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void FirstHit_WallBetween_Miss()
    {
        // Act
        // (4, 5) 칸이 벽. 쏜 사람(x 2.5)과 맞을 사람(x 6.5) 사이에 있음
        var hit = HitScan.FirstHit(2.5, 5.5, 0, [(_b, 6.5, 5.5)], Map((4, 5)));

        // Assert
        Assert.Null(hit);
    }

    [Fact]
    public void FirstHit_WallBehindTarget_Hit()
    {
        // Act
        // (8, 5) 칸이 벽이지만 맞을 사람보다 멀리 있음. 광선 위에 벽이 있다고 다 막는 것이 아님
        var hit = HitScan.FirstHit(2.5, 5.5, 0, [(_b, 6.5, 5.5)], Map((8, 5)));

        // Assert
        Assert.Equal(_b, hit);
    }

    [Fact]
    public void FirstHit_TwoInLine_NearerOneHit()
    {
        // Act
        // 먼 사람을 목록 앞에 둠. 목록 순서가 아니라 거리로 골라야 함
        var hit = HitScan.FirstHit(2.5, 5.5, 0, [(_c, 8.5, 5.5), (_b, 6.5, 5.5)], Map());

        // Assert
        Assert.Equal(_b, hit);
    }

    [Fact]
    public void FirstHit_NoTargets_Null()
    {
        // Act
        var hit = HitScan.FirstHit(2.5, 5.5, 0, [], Map());

        // Assert
        Assert.Null(hit);
    }

    // 11×11칸, 둘레만 벽인 맵. walls의 (x, y) 칸도 벽으로 만듦
    private static ArenaMap Map(params (int X, int Y)[] walls)
    {
        const int Size = 11;
        var cells = new int[Size][];
        for (int y = 0; y < Size; y++)
        {
            cells[y] = new int[Size];
            for (int x = 0; x < Size; x++)
            {
                if (x == 0 || y == 0 || x == Size - 1 || y == Size - 1)
                {
                    cells[y][x] = 1;
                }
            }
        }
        foreach (var (x, y) in walls)
        {
            cells[y][x] = 1;
        }
        return new ArenaMap(Size, Size, cells);
    }
}
