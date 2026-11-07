using SheepServer.Shooting;

namespace SheepServer.Rooms;

public class Player
{
    public Player(Guid id, double x, double y, double angle)
    {
        Id = id;
        X = x;
        Y = y;
        Angle = angle;
        Ready = false;
    }

    public Guid Id { get; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Angle { get; set; }
    public Pose Pose { get; set; } = Pose.Stand;
    public bool Ready { get; set; }

    // 남은 총알. 처음엔 가득 참. 재대결 때는 재대결하는 사람 수만큼 다시 장전함
    public int Ammo { get; set; } = Revolver.Capacity;

    // 맞은 시각(ms, 서버 시계). 맞힌 발사의 쏜 시각임. 서 있으면 null.
    // 이 시각에서 D 안에 쏜 발사는 받고, 아무도 안 남았을 때 함께 쓰러진 사람들을 찾는 데 씀 (기술 설계서 명중 판정 함께 쓰러짐)
    public double? DownAt { get; set; }

    // 마지막으로 받아들인 발사의 쏜 시각(ms, 서버 시계). 아직 안 쐈으면 null
    public double? LastShotAt { get; set; }

    // 맞은 반샷 수. 몸을 맞으면 늘고 ShotJudge.WoundsToDown에 닿으면 쓰러짐. 판이 끝날 때까지 남음 (컨셉 기획서 규칙 머리는 즉사, 몸은 반샷)
    public int Wounds { get; set; }

    // 이번 라운드 걷기가 시작할 때의 반샷 수. 함께 쓰러져 다음 라운드로 가는 사람은 이 값으로 되돌림 (컨셉 기획서 규칙 함께 쓰러짐)
    public int RoundStartWounds { get; set; }

    // 이번 라운드에 걷다 멈춘 거리(칸). 멈추지 않았으면 null이고 최대 걸음까지 걸음 (컨셉 기획서 규칙 걷기와 멈추기).
    // 시작 배치에 다시 설 때 지움
    public double? StopDistance { get; set; }
}