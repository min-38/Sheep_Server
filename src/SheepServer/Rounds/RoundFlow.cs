namespace SheepServer.Rounds;

// 판 흐름 규칙. 시계와 방을 모르고 시각, 인원, 준비 수만 받아 테스트에서 바로 부를 수 있음
public static class RoundFlow
{
    // ms. 카운트다운과 "시작!" 시각을 미리 알리는 시간. 지연이 가장 큰 사람도 그 시각 전에 알림을 받아야 함.
    // ponytail: 임시 값. 기술 설계서 미정 사항의 "미리 알리는 시간"을 플레이해 보며 정하면 그 값으로 바꿈
    public const double Lead = 500;

    // ms. 3, 2, 1을 이 간격마다 보여 줌 (시스템 기획서 판 흐름 시간). ponytail: 임시 값
    public const double CountdownInterval = 1000;

    // 칸. 걷기가 끝나면 중심에서 StartPlacement.Radius + 이 거리만큼 떨어짐 (시스템 기획서 판 흐름 시간). ponytail: 임시 값. 플레이해 보며 정함
    public const double WalkDistance = 3;

    // 칸/초. 모두 같은 속도로 걸음 (시스템 기획서 판 흐름 시간). ponytail: 임시 값. 플레이해 보며 정함
    public const double WalkSpeed = 1;

    // ms. 걷는 거리와 속도로 정해짐. 거리 ÷ 속도는 초라서 1000을 곱함
    public const double WalkDuration = WalkDistance / WalkSpeed * 1000;

    // ms. 걷기가 끝나고 "시작!"까지 무작위로 기다리는 시간의 범위 (시스템 기획서 판 흐름 시간). ponytail: 임시 값. 플레이해 보며 정함
    public const double MinDuelWait = 1000;
    public const double MaxDuelWait = 3000;

    // 틱마다 부름. 바뀔 것이 없으면 받은 current를 그대로 돌려줌. seed는 걷기가 시작하는 틱에만 쓰임
    public static Round Advance(Round current, double now, int players, int ready, int seed)
    {
        if (current.Next == null)
        {
            // 모두 준비했는지는 준비 중에만 봄. 결투 다음(승리, 재대결)은 이후 이슈
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
            // 결투 다음은 아직 없어 시각은 쓰지 않음
            RoundPhase.Duel => new Round(RoundPhase.Duel, null, 0),
            _ => throw new ArgumentOutOfRangeException(nameof(current)),
        };
    }

    // 같은 시드면 같은 대기 시간. 테스트에서 시드를 고정해 결투 시각을 미리 알 수 있음
    public static double DuelWait(int seed)
    {
        return MinDuelWait + new Random(seed).NextDouble() * (MaxDuelWait - MinDuelWait);
    }
}
