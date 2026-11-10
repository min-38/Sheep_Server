namespace SheepServer.Shooting;

// 발사 이벤트 (기술 설계서 메시지 shot). 받아들인 발사마다 틱을 기다리지 않고 방 안 모두에게 보냄
public class ShotMessage
{
    public ShotMessage(Guid playerId, double angle, double pitch)
    {
        PlayerId = playerId;
        Angle = angle;
        Pitch = pitch;
    }

    public string Type => "shot";
    public Guid PlayerId { get; }

    // fire의 angle 그대로. 돌고 바로 쏘면 state의 각도가 늦게 오므로 받는 쪽은 불꽃 동안 이 각도로 그림
    public double Angle { get; }

    // fire의 pitch 그대로. 받는 쪽이 총구에서 날아가는 총알을 그리는 데만 씀 (SHEEP-125)
    public double Pitch { get; }
}
