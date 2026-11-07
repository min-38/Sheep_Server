namespace SheepServer.Shooting;

// 반샷 알림 (기술 설계서 메시지 wounded). 몸을 맞았지만 쓰러지지 않은 틱에 맞은 사람에게만 보냄.
// 누가 쐈는지는 담지 않음. 누가 반샷인지는 맞은 사람과 쏜 사람만 앎 (컨셉 기획서 규칙)
public class WoundedMessage
{
    public string Type => "wounded";
}
