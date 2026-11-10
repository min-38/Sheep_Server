namespace SheepServer.Tests.Shooting;

using SheepServer.Arena;
using SheepServer.Shooting;

// 기술 설계서 명중 판정: 쏜 사람 위치에서 그 각도로 광선을 쏴, 벽보다 먼저 닿는 첫 번째 사람이 맞음.
// 사람은 몸과 머리 두 원기둥. 각도는 turn과 같음 (0은 +x, π/2는 +y), pitch는 위아래 각도(위가 +). 광선은 눈높이에서 출발함
public class HitScanTests
{
    private readonly Guid _b = Guid.NewGuid();
    private readonly Guid _c = Guid.NewGuid();

    [Fact]
    public void FirstHit_TargetStraightAhead_Hit()
    {
        // Act
        var hit = HitScan.FirstHit(2.5, 5.5, 0, 0, [(_b, 6.5, 5.5)], Map());

        // Assert
        Assert.Equal(_b, hit?.Id);
    }

    [Fact]
    public void FirstHit_TargetStraightAhead_HitPointOnNearSurface()
    {
        // Act
        var hit = HitScan.FirstHit(2.5, 5.5, 0, 0, [(_b, 6.5, 5.5)], Map());

        // Assert
        // 맞은 자리는 광선이 몸 원기둥 옆면에 처음 닿는 곳. 피와 상처를 그 자리에 그림 (SHEEP-126)
        Assert.Equal(6.5 - HitScan.BodyRadius, hit!.X, 9);
        Assert.Equal(5.5, hit.Y, 9);
        Assert.Equal(HitScan.EyeHeight, hit.Height, 9);
    }

    [Fact]
    public void FirstHit_TargetBehind_Miss()
    {
        // Act
        // 광선은 앞으로만 나감. 반대쪽(π)을 보고 쏘면 등 뒤의 사람은 맞지 않음
        var hit = HitScan.FirstHit(2.5, 5.5, Math.PI, 0, [(_b, 6.5, 5.5)], Map());

        // Assert
        Assert.Null(hit);
    }

    [Fact]
    public void FirstHit_ShootingDown_HitsTargetBelow()
    {
        // Act
        // π/2는 +y. 격자에서는 아래쪽임
        var hit = HitScan.FirstHit(5.5, 2.5, Math.PI / 2, 0, [(_b, 5.5, 7.5)], Map());

        // Assert
        Assert.Equal(_b, hit?.Id);
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
        var result = HitScan.FirstHit(2.5, 5.5, 0, 0, [(_b, 6.5, 5.5 + radii * HitScan.BodyRadius)], Map());

        // Assert
        Assert.Equal(expected, result?.Id);
    }

    [Fact]
    public void FirstHit_WallBetween_Miss()
    {
        // Act
        // (4, 5) 칸이 벽. 쏜 사람(x 2.5)과 맞을 사람(x 6.5) 사이에 있음
        var hit = HitScan.FirstHit(2.5, 5.5, 0, 0, [(_b, 6.5, 5.5)], Map((4, 5)));

        // Assert
        Assert.Null(hit);
    }

    [Fact]
    public void FirstHit_WallBehindTarget_Hit()
    {
        // Act
        // (8, 5) 칸이 벽이지만 맞을 사람보다 멀리 있음. 광선 위에 벽이 있다고 다 막는 것이 아님
        var hit = HitScan.FirstHit(2.5, 5.5, 0, 0, [(_b, 6.5, 5.5)], Map((8, 5)));

        // Assert
        Assert.Equal(_b, hit?.Id);
    }

    [Fact]
    public void FirstHit_TwoInLine_NearerOneHit()
    {
        // Act
        // 먼 사람을 목록 앞에 둠. 목록 순서가 아니라 거리로 골라야 함
        var hit = HitScan.FirstHit(2.5, 5.5, 0, 0, [(_c, 8.5, 5.5), (_b, 6.5, 5.5)], Map());

        // Assert
        Assert.Equal(_b, hit?.Id);
    }

    [Fact]
    public void FirstHit_NoTargets_Null()
    {
        // Act
        var hit = HitScan.FirstHit(2.5, 5.5, 0, 0, [], Map());

        // Assert
        Assert.Null(hit);
    }

