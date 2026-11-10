namespace SheepServer.Shooting;

// 판정한 발사 하나의 결과. 빗나가면 TargetId와 Zone이 null이고 Height, Offset은 0.
// Down은 이 발사로 맞은 사람이 쓰러졌는지임. 머리를 맞았거나, 몸을 맞아 반샷이 ShotJudge.WoundsToDown에 닿으면 쓰러짐.
// X, Y는 맞은 자리의 격자 좌표이고 빗나가면 0 (SHEEP-126)
public record ShotOutcome(Guid ShooterId, double At, Guid? TargetId, HitZone? Zone, double Height, double Offset, bool Down, double X, double Y);
