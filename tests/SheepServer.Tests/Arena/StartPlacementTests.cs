namespace SheepServer.Tests.Arena;

using SheepServer.Arena;

public class StartPlacementTests
{
    private const double Tolerance = 1e-9;
    private static readonly double CenterX = ArenaMap.Fixed.Width / 2.0;
    private static readonly double CenterY = ArenaMap.Fixed.Height / 2.0;

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void At_EveryCount_StandsRadiusFromCenter(int count)
    {
        // Act
        var placements = PlaceAll(count);

        // Assert
        foreach (var p in placements)
        {
            Assert.Equal(StartPlacement.Radius, Distance(p.X, p.Y, CenterX, CenterY), Tolerance);
        }
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void At_EveryCount_NeighborsEquallySpaced(int count)
    {
        // Act
        var placements = PlaceAll(count);

        // Assert
        // 각도 대신 거리로 비교함. 각도는 마지막 사람에서 첫 사람으로 넘어갈 때 2π를 지나 값이 꼬임
        double first = Distance(placements[0].X, placements[0].Y, placements[1].X, placements[1].Y);
        for (int i = 0; i < count; i++)
        {
            var next = placements[(i + 1) % count];
            Assert.Equal(first, Distance(placements[i].X, placements[i].Y, next.X, next.Y), Tolerance);
        }
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void At_EveryCount_FacesCenter(int count)
    {
        // Act
        var placements = PlaceAll(count);

        // Assert
        // 바라보는 방향과 (위치 - 중심)의 내적이 -R이면 정확히 중심을 봄 (컨셉 기획서: 서로 마주 봄)
        foreach (var p in placements)
        {
            double dot = Math.Cos(p.Angle) * (p.X - CenterX) + Math.Sin(p.Angle) * (p.Y - CenterY);
            Assert.Equal(-StartPlacement.Radius, dot, Tolerance);
        }
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void At_EveryCount_NotOnWall(int count)
    {
        // Act
        var placements = PlaceAll(count);

        // Assert
        foreach (var p in placements)
        {
            Assert.Equal(0, ArenaMap.Fixed.Cells[(int)p.Y][(int)p.X]);
        }
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(2, 2)]
    [InlineData(2, -1)]
    public void At_OutOfRange_Throws(int count, int index)
    {
        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => StartPlacement.At(count, index, CenterX, CenterY));
    }

    [Fact]
    public void Order_SameSeed_SameOrder()
    {
        // Act & Assert
        Assert.Equal(StartPlacement.Order(5, 42), StartPlacement.Order(5, 42));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(8)]
    public void Order_EveryCount_EachSpotOnce(int count)
    {
        // Act & Assert
        // 모두 다른 자리에 서고, 빠지거나 겹치는 자리가 없음 (SHEEP-186)
        for (int seed = 0; seed < 50; seed++)
        {
            Assert.Equal(Enumerable.Range(0, count), StartPlacement.Order(count, seed).Order());
        }
    }

    [Fact]
    public void Order_ManySeeds_EveryOrderOfThreeAppears()
    {
        // Act
        var seen = Enumerable.Range(0, 200).Select(seed => string.Join(",", StartPlacement.Order(3, seed))).ToHashSet();

        // Assert
        // 셋이 설 수 있는 순서 6가지가 모두 나옴. 들어온 순서로만 서지 않음
        Assert.Equal(6, seen.Count);
    }

    private static (double X, double Y, double Angle)[] PlaceAll(int count) =>
        Enumerable.Range(0, count).Select(i => StartPlacement.At(count, i, CenterX, CenterY)).ToArray();

    private static double Distance(double x1, double y1, double x2, double y2) =>
        Math.Sqrt((x2 - x1) * (x2 - x1) + (y2 - y1) * (y2 - y1));
}