    // 아래 테스트는 (5, 5)에서 +x(0)로 쏘고, 맞을 사람은 3칸 앞 (8, 5)에 섬
    [Fact]
    public void FirstHit_Level_BodyAtEyeHeight()
    {
        // Act
        var hit = HitScan.FirstHit(5, 5, 0, 0, [(_b, 8, 5)], Map());

        // Assert
        // 수평으로 쏘면 눈높이에서 몸에 맞음
        Assert.NotNull(hit);
        Assert.Equal(_b, hit.Id);
        Assert.Equal(HitZone.Body, hit.Zone);
        Assert.Equal(HitScan.EyeHeight, hit.Height, 1e-9);
        Assert.Equal(0, hit.Offset, 1e-9);
    }

    [Fact]
    public void FirstHit_AimUpAtHead_Head()
    {
        // Arrange
        // 머리 옆면(앞으로 3 - HeadRadius)에서 눈높이보다 0.34 위. 몸 꼭대기(0.78)와 머리 꼭대기(0.9) 사이임
        double pitch = Math.Atan(0.34 / (3 - HitScan.HeadRadius));

        // Act
        var hit = HitScan.FirstHit(5, 5, 0, pitch, [(_b, 8, 5)], Map());

        // Assert
        Assert.NotNull(hit);
        Assert.Equal(HitZone.Head, hit.Zone);
        Assert.Equal(HitScan.EyeHeight + 0.34, hit.Height, 1e-9);
    }

    [Fact]
    public void FirstHit_BelowShoulder_Body()
    {
        // Arrange
        // 몸 옆면(앞으로 3 - BodyRadius)에서 높이 0.75. 7.5등신 틀에서는 어깨 아래라 몸임 (시스템 기획서 리볼버, SHEEP-89)
        double pitch = Math.Atan(0.25 / (3 - HitScan.BodyRadius));

        // Act
        var hit = HitScan.FirstHit(5, 5, 0, pitch, [(_b, 8, 5)], Map());

        // Assert
        Assert.NotNull(hit);
        Assert.Equal(HitZone.Body, hit.Zone);
        Assert.Equal(0.75, hit.Height, 1e-9);
    }

    [Fact]
    public void FirstHit_BesideBodyAtEyeHeight_Miss()
    {
        // Act
        // 광선에서 옆으로 0.2. 7.5등신 몸 반지름(0.15) 밖이라 눈높이로 쏴도 옆을 지나감 (SHEEP-89)
        var hit = HitScan.FirstHit(5, 5, 0, 0, [(_b, 8, 5.2)], Map());

        // Assert
        Assert.Null(hit);
    }

    [Fact]
    public void FirstHit_AimOverHead_Miss()
    {
        // Act
        var hit = HitScan.FirstHit(5, 5, 0, 0.3, [(_b, 8, 5)], Map());

        // Assert
        Assert.Null(hit);
    }

    [Fact]
    public void FirstHit_AimBelowFloor_Miss()
    {
        // Act
        // 사람에 닿기 전에 바닥(높이 0) 아래로 내려감
        var hit = HitScan.FirstHit(5, 5, 0, -0.3, [(_b, 8, 5)], Map());

        // Assert
        Assert.Null(hit);
    }

    [Fact]
    public void FirstHit_HeadHeightBesideHead_Miss()
    {
        // Arrange
        // 맞을 사람이 광선에서 옆으로 0.1. 몸 반지름(0.15) 안이지만 머리 반지름(0.05) 밖임. 머리 높이로 쏘면 머리 옆을 지나감
        double pitch = Math.Atan(0.34 / (3 - HitScan.HeadRadius));

        // Act
        var hit = HitScan.FirstHit(5, 5, 0, pitch, [(_b, 8, 5.1)], Map());

        // Assert
        Assert.Null(hit);
    }

    [Fact]
    public void FirstHit_TargetRightOfRay_NegativeOffset()
    {
        // Act
        // +x를 보면 +y가 오른쪽임 (render.ts의 plane과 같음). 맞을 사람의 중심이 광선 오른쪽에 있어 맞은 자리는 중심의 왼쪽
        var hit = HitScan.FirstHit(5, 5, 0, 0, [(_b, 8, 5.1)], Map());

        // Assert
        Assert.NotNull(hit);
        Assert.Equal(-0.1, hit.Offset, 1e-9);
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
