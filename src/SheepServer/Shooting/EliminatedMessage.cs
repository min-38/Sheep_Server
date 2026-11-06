namespace SheepServer.Shooting;

// 탈락 (기술 설계서 메시지 eliminated). 명중이 정해지면 틱을 기다리지 않고 방 안 모두에게 보냄. 한 판에 한 사람마다 한 번만
public class EliminatedMessage
{
    public EliminatedMessage(Guid playerId)
    {
        PlayerId = playerId;
    }

    public string Type => "eliminated";
    public Guid PlayerId { get; }
}
