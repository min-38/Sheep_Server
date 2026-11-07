namespace SheepServer.Shooting;

// 받아들였지만 아직 판정하지 않은 발사. Pitch는 위아래 각도(라디안, 위가 +).
// At은 도착 시각에 맞춰 자른 쏜 시각(ms, 서버 시계) (기술 설계서 명중 판정 쏜 시각 검사)
public record Shot(Guid ShooterId, double Angle, double Pitch, double At);
