namespace SheepServer.Shooting;

// 쓰러뜨린 사람 알림 (기술 설계서 메시지 downedBy). 맞아 쓰러진 사람에게만, 같은 틱의 그 eliminated 바로 뒤에 보냄.
// 클라가 이 사람의 눈으로 내가 쓰러진 장면을 다시 그리고, 쓰러지는 장면에 누가 어디를 맞혔는지 띄우는 데 씀. 시간 초과(자살)는 쏜 사람이 없어 보내지 않음
public class DownedByMessage
{
    public DownedByMessage(Guid playerId, double at, HitZone zone)
    {
        PlayerId = playerId;
        At = at;
        Zone = zone;
    }

    public string Type => "downedBy";

    // 쓰러뜨린 사람
    public Guid PlayerId { get; }

    // 쓰러뜨린 발사의 쏜 시각(ms, 서버 시계). fire의 at을 자른 값. 클라가 기록한 state 중 어디까지 보일지 정함
    public double At { get; }

    // 쓰러뜨린 발사가 맞은 부위. 머리면 머리 즉사, 몸이면 두 번째 반샷 (SHEEP-119)
    public HitZone Zone { get; }
}
