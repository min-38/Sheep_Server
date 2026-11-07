namespace SheepServer.Shooting;

// 리볼버 숫자 (시스템 기획서 리볼버). 클라 main.ts·revolver.ts와 같은 값이어야 클라가 센 탄 수와 서버가 받은 발사 수가 어긋나지 않음
public static class Revolver
{
    // 컨셉 기획서 규칙: 라운드마다 한 발. 다음 라운드에도 인원과 상관없이 이만큼 장전함
    public const int Capacity = 1;

    // ms. 같은 사람이 이보다 빨리 쏘면 버림. ponytail: 임시 값. 플레이해 보며 정함
    public const double FireInterval = 250;
}
