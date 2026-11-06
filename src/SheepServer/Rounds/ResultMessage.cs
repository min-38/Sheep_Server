namespace SheepServer.Rounds;

// 판 결과 (기술 설계서 메시지 result). 결투 중 승리나 재대결이 정해지면 방 안 모두에게 한 번 보내고, 바로 뒤에 다음 단계를 phase로 알림
public class ResultMessage
{
    public ResultMessage(Outcome outcome, IReadOnlyList<Guid> playerIds, int rounds)
    {
        Outcome = outcome;
        PlayerIds = playerIds;
        Rounds = rounds;
    }

    public string Type => "result";
    public Outcome Outcome { get; }

    // 이긴 사람 1명, 또는 재대결하는 사람들
    public IReadOnlyList<Guid> PlayerIds { get; }

    // 재대결하는 사람이 새로 장전하는 총알 수. 재대결하는 사람 수와 같고 승리면 0
    public int Rounds { get; }
}
