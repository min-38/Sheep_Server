namespace SheepServer.Rooms;

public class StateMessage
{
    public StateMessage(int tick, List<PlayerState> players)
    {
        Tick = tick;
        Players = players;
    }

    public string Type => "state";
    public int Tick { get; }
    public List<PlayerState> Players { get; }
}
