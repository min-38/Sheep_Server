namespace SheepServer.Rounds;

// 판 흐름 규칙. 시계와 방을 모르고 시각, 인원, 준비 수만 받아 테스트에서 바로 부를 수 있음
public static class RoundFlow
{
    // ms. 카운트다운 시각을 미리 알리는 시간. 지연이 가장 큰 사람도 시작 전에 알림을 받아야 함.
    // ponytail: 임시 값. 기술 설계서 미정 사항의 "미리 알리는 시간"을 플레이해 보며 정하면 그 값으로 바꿈
    public const double Lead = 500;

    // ms. 3, 2, 1을 이 간격마다 보여 줌 (시스템 기획서 판 흐름 시간). ponytail: 임시 값
    public const double CountdownInterval = 1000;

    // ms. ponytail: 임시 값. SHEEP-31에서 걷는 거리와 속도가 정해지면 그 값으로 바꿈
    public const double WalkDuration = 3000;

    // 틱마다 부름. 바뀔 것이 없으면 받은 current를 그대로 돌려줌
    public static Round Advance(Round current, double now, int players, int ready)
    {
        if (current.Next == null)
        {
            // 모두 준비했는지는 준비 중에만 봄. 결투 다음(승리, 재대결)은 이후 이슈
            if (current.Phase == RoundPhase.Lobby && players >= 2 && ready == players)
            {
                // 아직 준비 중임. 카운트다운을 Lead 뒤로 정해 두고 그때 넘어감
                return new Round(RoundPhase.Lobby, RoundPhase.Countdown, now + Lead);
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
            RoundPhase.Walk => new Round(RoundPhase.Walk, RoundPhase.Duel, current.NextAt + WalkDuration),
            // 결투 다음은 아직 없어 시각은 쓰지 않음
            RoundPhase.Duel => new Round(RoundPhase.Duel, null, 0),
            _ => throw new ArgumentOutOfRangeException(nameof(current)),
        };
    }
}
