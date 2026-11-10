using SheepServer.Shooting;

namespace SheepServer.Rounds;

// 판 흐름 규칙. 시계와 방을 모르고 시각, 인원, 준비 수만 받아 테스트에서 바로 부를 수 있음
public static class RoundFlow
{
    // ms. 카운트다운과 "시작!" 시각을 미리 알리는 시간. 지연이 가장 큰 사람도 그 시각 전에 알림을 받아야 함.
    // ponytail: 임시 값. 기술 설계서 미정 사항의 "미리 알리는 시간"을 플레이해 보며 정하면 그 값으로 바꿈
    public const double Lead = 500;

    // ms. 카운트다운 길이. 그동안 모두 함께 2.5초에 걸쳐 장전하고 총을 권총집에 넣음 (사용자 결정 2026-11-10, SHEEP-121).
    // 장전 모습과 소리는 클라가 이 시각에서 잼
    public const double CountdownDuration = 4000;

    // ms. 걷기 시각에 중심을 보던 사람이 제자리에서 바깥으로 천천히 뒤도는 시간. 그 뒤에 걸음 (사용자 결정 2026-11-09, SHEEP-120).
    // ponytail: 임시 값(약 1초로 시작). 플레이해 보며 정함
    public const double TurnDuration = 1000;

    // 모두 멈추지 않고 Steps걸음을 StepInterval(ms)마다 StepLength(칸)씩 걸음 (사용자 결정 2026-11-10, SHEEP-120).
    // 걸음 길이는 마주 선 거리 2 × (StartPlacement.Radius + WalkDistance) = 8칸을 그대로 두려고 0.3칸
    public const int Steps = 10;
    public const double StepLength = 0.3;
    public const double StepInterval = 500;

    // 칸. 다 걸으면 중심에서 StartPlacement.Radius + 이 거리만큼 떨어짐 (시스템 기획서 판 흐름 시간)
    public const double WalkDistance = Steps * StepLength;

    // ms. 다 돈 뒤 걷는 시간. 클라 round.ts WALK_DURATION과 같아야 발소리가 걸음과 맞음
    public const double WalkDuration = Steps * StepInterval;

    // ms. 걷기가 끝나고 "시작!"까지 무작위로 기다리는 시간의 범위 (시스템 기획서 판 흐름 시간). ponytail: 임시 값. 플레이해 보며 정함
    public const double MinDuelWait = 1000;
    public const double MaxDuelWait = 3000;

    // ms. 결투 시각("시작!")부터 이 시간 안에 쏘지 않은 사람은 자살로 쓰러짐 (시스템 기획서 판 흐름 시간).
    // 클라가 남은 시간을 보여 줄 때(round.ts DUEL_LIMIT, SHEEP-64) 같은 값을 써야 함. 5초는 사용자 결정 (2026-11-10, SHEEP-127)
    public const double DuelLimit = 5000;

    // ms. 재대결 결과를 보여 주는 시간 (시스템 기획서 판 흐름 시간). 이 시간이 지나면 다음 라운드 카운트다운으로 넘어감.
    // ponytail: 임시 값. 플레이해 보며 정함
    public const double ResultDuration = 3000;

    // ms. 승리 결과를 보여 주는 시간. 포스터 뒤에 이긴 사람이 해 쪽으로 걸어가고 화면이 동그랗게 닫히는 세레모니를 보여 준 뒤
    // 준비 중으로 돌아감 (사용자 결정 2026-11-10, SHEEP-136). 클라 ceremony.ts가 이 끝 시각에서 거꾸로 잼. ponytail: 임시 값. 플레이해 보며 정함
    public const double WinResultDuration = 8500;

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
            RoundPhase.Countdown => new Round(RoundPhase.Countdown, RoundPhase.Walk, current.NextAt + CountdownDuration),
            // 결투 시각은 돌고 다 걷고 무작위로 더 기다린 뒤. 정해 두기만 하고 Next를 비워 아직 알리지 않음
            RoundPhase.Walk => new Round(RoundPhase.Walk, null, current.NextAt + TurnDuration + WalkDuration + DuelWait(seed)),
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
