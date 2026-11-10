namespace SheepServer.Shooting;

// 반샷 알림 (기술 설계서 메시지 wounded). 몸을 맞았지만 쓰러지지 않은 틱에 방 안 모두에게 보냄.
// 맞은 사람과 맞은 자리(높이, 격자 좌표)를 담아 모두가 피와 상처를 봄. 누가 쐈는지는 담지 않음 (컨셉 기획서 규칙 반샷, SHEEP-126)
public class WoundedMessage
{
    public WoundedMessage(Guid playerId, double height, double x, double y)
    {
        PlayerId = playerId;
        Height = height;
        X = x;
        Y = y;
    }

    public string Type => "wounded";
    public Guid PlayerId { get; }

    // 반샷은 몸을 맞았을 때만 남. 머리는 바로 쓰러져 eliminated로 감
    public HitZone Zone => HitZone.Body;

    // 맞은 높이(칸, 바닥 0)
    public double Height { get; }
    public double X { get; }
    public double Y { get; }
}
