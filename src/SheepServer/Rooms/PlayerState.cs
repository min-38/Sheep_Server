namespace SheepServer.Rooms;

public record PlayerState(Guid Id, double X, double Y, double Angle, Pose Pose, bool Ready, string Nickname, int Color, int Wins, double Pitch, int Character, int Seat);