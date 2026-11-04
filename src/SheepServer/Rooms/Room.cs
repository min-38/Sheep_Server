using System;
using System.Collections.Generic;
using SheepServer.Arena;
using SheepServer.Rounds;

namespace SheepServer.Rooms;

public class Room
{
    private static readonly double CenterX = ArenaMap.Fixed.Width / 2.0;
    private static readonly double CenterY = ArenaMap.Fixed.Height / 2.0;

    private readonly string _roomId;
    private readonly object _lock = new();

    // 플레이어마다 메시지(state, phase)를 보내는 방법. 연결은 Program.cs가 갖고 있고 TickLoop은 이것만 부름
    private readonly Dictionary<Guid, Func<object, Task>> _senders = new();

    private int _maxPlayers;
    private int _currentPlayers;
    private List<Player> _players;

    private int _tickNumber;

    // 판 흐름. 처음엔 준비 중이고, 바꾸는 규칙은 RoundFlow에 있음
    private Round _round = new(RoundPhase.Lobby, null, 0);

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

    // 카운트다운이 정해진 뒤부터 판 도중임. 이때는 들어올 수 없고 준비도 바꿀 수 없음 (기술 설계서 메시지 joinRoom, ready)
    public bool InRound
    {
        get
        {
            lock (_lock)
            {
                return _round.Phase != RoundPhase.Lobby || _round.Next != null;
            }
        }
    }

    // send는 테스트처럼 보낼 곳이 없으면 null
    public void AddPlayer(Guid playerId, Func<object, Task>? send = null)
    {
        lock (_lock)
        {
            if (_currentPlayers >= _maxPlayers)
            {
                throw new InvalidOperationException("Room is full.");
            }

            if (InRound)
            {
                throw new InvalidOperationException("Round in progress.");
            }

            _players.Add(new Player(playerId, 0, 0, 0));

            _currentPlayers++;
            ArrangePlayers();

            var spot = StartPlacement.At(_players.Count, _players.Count - 1, CenterX, CenterY);
            _players[_players.Count - 1].Angle = spot.Angle;

            if (send != null)
            {
                _senders[playerId] = send;
            }
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
                _senders.Remove(playerId);
                // 판 도중에 다시 세우면 남은 사람의 자리가 바뀜. 나간 사람만 빠짐
                if (!InRound)
                {
                    ArrangePlayers();
                }
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

    public void SetReady(Guid playerId, bool ready)
    {
        lock (_lock)
        {
            // 판 도중에는 버림. 판이 끝나면 모두 풂 (이후 이슈)
            if (InRound)
            {
                return;
            }

            var player = _players.Find(p => p.Id == playerId);
            if (player != null)
            {
                player.Ready = ready;
            }
        }
    }

    // 틱 하나를 돌리고 이번 틱에 방 안 모두에게 보낼 메시지를 순서대로 돌려줌
    public List<object> Step(double now)
    {
        lock (_lock)
        {
            // TickLoop이 50ms마다 서버 시각을 넘겨 부름. 시계를 직접 읽지 않아 테스트에서 바로 부를 수 있음
            _tickNumber++;
            var messages = new List<object>();

            int readyCount = 0;
            foreach (var player in _players)
            {
                if (player.Ready)
                {
                    readyCount++;
                }
            }

            // 판이 바뀌어 새로 정한 다음 단계가 있으면 한 번만 알림. 같은 알림을 틱마다 보내지 않음
            var next = RoundFlow.Advance(_round, now, _players.Count, readyCount);
            if (next != _round && next.Next != null)
            {
                messages.Add(new PhaseMessage(next.Next.Value, next.NextAt));
            }
            _round = next;

            List<PlayerState> playerStates = new List<PlayerState>();
            foreach (var player in _players)
            {
                playerStates.Add(new PlayerState(player.Id, player.X, player.Y, player.Angle, player.Ready));
            }
            messages.Add(new StateMessage(_tickNumber, playerStates));
            return messages;
        }
    }

    // 복사해서 돌려줌. 보내기는 await라 lock 밖에서 해야 하고, 그동안 입장과 퇴장이 목록을 바꿔도 됨
    public List<Func<object, Task>> GetSenders()
    {
        lock (_lock)
        {
            return new List<Func<object, Task>>(_senders.Values);
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
