namespace SheepServer.Arena;

// 판이 시작될 때 각 플레이어가 결투장 어디에 서서 어느 쪽을 볼지 정함 (기술 설계서 결투장)
public static class StartPlacement
{
    // 칸. ponytail: 임시 값. 걷는 거리와 함께 플레이하며 정함
    public const double Radius = 1.0;

    // 중심을 둘러싼 원 위에 count명을 같은 간격으로 세웠을 때 index번의 위치와 각도.
    // 각도는 라디안, 0은 +x, π/2는 +y (클라 makeCamera와 같음)
    public static (double X, double Y, double Angle) At(int count, int index, double centerX, double centerY)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, count);

        // 시곗바늘처럼 한 바퀴를 count등분한 index번째 방향
        double around = 2 * Math.PI * index / count;
        double x = centerX + Radius * Math.Cos(around);
        double y = centerY + Radius * Math.Sin(around);

        // 처음에는 서로 마주 봄. 바깥에 서 있으니 뒤로 돌면 중심을 봄
        double angle = (around + Math.PI) % (2 * Math.PI);

        return (x, y, angle);
    }

    // 판마다 시작 자리를 섞은 순서. 돌려준 배열의 k번 값은 k번 자리에 설 사람의 번호(들어온 순서)임.
    // 같은 시드면 같은 순서라 테스트에서 미리 알 수 있음 (SHEEP-186)
    public static int[] Order(int count, int seed)
    {
        int[] order = [.. Enumerable.Range(0, count)];
        new Random(seed).Shuffle(order);
        return order;
    }
}
