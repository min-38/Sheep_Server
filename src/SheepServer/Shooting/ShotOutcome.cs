namespace SheepServer.Shooting;

// 판정한 발사 하나의 결과. 빗나가면 TargetId와 Zone이 null이고 Height, Offset은 0.
// Down은 이 발사로 맞은 사람이 쓰러졌는지임. ponytail: 반샷(SHEEP-61) 전이라 맞으면 늘 쓰러짐
public record ShotOutcome(Guid ShooterId, double At, Guid? TargetId, HitZone? Zone, double Height, double Offset, bool Down);
