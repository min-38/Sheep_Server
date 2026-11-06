using SheepServer.Arena;

namespace SheepServer.Shooting;

// 명중 판정 규칙 (기술 설계서 명중 판정). 방과 시계를 모르고 위치와 맵만 받아 테스트에서 바로 부를 수 있음
public static class HitScan
{
    // 칸. 사람을 이 반지름의 원으로 봄 (시스템 기획서 리볼버). ponytail: 임시 값. 플레이해 보며 정함
    public const double Radius = 0.25;

    // 칸. 벽을 찾을 때 광선을 따라 이만큼씩 나아감.
    // ponytail: 벽까지 거리가 이만큼 틀릴 수 있음. 벽에 붙은 사람의 판정이 어긋나면 클라 raycast.ts의 castRay(DDA)를 옮겨 옴
    private const double Step = 0.01;

    // (x, y)에서 angle 쪽으로 쏜 광선이 벽보다 먼저 닿는 첫 번째 사람. 없으면 null.
    // targets에는 쏜 사람과 쓰러진 사람을 빼고 넘김. 각도는 turn과 같음 (0은 +x, π/2는 +y)
    public static Guid? FirstHit(double x, double y, double angle, IEnumerable<(Guid Id, double X, double Y)> targets, ArenaMap map)
    {
        double dx = Math.Cos(angle);
        double dy = Math.Sin(angle);

        Guid? hit = null;
        double nearest = WallDistance(x, y, dx, dy, map);
        foreach (var target in targets)
        {
            // along은 광선을 따라 그 사람 옆까지 간 거리, side2는 광선에서 옆으로 떨어진 거리의 제곱
            double wx = target.X - x;
            double wy = target.Y - y;
            double along = wx * dx + wy * dy;
            double side2 = wx * wx + wy * wy - along * along;

            // 등 뒤에 있거나 옆으로 반지름보다 멀면 빗나감
            if (along <= 0 || side2 > Radius * Radius)
            {
                continue;
            }

            // 원의 가장자리에 처음 닿는 거리. 목록 순서가 아니라 가장 가까운 사람이 맞음
            double distance = along - Math.Sqrt(Radius * Radius - side2);
            if (distance < nearest)
            {
                hit = target.Id;
                nearest = distance;
            }
        }
        return hit;
    }

    // 광선이 처음 벽 칸에 들어가는 거리. 맵 밖도 벽으로 봐서 반드시 끝남
    private static double WallDistance(double x, double y, double dx, double dy, ArenaMap map)
    {
        for (double d = 0; ; d += Step)
        {
            int cx = (int)Math.Floor(x + dx * d);
            int cy = (int)Math.Floor(y + dy * d);
            if (cx < 0 || cy < 0 || cx >= map.Width || cy >= map.Height || map.Cells[cy][cx] != 0)
            {
                return d;
            }
        }
    }
}
