using SheepServer.Shooting;

namespace SheepServer.Rounds;

// 판 흐름 규칙. 시계와 방을 모르고 시각, 인원, 준비 수만 받아 테스트에서 바로 부를 수 있음
public static class RoundFlow
{
    // ms. 카운트다운과 "시작!" 시각을 미리 알리는 시간. 지연이 가장 큰 사람도 그 시각 전에 알림을 받아야 함.
    // ponytail: 임시 값. 기술 설계서 미정 사항의 "미리 알리는 시간"을 플레이해 보며 정하면 그 값으로 바꿈
    public const double Lead = 500;

    // ms. 3, 2, 1을 이 간격마다 보여 줌 (시스템 기획서 판 흐름 시간). ponytail: 임시 값
    public const double CountdownInterval = 1000;

    // 칸. 최대 걸음. 멈추지 않으면 걷기가 끝날 때 중심에서 StartPlacement.Radius + 이 거리만큼 떨어짐 (시스템 기획서 판 흐름 시간).
    // 걷다 멈춘 사람은 그보다 덜 감. ponytail: 임시 값. 플레이해 보며 정함
    public const double WalkDistance = 3;

    // 칸/초. 모두 같은 속도로 걸음 (시스템 기획서 판 흐름 시간). ponytail: 임시 값. 플레이해 보며 정함
    public const double WalkSpeed = 1;

    // ms. 걷는 거리와 속도로 정해짐. 거리 ÷ 속도는 초라서 1000을 곱함
    public const double WalkDuration = WalkDistance / WalkSpeed * 1000;

    // ms. 걷기가 끝나고 "시작!"까지 무작위로 기다리는 시간의 범위 (시스템 기획서 판 흐름 시간). ponytail: 임시 값. 플레이해 보며 정함
    public const double MinDuelWait = 1000;
    public const double MaxDuelWait = 3000;

    // ms. 결투 시각("시작!")부터 이 시간 안에 쏘지 않은 사람은 자살로 쓰러짐 (시스템 기획서 판 흐름 시간).
    // 클라가 남은 시간을 보여 줄 때(round.ts DUEL_LIMIT, SHEEP-64) 같은 값을 써야 함. 5초는 사용자 결정 (2026-11-10, SHEEP-127)
    public const double DuelLimit = 5000;

    // ms. 판 결과를 보여 주는 시간 (시스템 기획서 판 흐름 시간). 이 시간이 지나면 승리면 준비 중으로, 다음 라운드면 카운트다운으로 넘어감.
    // ponytail: 임시 값. 플레이해 보며 정함
    public const double ResultDuration = 3000;

    // 틱마다 부름. 바뀔 것이 없으면 받은 current를 그대로 돌려줌. seed는 걷기가 시작하는 틱에만 쓰임
    public static Round Advance(Round current, double now, int players, int ready, int seed)
    {
        if (current.Next == null)
        {
            // 모두 준비했는지는 준비 중에만 봄. 결투 다음(승리, 재대결)은 결과가 정해질 때 Room이 정함
            if (current.Phase == RoundPhase.Lobby && players >= 2 && ready == players)
            {
                // 아직 준비 중임. 카운트다운을 Lead 뒤로 정해 두고 그때 넘어감
                return new Round(RoundPhase.Lobby, RoundPhase.Countdown, now + Lead);
            }

            // 걷기가 시작할 때 정해 둔 결투 시각을 Lead 전에야 알림. Next가 생기면 Room이 알림을 보냄
            if (current.Phase == RoundPhase.Walk && now >= current.NextAt - Lead)
            {
                return current with { Next = RoundPhase.Duel };
            }
            return current;
        }

        if (now < current.NextAt)
        {
            return current;
        }

        // 다음 시각은 늦게 온 틱(now)이 아니라 정해 둔 시각(NextAt)에 더함. 그래야 미리 알린 시각과 어긋나지 않음
        return current.Next switch
        {
            RoundPhase.Countdown => new Round(RoundPhase.Countdown, RoundPhase.Walk, current.NextAt + 3 * CountdownInterval),
            // 결투 시각은 다 걷고 무작위로 더 기다린 뒤. 정해 두기만 하고 Next를 비워 아직 알리지 않음
            RoundPhase.Walk => new Round(RoundPhase.Walk, null, current.NextAt + WalkDuration + DuelWait(seed)),
            // 결투 다음은 결과가 정해져야 알 수 있어 시각은 쓰지 않음
            RoundPhase.Duel => new Round(RoundPhase.Duel, null, 0),
            // 승리 결과를 다 보여 주면 준비 중으로 돌아감. 다음 판은 다시 모두 준비할 때 정함
            RoundPhase.Lobby => new Round(RoundPhase.Lobby, null, 0),
            _ => throw new ArgumentOutOfRangeException(nameof(current)),
        };
    }

    // 결투 중 판 결과를 정함 (컨셉 기획서 게임 루프, 규칙). 결과가 없어 결투가 이어지면 null.
    // alive는 쓰러지지 않은 사람과 남은 총알, lastDown은 방에 남은 사람 중 가장 늦게 쓰러진 사람들(함께 쓰러졌으면 여럿)
    public static ResultMessage? Result(IReadOnlyList<(Guid Id, int Ammo)> alive, IReadOnlyList<Guid> lastDown)
    {
        if (alive.Count == 1)
        {
            return new ResultMessage(Outcome.Win, new List<Guid> { alive[0].Id }, 0);
        }

        if (alive.Count == 0)
        {
            // 방에 아무도 없음. 방이 지워지므로 정할 것이 없음
            if (lastDown.Count == 0)
            {
                return null;
            }

            // 살아 있던 사람이 모두 나가 혼자 남으면 다음 라운드를 할 수 없음. 상대가 모두 기권한 것으로 보고 그 사람이 이김
            if (lastDown.Count == 1)
            {
                return new ResultMessage(Outcome.Win, new List<Guid> { lastDown[0] }, 0);
            }

            // 무승부로 보고 마지막에 함께 쓰러진 사람끼리 다음 라운드로 감
            return new ResultMessage(Outcome.Rematch, lastDown, Revolver.Capacity);
        }

        foreach (var player in alive)
        {
            if (player.Ammo > 0)
            {
                return null;
            }
        }

        // 2명 이상 살았는데 모두 쏴서 총알이 없음. 살아남은 사람끼리 다음 라운드로 가고 다시 한 발씩 장전함
        var ids = new List<Guid>();
        foreach (var player in alive)
        {
            ids.Add(player.Id);
        }
        return new ResultMessage(Outcome.Rematch, ids, Revolver.Capacity);
    }

    // 같은 시드면 같은 대기 시간. 테스트에서 시드를 고정해 결투 시각을 미리 알 수 있음
    public static double DuelWait(int seed)
    {
        return MinDuelWait + new Random(seed).NextDouble() * (MaxDuelWait - MinDuelWait);
    }
}
