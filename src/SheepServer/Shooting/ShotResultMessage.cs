namespace SheepServer.Shooting;

// 사격 결과 (기술 설계서 메시지 shotResult). 판정한 틱에 쏜 사람에게만 보냄. 누구를 맞혔는지는 알리지 않음
public class ShotResultMessage
{
    // 빗나갔으면 부위, 높이, 오른쪽 거리를 모두 null로 보냄. 0이면 몸 한가운데 바닥에 맞은 것과 구별되지 않음
    public ShotResultMessage(ShotOutcome outcome)
    {
        bool hit = outcome.TargetId != null;
        Zone = outcome.Zone;
        Height = hit ? outcome.Height : null;
        Offset = hit ? outcome.Offset : null;
        Down = outcome.Down;
    }

    public string Type => "shotResult";
    public HitZone? Zone { get; }

    // 칸. 맞은 높이, 바닥이 0
    public double? Height { get; }

    // 칸. 맞은 자리가 맞은 사람 중심에서 쏜 사람 기준 오른쪽으로 떨어진 거리
    public double? Offset { get; }
    public bool Down { get; }
}
