using System;

namespace SheepServer.Rooms;

public class Room
{
    private readonly string _roomId;
    private int _maxPlayers;
    private int _currentPlayers;
    private List<Guid> _players;

    public Room(string roomId, int maxPlayers)
    {
        _roomId = roomId;
        MaxPlayers = maxPlayers;
        _currentPlayers = 0;
        _players = new List<Guid>();
    }

    public string RoomId => _roomId;

    public int MaxPlayers
    {
        get => _maxPlayers;
        set
        {
            if (value < 1)
            {
                throw new ArgumentException("MaxPlayers must be greater than 0.");
            }
            _maxPlayers = value;
        }
    }

    public int CurrentPlayers
    {
        get => _currentPlayers;
        set
        {
            if (value < 0 || value > _maxPlayers)
            {
                throw new ArgumentException("CurrentPlayers must be between 0 and MaxPlayers.");
            }
            _currentPlayers = value;
        }
    }

    public void AddPlayer(Guid playerId)
    {
        if (_currentPlayers >= _maxPlayers)
        {
            throw new InvalidOperationException("Room is full.");
        }
        _players.Add(playerId);
        _currentPlayers++;
    }

    public void RemovePlayer(Guid playerId)
    {
        if (_players.Remove(playerId))
        {
            _currentPlayers--;
        }
        else
        {
            throw new InvalidOperationException("Player not found in the room.");
        }
    }
}