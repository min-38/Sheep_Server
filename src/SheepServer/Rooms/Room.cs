using System;
using System.Collections.Generic;
using SheepServer.Arena;

namespace SheepServer.Rooms;

public class Room
{
    private static readonly double CenterX = ArenaMap.Fixed.Width / 2.0;
    private static readonly double CenterY = ArenaMap.Fixed.Height / 2.0;

    private readonly string _roomId;
    private readonly object _lock = new();

    private int _maxPlayers;
    private int _currentPlayers;
    private List<Player> _players;

    private int _tickNumber;

    public Room(string roomId, int maxPlayers)
    {
        _roomId = roomId;
        MaxPlayers = maxPlayers;
        _currentPlayers = 0;
        _players = new List<Player>();
    }

    public string RoomId => _roomId;

    public int TickNumber => _tickNumber;

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
        lock (_lock)
        {
            if (_currentPlayers >= _maxPlayers)
            {
                throw new InvalidOperationException("Room is full.");
            }

            _players.Add(new Player(playerId, 0, 0, 0));

            _currentPlayers++;
            ArrangePlayers();

            var spot = StartPlacement.At(_players.Count, _players.Count - 1, CenterX, CenterY);
            _players[_players.Count - 1].Angle = spot.Angle;
        }
    }

    public void RemovePlayer(Guid playerId)
    {
        lock (_lock)
        {
            var player = _players.Find(p => p.Id == playerId);
            if (player != null)
            {
                _currentPlayers--;
                _players.Remove(player);
                ArrangePlayers();
            }
            else
            {
                throw new InvalidOperationException("Player not found in the room.");
            }
        }
    }

    public void SetAngle(Guid playerId, double angle)
    {
        lock (_lock)
        {
            // 클라가 보낸 값이라 던지면 연결이 끊김. 범위 밖이나 없는 사람은 조용히 버림 (기술 설계서 메시지 turn)
            if (!double.IsFinite(angle) || angle < 0 || angle >= 2 * Math.PI)
            {
                return;
            }

            var player = _players.Find(p => p.Id == playerId);
            if (player != null)
            {
                player.Angle = angle;
            }
        }
    }

    public StateMessage Step()
    {
        lock (_lock)
        {
            // TickLoop이 50ms마다 부름. 시계를 읽지 않아 테스트에서 바로 부를 수 있음
            _tickNumber++;
            List<PlayerState> playerStates = new List<PlayerState>();
            foreach (var player in _players)
            {
                playerStates.Add(new PlayerState(player.Id, player.X, player.Y, player.Angle));
            }
            return new StateMessage(_tickNumber, playerStates);
        }
    }

    private void ArrangePlayers()
    {
        for (int i = 0; i < _players.Count; i++)
        {
            var spot = StartPlacement.At(_players.Count, i, CenterX, CenterY);
            _players[i].X = spot.X;
            _players[i].Y = spot.Y;
        }
    }
}
