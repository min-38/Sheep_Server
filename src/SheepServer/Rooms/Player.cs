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

    // 쓰러진 서버 시각(ms). 서 있으면 null. 아무도 안 남았을 때 가장 늦게(함께) 쓰러진 사람들을 찾는 데 씀
    public double? DownAt { get; set; }

    // 마지막으로 받아들인 발사의 쏜 시각(ms, 서버 시계). 아직 안 쐈으면 null
    public double? LastShotAt { get; set; }
}