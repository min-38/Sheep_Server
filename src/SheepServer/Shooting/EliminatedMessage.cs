namespace SheepServer.Shooting;

// 탈락 (기술 설계서 메시지 eliminated). 명중을 정한 틱(맞힌 발사의 쏜 시각에서 W가 지난 뒤)이나 시간 초과를 정한 틱에 방 안 모두에게 보냄.
// 한 결투에 한 사람마다 한 번만(재대결은 새 결투). 맞아 쓰러졌으면 쓰러뜨린 발사가 맞은 자리(부위, 높이, 격자 좌표)를 담고, 시간 초과면 모두 null (SHEEP-126)
public class EliminatedMessage
{
    public EliminatedMessage(Guid playerId, EliminationReason reason, HitZone? zone = null, double? height = null, double? x = null, double? y = null)
    {
        PlayerId = playerId;
        Reason = reason;
        Zone = zone;
        Height = height;
        X = x;
        Y = y;
    }

    public string Type => "eliminated";
    public Guid PlayerId { get; }
    public EliminationReason Reason { get; }
    public HitZone? Zone { get; }
    public double? Height { get; }
    public double? X { get; }
    public double? Y { get; }
}
