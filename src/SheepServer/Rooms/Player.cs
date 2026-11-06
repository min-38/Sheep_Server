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

    // 남은 총알. 처음엔 가득 참. 재대결 장전은 이후 이슈(SHEEP-36)
    public int Ammo { get; set; } = Revolver.Capacity;

    // 마지막으로 받아들인 발사의 쏜 시각(ms, 서버 시계). 아직 안 쐈으면 null
    public double? LastShotAt { get; set; }
}