namespace SheepServer.Rooms;

// 관전 시작의 답 (기술 설계서 메시지 spectating). 받아들인 spectate에만 관전자에게 보냄.
// 관전을 시작할 때 대상의 혈흔과 실린더를 그리는 데 씀. 그 뒤 바뀌는 것은 shot과 복사된 wounded로 앎
public class SpectatingMessage
{
    public SpectatingMessage(Guid playerId, int wounds, int ammo)
    {
        PlayerId = playerId;
        Wounds = wounds;
        Ammo = ammo;
    }

    public string Type => "spectating";

    // 관전 대상
    public Guid PlayerId { get; }
    public int Wounds { get; }
    public int Ammo { get; }
}
