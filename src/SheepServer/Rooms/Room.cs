using System;
using System.Collections.Generic;
using SheepServer.Arena;
using SheepServer.Rounds;
using SheepServer.Shooting;

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

    // 걷기가 시작한 서버 시각(ms). 걷는 중일 때만 값이 있고, 다 걸으면 null
    private double? _walkAt;

    // 재대결이 정해지면 재대결하는 사람과 새로 장전할 총알 수. 재대결 카운트다운이 시작할 때 다시 세우고 비움
    private IReadOnlyList<Guid>? _rematchers;
    private int _rematchRounds;

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
            // 클라가 보낸 값이라 던지면 연결이 끊김. 범위 밖이나 없는 사람은 조용히 버림 (기술 설계서 메시지 turn).
            // 카운트다운부터 "시작!" 전까지도 버림. 걷기 단계는 다 걷고 결투를 기다리는 동안까지 이어짐
            if (!double.IsFinite(angle) || angle < 0 || angle >= 2 * Math.PI || _round.Phase is RoundPhase.Countdown or RoundPhase.Walk)
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
            // 판 도중에는 버림. 승리가 정해질 때 모두 풂 (DecideResult)
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

    // 발사 하나를 받아 방 안 모두에게 바로 보낼 메시지를 순서대로 돌려줌. 버리면 빈 목록 (기술 설계서 메시지 fire).
    // at은 클라가 쏜 시각, now는 서버가 받은 시각(ms). 둘 다 서버 시계
    public List<object> Fire(Guid playerId, double angle, double at, double now)
    {
        lock (_lock)
        {
            var messages = new List<object>();

            // 결투 시각의 틱보다 발사가 먼저 올 수 있어 단계만 보지 않고 시각도 봄. 걷기 단계에서는 NextAt이 결투 시각임.
            // 결과가 정해져 다음 단계(Next)가 생기면 그 시각까지 버림
            bool dueling = (_round.Phase == RoundPhase.Duel && _round.Next == null) || (_round.Phase == RoundPhase.Walk && now >= _round.NextAt);
            if (!dueling || !double.IsFinite(angle) || angle < 0 || angle >= 2 * Math.PI || !double.IsFinite(at))
            {
                return messages;
            }

            var shooter = _players.Find(p => p.Id == playerId);
            if (shooter == null || shooter.Pose == Pose.Down || shooter.Ammo <= 0)
            {
                return messages;
            }

            // 발사 간격은 도착 시각이 아니라 쏜 시각끼리 잼. 도착 간격은 지연에 따라 흔들려 클라가 센 탄 수와 어긋남
            if (shooter.LastShotAt != null && at - shooter.LastShotAt.Value < Revolver.FireInterval)
            {
                return messages;
            }

            // 받아들인 발사만 총알과 발사 간격을 씀
            shooter.Ammo--;
            shooter.LastShotAt = at;
            messages.Add(new ShotMessage(playerId, angle));

            // 쏜 사람과 이미 쓰러진 사람은 맞지 않음.
            // ponytail: 도착 순서대로 바로 판정함 (M4). M5에서 쏜 시각 순서 판정, 확정 대기, 함께 쓰러짐으로 바꿈
            var targets = new List<(Guid Id, double X, double Y)>();
            foreach (var player in _players)
            {
                if (player != shooter && player.Pose != Pose.Down)
                {
                    targets.Add((player.Id, player.X, player.Y));
                }
            }

            var hit = HitScan.FirstHit(shooter.X, shooter.Y, angle, targets, ArenaMap.Fixed);
            if (hit != null)
            {
                var target = _players.Find(p => p.Id == hit.Value)!;
                target.Pose = Pose.Down;
                target.DownAt = now;
                messages.Add(new EliminatedMessage(hit.Value));
            }

            // 맞혔거나 마지막 총알을 썼으면 결과가 날 수 있음. 틱을 기다리지 않고 shot·eliminated 바로 뒤에 보냄
            messages.AddRange(DecideResult(now));
            return messages;
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

            // 판 도중 나간 사람은 탈락으로 봄. 발사가 없어 Fire에서 결과를 정하지 못하므로 결투 중이면 틱마다 봄
            if (_round.Phase == RoundPhase.Duel && _round.Next == null)
            {
                messages.AddRange(DecideResult(now));
            }

            // 판이 바뀌어 새로 정한 다음 단계가 있으면 한 번만 알림. 같은 알림을 틱마다 보내지 않음
            // 시드는 걷기가 시작하는 틱에만 쓰여 틱마다 새로 뽑아도 됨. 그래서 판마다 기다리는 시간이 달라짐
            var next = RoundFlow.Advance(_round, now, _players.Count, readyCount, Random.Shared.Next());
            if (next != _round && next.Next != null)
            {
                messages.Add(new PhaseMessage(next.Next.Value, next.NextAt));
            }

            // 승리 결과를 다 보여 주면 준비 중으로 돌아가 모두 처음 자리에서 다시 시작함 (기술 설계서 결투장 다시 서기)
            if (_round.Next == RoundPhase.Lobby && next.Phase == RoundPhase.Lobby)
            {
                PlaceAtStart(_players, Revolver.Capacity);
            }

            // 재대결 카운트다운이 시작하면 재대결하는 사람만 그 인원수로 다시 세우고 장전함. 나머지는 쓰러진 자리에 그대로 둠
            if (_rematchers != null && _round.Phase != RoundPhase.Countdown && next.Phase == RoundPhase.Countdown)
            {
                var rematching = new List<Player>();
                foreach (var player in _players)
                {
                    if (_rematchers.Contains(player.Id))
                    {
                        rematching.Add(player);
                    }
                }
                PlaceAtStart(rematching, _rematchRounds);
                _rematchers = null;
            }

            if (_round.Phase != RoundPhase.Walk && next.Phase == RoundPhase.Walk)
            {
                _walkAt = _round.NextAt;

                // 지금 각도에 π를 더하면 안 됨. 준비 중에 돌았을 수 있어 위치로 바깥(중심에서 멀어지는 쪽)을 구함
                foreach (var player in _players)
                {
                    // 쓰러진 사람은 재대결에 끼지 못해 돌지도 걷지도 않음
                    if (player.Pose == Pose.Down)
                    {
                        continue;
                    }

                    // Atan2는 y를 먼저 받고 -π~π를 돌려줌. turn과 같은 0 이상 2π 미만으로 맞춤
                    double outward = Math.Atan2(player.Y - CenterY, player.X - CenterX);
                    if (outward < 0)
                    {
                        outward += 2 * Math.PI;
                    }
                    player.Angle = outward;
                }
            }

            _round = next;

            if (_walkAt != null)
            {
                // 틱마다 더하지 않고 걷기 시각부터 지난 시간으로 정함. 늦게 온 틱이 있어도 걷는 거리가 줄지 않음
                double ratio = Math.Clamp((now - _walkAt.Value) / RoundFlow.WalkDuration, 0, 1);
                double walked = RoundFlow.WalkDistance * ratio;

                // 출발할 때 모두 중심에서 R만큼 떨어져 바깥을 봄. 그래서 지금 자리는 바라보는 쪽으로 R + 걸은 거리
                foreach (var player in _players)
                {
                    if (player.Pose == Pose.Down)
                    {
                        continue;
                    }

                    double distance = StartPlacement.Radius + walked;
                    player.X = CenterX + Math.Cos(player.Angle) * distance;
                    player.Y = CenterY + Math.Sin(player.Angle) * distance;
                    player.Pose = ratio < 1 ? Pose.Walk : Pose.Stand;
                }

                // 다 걸으면 그만 계산함. 계속하면 결투 중에 돌 때마다 자리가 원을 따라 미끄러짐
                if (ratio >= 1)
                {
                    _walkAt = null;
                }
            }

            List<PlayerState> playerStates = new List<PlayerState>();
            foreach (var player in _players)
            {
                playerStates.Add(new PlayerState(player.Id, player.X, player.Y, player.Angle, player.Pose, player.Ready));
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

    // players를 그 인원수로 시작 배치에 세우고 rounds발을 장전함. 판이 끝나 준비 중으로 돌아갈 때와 재대결 카운트다운에 씀
    private static void PlaceAtStart(List<Player> players, int rounds)
    {
        for (int i = 0; i < players.Count; i++)
        {
            var spot = StartPlacement.At(players.Count, i, CenterX, CenterY);
            var player = players[i];
            player.X = spot.X;
            player.Y = spot.Y;
            player.Angle = spot.Angle;
            player.Pose = Pose.Stand;
            player.DownAt = null;
            player.Ammo = rounds;
            player.LastShotAt = null;
        }
    }

    // 결과가 정해졌으면 판의 다음 단계를 정해 두고 결과와 다음 단계 알림을 돌려줌. 결과가 없으면 빈 목록. lock 안에서 부름
    private List<object> DecideResult(double now)
    {
        // 쓰러지지 않은 사람과 남은 총알, 그리고 가장 늦게 쓰러진 시각을 찾음
        var alive = new List<(Guid Id, int Ammo)>();
        double? lastDownAt = null;
        foreach (var player in _players)
        {
            if (player.Pose != Pose.Down)
            {
                alive.Add((player.Id, player.Ammo));
            }
            else if (lastDownAt == null || player.DownAt > lastDownAt)
            {
                lastDownAt = player.DownAt;
            }
        }

        // 그 시각에 쓰러진 사람들. 같은 시각에 쓰러졌으면 함께 쓰러진 것임 (M5)
        var lastDown = new List<Guid>();
        foreach (var player in _players)
        {
            if (player.Pose == Pose.Down && player.DownAt == lastDownAt)
            {
                lastDown.Add(player.Id);
            }
        }

        var messages = new List<object>();
        var result = RoundFlow.Result(alive, lastDown);
        if (result == null)
        {
            return messages;
        }

        // 승리면 준비 중으로, 재대결이면 재대결 카운트다운으로 넘어감
        var next = RoundPhase.Lobby;
        if (result.Outcome == Outcome.Win)
        {
            // 판이 끝남. 결과를 보는 동안에도 준비 수가 0으로 보이게 지금 풂 (기술 설계서 메시지 ready)
            foreach (var player in _players)
            {
                player.Ready = false;
            }
        }
        else
        {
            next = RoundPhase.Countdown;
            _rematchers = result.PlayerIds;
            _rematchRounds = result.Rounds;
        }
        _round = new Round(RoundPhase.Duel, next, now + RoundFlow.ResultDuration);

        // Step은 판이 바뀐 틱에만 다음 단계를 알리는데, 여기서는 판을 직접 바꿔 Step이 바뀐 것을 모름. 그래서 여기서 알림
        messages.Add(result);
        messages.Add(new PhaseMessage(next, _round.NextAt));
        return messages;
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
