namespace SheepServer.Shooting;

// 광선이 처음 닿은 사람과 자리. Height는 맞은 높이(칸, 바닥 0).
// Offset은 맞은 자리가 맞은 사람 중심에서 쏜 사람 기준 오른쪽으로 떨어진 거리(칸). 쏜 사람의 사격 결과 카드에 총알 구멍을 찍는 데 씀.
// X, Y는 맞은 자리의 격자 좌표. 모두가 피와 상처를 그 자리에 그림 (SHEEP-126)
public record ScanHit(Guid Id, HitZone Zone, double Height, double Offset, double X, double Y);
