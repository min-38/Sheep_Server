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
    public bool Ready { get; set; }
}