using SheepServer.Arena;

namespace SheepServer.Shooting;

// 쏜 시각 순서 판정 (기술 설계서 명중 판정). 방과 시계를 모르고 발사, 자리, 맵만 받아 지연을 흉내 낸 입력으로 테스트할 수 있음
public static class ShotJudge
{
    // ms. W. 쏜 시각을 도착 시각보다 이만큼까지만 앞당겨 주고, 발사를 이만큼 모았다가 판정함 (시스템 기획서 리볼버).
    // ponytail: 임시 값. 기술 설계서 미정 사항의 W를 플레이해 보며 정하면 그 값으로 바꿈
    public const double MaxDelay = 200;

    // ms. D. 맞은 뒤 이 시간 안에 쏜 발사는 유효해, 서로 맞히면 함께 쓰러짐 (시스템 기획서 판 흐름 시간).
    // ponytail: 임시 값. 기술 설계서 미정 사항의 D를 플레이해 보며 정하면 그 값으로 바꿈
    public const double MutualWindow = 100;

    // 쏜 시각을 도착 시각 - W 이상, 도착 시각 이하로 자름. 그래야 at에서 W만 기다리고 판정해도 뒤에 올 발사가 앞설 수 없음
    public static double ClampAt(double at, double arrivedAt) => Math.Clamp(at, arrivedAt - MaxDelay, arrivedAt);

    // shots를 쏜 시각 순서로 판정해, 맞은 사람과 맞은 시각(맞힌 발사의 쏜 시각)을 그 순서로 돌려줌.
    // players는 방 안 사람의 자리와 이미 맞은 시각(서 있으면 null). 목록에 없는 사람(나간 사람)의 발사는 버림
    public static List<(Guid Id, double At)> Judge(IEnumerable<Shot> shots, IReadOnlyList<(Guid Id, double X, double Y, double? DownAt)> players, ArenaMap map)
    {
        var downAt = new Dictionary<Guid, double?>();
        foreach (var player in players)
        {
            downAt[player.Id] = player.DownAt;
        }

        var hits = new List<(Guid Id, double At)>();

        // 쏜 시각이 같으면 받은 순서대로. OrderBy는 같은 값끼리의 순서를 지킴
        foreach (var shot in shots.OrderBy(s => s.At))
        {
            // 맞고 D가 지난 뒤에 쏜 발사는 무효. 받을 때는 아직 맞은 줄 몰랐을 수 있어 여기서 다시 봄
            if (!downAt.TryGetValue(shot.ShooterId, out var shooterDownAt) || (shooterDownAt != null && shot.At > shooterDownAt.Value + MutualWindow))
            {
                continue;
            }

            // 쏜 사람과 이미 맞은 사람은 맞지 않음. 맞은 사람은 광선이 지나감
            double x = 0;
            double y = 0;
            var targets = new List<(Guid Id, double X, double Y)>();
            foreach (var player in players)
            {
                if (player.Id == shot.ShooterId)
                {
                    x = player.X;
                    y = player.Y;
                }
                else if (downAt[player.Id] == null)
                {
                    targets.Add((player.Id, player.X, player.Y));
                }
            }

            var hit = HitScan.FirstHit(x, y, shot.Angle, targets, map);
            if (hit != null)
            {
                downAt[hit.Value] = shot.At;
                hits.Add((hit.Value, shot.At));
            }
        }
        return hits;
    }
}
