namespace SheepServer.Shooting;

// 탈락 (기술 설계서 메시지 eliminated). 명중을 정한 틱(맞힌 발사의 쏜 시각에서 W가 지난 뒤)에 방 안 모두에게 보냄. 한 결투에 한 사람마다 한 번만(재대결은 새 결투)
public class EliminatedMessage
{
    public EliminatedMessage(Guid playerId)
    {
        PlayerId = playerId;
    }

    public string Type => "eliminated";
    public Guid PlayerId { get; }
}
