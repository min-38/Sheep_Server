namespace SheepServer.Rounds;

// 판 단계 알림 (기술 설계서 메시지 phase). 그 단계가 시작하기 전에 방 안 모두에게 한 번 보냄
public class PhaseMessage
{
    public PhaseMessage(RoundPhase phase, double at)
    {
        Phase = phase;
        At = at;
    }

    public string Type => "phase";
    public RoundPhase Phase { get; }

    // 그 단계가 시작하는 서버 시각(ms). serverTime의 ts와 같은 시계
    public double At { get; }
}
