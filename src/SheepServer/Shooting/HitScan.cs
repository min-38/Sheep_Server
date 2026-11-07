using SheepServer.Arena;

namespace SheepServer.Shooting;

// 명중 판정 규칙 (기술 설계서 명중 판정). 방과 시계를 모르고 위치와 맵만 받아 테스트에서 바로 부를 수 있음
public static class HitScan
{
    // 칸. 사람을 몸과 머리 두 원기둥으로 봄. 7.5등신 틀에서 키를 0.9칸으로 보고 잼. 모자는 판정에 넣지 않음 (시스템 기획서 리볼버, SHEEP-89).
    // 클라 hitcard.ts의 BODY, HEAD와 같은 값이어야 사격 결과 카드(SHEEP-62)의 총알 구멍이 맞은 자리에 찍힘. ponytail: 임시 값. 플레이해 보며 정함
    public const double EyeHeight = 0.5;
    public const double BodyRadius = 0.15;
    public const double BodyTop = 0.78;
    public const double HeadRadius = 0.05;
    public const double HeadTop = 0.9;

    // 칸. 벽을 찾을 때 광선을 따라 이만큼씩 나아감.
    // ponytail: 벽까지 거리가 이만큼 틀릴 수 있음. 벽에 붙은 사람의 판정이 어긋나면 클라 raycast.ts의 castRay(DDA)를 옮겨 옴
    private const double Step = 0.01;

    // 원기둥 하나. 바닥 높이 Bottom부터 Top까지
    private static readonly (HitZone Zone, double Radius, double Bottom, double Top)[] Parts =
    [
        (HitZone.Body, BodyRadius, 0, BodyTop),
        (HitZone.Head, HeadRadius, BodyTop, HeadTop),
    ];

    // (x, y)의 눈높이에서 angle 쪽으로, 위아래 pitch만큼 기울여 쏜 광선이 벽보다 먼저 닿는 첫 번째 사람. 없으면 null.
    // targets에는 쏜 사람과 쓰러진 사람을 빼고 넘김. 각도는 turn과 같음 (0은 +x, π/2는 +y). pitch는 위가 +
    public static ScanHit? FirstHit(double x, double y, double angle, double pitch, IEnumerable<(Guid Id, double X, double Y)> targets, ArenaMap map)
    {
        double dx = Math.Cos(angle);
        double dy = Math.Sin(angle);

        // 광선 높이는 수평으로 간 거리에 tan(pitch)를 곱한 만큼 바뀜. 클라 render.ts의 horizonY와 같은 식이라 조준점과 맞음
        double rise = Math.Tan(pitch);

        ScanHit? hit = null;
        double nearest = WallDistance(x, y, dx, dy, map);
        foreach (var target in targets)
        {
            // along은 광선을 따라 그 사람 옆까지 간 거리, side2는 광선에서 옆으로 떨어진 거리의 제곱
            double wx = target.X - x;
            double wy = target.Y - y;
            double along = wx * dx + wy * dy;
            double side2 = wx * wx + wy * wy - along * along;

            foreach (var part in Parts)
            {
                // 등 뒤에 있거나 옆으로 반지름보다 멀면 빗나감
                if (along <= 0 || side2 > part.Radius * part.Radius)
                {
                    continue;
                }

                // 원기둥 옆면에 처음 닿는 수평 거리와 그때 광선 높이. 그 높이가 이 원기둥 안이면 맞음.
                // ponytail: 옆면만 봄. 위에서 내려와 머리 뚜껑이나 몸 윗면으로 들어가는 광선은 빗나감으로 침. 판정이 어색하면 뚜껑도 봄
                double distance = along - Math.Sqrt(part.Radius * part.Radius - side2);
                double height = EyeHeight + distance * rise;
                if (height < part.Bottom || height > part.Top)
                {
                    continue;
                }

                // 목록 순서가 아니라 가장 가까운 것이 맞음. (−dy, dx)가 쏜 사람의 오른쪽이고, 맞은 자리는 광선 위라 중심의 반대쪽으로 떨어져 있음
                if (distance < nearest)
                {
                    hit = new ScanHit(target.Id, part.Zone, height, -(wx * -dy + wy * dx));
                    nearest = distance;
                }
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
