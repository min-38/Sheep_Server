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

    // 이번 결투의 결투 시각("시작!", ms). 걷기가 시작할 때 정함. 이보다 먼저 쐈다는 발사는 버림
    private double _duelAt;

    // 받았지만 아직 판정하지 않은 발사. 쏜 시각에서 W가 지난 틱에 판정함 (기술 설계서 명중 판정 확정 대기)
    private readonly List<Shot> _pending = new();

    // 재대결이 정해지면 재대결하는 사람과 새로 장전할 총알 수. 재대결 카운트다운이 시작할 때 다시 세우고 비움
    private IReadOnlyList<Guid>? _rematchers;
    private int _rematchRounds;

    public Room(string roomId, int maxPlayers, bool isPrivate = false)
    {
        _roomId = roomId;
        MaxPlayers = maxPlayers;
        IsPrivate = isPrivate;
        _currentPlayers = 0;
        _players = new List<Player>();
    }

    public string RoomId => _roomId;

    // 비공개 방은 랜덤 입장과 방 목록에 나오지 않고 방 코드로만 들어옴 (시스템 기획서 방 화면 방 만들기)
    public bool IsPrivate { get; }

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

    // send는 테스트처럼 보낼 곳이 없으면 null. nickname은 검사를 마친 이름이고, 없으면 임시 이름을 씀
    public void AddPlayer(Guid playerId, Func<object, Task>? send = null, string? nickname = null)
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

            _players.Add(new Player(playerId, 0, 0, 0, nickname ?? Nickname.Placeholder, FreeColor()));

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

    // 걷다 멈추기. 그 사람은 at까지 걸은 자리에 서고 이번 라운드에는 더 걷지 않음 (기술 설계서 메시지 stop).
    // at은 클라가 누른 시각, now는 서버가 받은 시각(ms). 둘 다 서버 시계. 답은 없음
    public void Stop(Guid playerId, double at, double now)
    {
        lock (_lock)
        {
            // 걷기 시각과 그 뒤 첫 틱 사이에 올 수 있어 _walkAt만 보지 않고 다음 단계와 시각도 봄 (Fire의 dueling과 같은 이유)
            double? walkAt = _walkAt ?? (_round.Next == RoundPhase.Walk && now >= _round.NextAt ? _round.NextAt : null);
            if (walkAt == null || now >= walkAt.Value + RoundFlow.WalkDuration || !double.IsFinite(at))
            {
                return;
            }

            // 쓰러진 사람은 걷지 않음. 첫 stop만 씀
            var player = _players.Find(p => p.Id == playerId);
            if (player == null || player.Pose == Pose.Down || player.StopDistance != null)
            {
                return;
            }

            // 누른 시각은 쏜 시각처럼 W까지만 앞당겨 주고, 걷기 전에 누른 것은 걷기 시각에 누른 것으로 봄
            at = Math.Max(ShotJudge.ClampAt(at, now), walkAt.Value);
            player.StopDistance = RoundFlow.WalkDistance * Math.Clamp((at - walkAt.Value) / RoundFlow.WalkDuration, 0, 1);
        }
    }

    // 발사 하나를 받아 방 안 모두에게 바로 보낼 메시지를 돌려줌. 받으면 shot 하나, 버리면 빈 목록 (기술 설계서 메시지 fire).
    // 명중은 여기서 정하지 않고 쏜 시각에서 W가 지난 틱(Step)에 정함. pitch는 위아래 각도(라디안, 위가 +).
    // at은 클라가 쏜 시각, now는 서버가 받은 시각(ms). 둘 다 서버 시계
    public List<object> Fire(Guid playerId, double angle, double pitch, double at, double now)
    {
        lock (_lock)
        {
            var messages = new List<object>();

            // 결투 시각의 틱보다 발사가 먼저 올 수 있어 단계만 보지 않고 시각도 봄. 걷기 단계에서는 NextAt이 결투 시각임.
            // 결과가 정해져 다음 단계(Next)가 생기면 그 시각까지 버림
            bool dueling = (_round.Phase == RoundPhase.Duel && _round.Next == null) || (_round.Phase == RoundPhase.Walk && now >= _round.NextAt);
            if (!dueling || !double.IsFinite(angle) || angle < 0 || angle >= 2 * Math.PI || !double.IsFinite(at) || at < _duelAt)
            {
                return messages;
            }

            // ±π/2면 tan이 무한대라 광선 높이를 잴 수 없고, 넘으면 뒤로 넘어감. 클라는 시선 한계(MAX_PITCH)에 흔들림을 더한 만큼만 보냄
            if (!double.IsFinite(pitch) || Math.Abs(pitch) >= Math.PI / 2)
            {
                return messages;
            }

            var shooter = _players.Find(p => p.Id == playerId);
            if (shooter == null || shooter.Ammo <= 0)
            {
                return messages;
            }

            // 아래 검사와 판정은 자른 쏜 시각으로 함 (기술 설계서 명중 판정 쏜 시각 검사)
            at = ShotJudge.ClampAt(at, now);

            // 제한 시각 뒤에 쏜 발사는 버림. 그 사람은 W 뒤에 자살로 쓰러짐 (컨셉 기획서 규칙 제한 시간)
            if (at > _duelAt + RoundFlow.DuelLimit)
            {
                return messages;
            }

            // 맞았어도 맞은 시각에서 D 안에 쏜 발사는 받음. 서로 맞히면 함께 쓰러짐
            if (shooter.DownAt != null && at > shooter.DownAt.Value + ShotJudge.MutualWindow)
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
            _pending.Add(new Shot(playerId, angle, pitch, at));
            messages.Add(new ShotMessage(playerId, angle));
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

            // 쏜 시각에서 W가 지난 발사를 쏜 시각 순서로 판정함. 뒤에 올 발사는 쏜 시각이 지금 - W보다 늦어 이것들보다 앞설 수 없음
            messages.AddRange(JudgeShots(now));

            // 결과는 틱마다 봄. 판 도중 나간 사람은 탈락으로 봄. 시간 초과는 결과를 정하기 전에 봐야 그 틱에 결과가 남은 사람을 셈.
            // 결과가 정해진 뒤에는 결투가 끝난 것이라 총알을 든 승자도 시간 초과로 보지 않음
            if (_round.Phase == RoundPhase.Duel && _round.Next == null)
            {
                messages.AddRange(TimeOut(now));
                messages.AddRange(DecideResult(now));
            }

            // 판이 바뀌어 새로 정한 다음 단계가 있으면 한 번만 알림. 같은 알림을 틱마다 보내지 않음
            // 시드는 걷기가 시작하는 틱에만 쓰여 틱마다 새로 뽑아도 됨. 그래서 판마다 기다리는 시간이 달라짐
            var next = RoundFlow.Advance(_round, now, _players.Count, readyCount, Random.Shared.Next());
            if (next != _round && next.Next != null)
            {
                messages.Add(new PhaseMessage(next.Next.Value, next.NextAt));
            }

            // 승리 결과를 다 보여 주면 준비 중으로 돌아가 모두 처음 자리에서 다시 시작함 (기술 설계서 결투장 다시 서기).
            // 판이 끝났으므로 반샷도 모두 지움 (컨셉 기획서 규칙)
            if (_round.Next == RoundPhase.Lobby && next.Phase == RoundPhase.Lobby)
            {
                PlaceAtStart(_players, Revolver.Capacity);
                foreach (var player in _players)
                {
                    player.Wounds = 0;
                }
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

                        // 쓰러졌다가 다시 서는 사람(함께 쓰러짐)은 반샷을 그 라운드 전으로 되돌림. 살아남은 사람의 반샷은 그대로 둠.
                        // PlaceAtStart가 세우고 나면 Stand라 가를 수 없어 그 전에 봄
                        if (player.Pose == Pose.Down)
                        {
                            player.Wounds = player.RoundStartWounds;
                        }
                    }
                }
                PlaceAtStart(rematching, _rematchRounds);
                _rematchers = null;
            }

            if (_round.Phase != RoundPhase.Walk && next.Phase == RoundPhase.Walk)
            {
                _walkAt = _round.NextAt;
                _duelAt = next.NextAt;

                // 지금 각도에 π를 더하면 안 됨. 준비 중에 돌았을 수 있어 위치로 바깥(중심에서 멀어지는 쪽)을 구함
                foreach (var player in _players)
                {
                    // 쓰러진 사람은 재대결에 끼지 못해 돌지도 걷지도 않음
                    if (player.Pose == Pose.Down)
                    {
                        continue;
                    }

                    // 이 라운드에 함께 쓰러지면 이 값으로 되돌림
                    player.RoundStartWounds = player.Wounds;

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

                    // 멈춘 사람은 멈춘 자리에 서서 걷기가 끝날 때까지 기다림. 그래서 "시작!"은 모두 같은 시각임
                    double distance = StartPlacement.Radius + Math.Min(walked, player.StopDistance ?? walked);
                    player.X = CenterX + Math.Cos(player.Angle) * distance;
                    player.Y = CenterY + Math.Sin(player.Angle) * distance;
                    player.Pose = ratio < 1 && player.StopDistance == null ? Pose.Walk : Pose.Stand;
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
                playerStates.Add(new PlayerState(player.Id, player.X, player.Y, player.Angle, player.Pose, player.Ready, player.Nickname, player.Color, player.Wins));
            }
            messages.Add(new StateMessage(_tickNumber, playerStates));
            return messages;
        }
    }

    // 복사해서 돌려줌. 보내기는 await라 lock 밖에서 해야 하고, 그동안 입장과 퇴장이 목록을 바꿔도 됨.
    // id도 같이 줘서 한 사람에게만 갈 메시지(Direct)를 그 사람에게만 보냄
    public List<(Guid Id, Func<object, Task> Send)> GetSenders()
    {
        lock (_lock)
        {
            var senders = new List<(Guid Id, Func<object, Task> Send)>();
            foreach (var (id, send) in _senders)
            {
                senders.Add((id, send));
            }
            return senders;
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

            // 걷기가 시작하는 틱이 아니라 여기서 지움. 걷기 시각과 그 틱 사이에 온 stop이 지워지지 않게 함
            player.StopDistance = null;
        }
    }

    // 판정할 차례가 된 발사를 판정해 맞은 사람에게 반샷을 더하거나 쓰러뜨림. 쏜 사람에게만 갈 사격 결과,
    // 쓰러지지 않은 맞은 사람에게만 갈 반샷 알림, 모두에게 갈 탈락 알림을 돌려줌. lock 안에서 부름
    private List<object> JudgeShots(double now)
    {
        var messages = new List<object>();
        var due = _pending.FindAll(s => s.At + ShotJudge.MaxDelay <= now);
        if (due.Count == 0)
        {
            return messages;
        }
        _pending.RemoveAll(s => s.At + ShotJudge.MaxDelay <= now);

        var players = new List<(Guid Id, double X, double Y, double? DownAt, int Wounds)>();
        foreach (var player in _players)
        {
            players.Add((player.Id, player.X, player.Y, player.DownAt, player.Wounds));
        }

        foreach (var outcome in ShotJudge.Judge(due, players, ArenaMap.Fixed))
        {
            // 쏜 사람이 판정 전에 나갔으면 받을 사람이 없어 아무에게도 안 감 (TickLoop.MessagesFor)
            messages.Add(new Direct(outcome.ShooterId, new ShotResultMessage(outcome)));
            if (outcome.TargetId == null)
            {
                continue;
            }

            var target = _players.Find(p => p.Id == outcome.TargetId)!;
            if (outcome.Zone == HitZone.Body)
            {
                target.Wounds++;
            }

            if (outcome.Down)
            {
                target.Pose = Pose.Down;
                target.DownAt = outcome.At;
                messages.Add(new EliminatedMessage(target.Id, EliminationReason.Shot));
            }
            else
            {
                messages.Add(new Direct(target.Id, new WoundedMessage()));
            }
        }
        return messages;
    }

    // 제한 시각에서 W가 지났으면 서 있고 아직 쏘지 않은 사람을 모두 자살로 쓰러뜨리고, 모두에게 갈 탈락 알림을 돌려줌 (컨셉 기획서 규칙 제한 시간).
    // 제한 시각 전에 쏜 발사는 W 안에 도착해 이 틱의 JudgeShots에서 이미 판정됨. lock 안에서 부름
    private List<object> TimeOut(double now)
    {
        var messages = new List<object>();
        double deadline = _duelAt + RoundFlow.DuelLimit;
        if (now < deadline + ShotJudge.MaxDelay)
        {
            return messages;
        }

        foreach (var player in _players)
        {
            if (player.Pose == Pose.Down || player.Ammo <= 0)
            {
                continue;
            }

            // 모두 제한 시각에 쓰러져 서로 함께 쓰러진 것임. 자살에 한 발을 써서, 제한 시각에 쏜 발사가 이 틱 뒤에 와도 받지 않음.
            // 총알이 없어 다음 틱에 다시 쓰러뜨리지도 않음
            player.Pose = Pose.Down;
            player.DownAt = deadline;
            player.Ammo = 0;
            messages.Add(new EliminatedMessage(player.Id, EliminationReason.Timeout));
        }
        return messages;
    }

    // 결과가 정해졌으면 판의 다음 단계를 정해 두고 결과와 다음 단계 알림을 돌려줌. 결과가 없으면 빈 목록. lock 안에서 부름
    private List<object> DecideResult(double now)
    {
        // 쓰러지지 않은 사람과 남은 총알, 그리고 가장 늦게 맞은 시각을 찾음
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

        var messages = new List<object>();

        // 쓰러진 사람이 맞은 시각에서 D 안에 쏜 발사가 아직 판정되지 않았을 수 있음. W + D가 지나야 정함 (기술 설계서 메시지 result)
        if (lastDownAt != null && now < lastDownAt.Value + ShotJudge.MaxDelay + ShotJudge.MutualWindow)
        {
            return messages;
        }

        // 둘 이상 남았으면 아직 판정하지 않은 발사가 누군가를 쓰러뜨릴 수 있음. 모두 총알이 없어도 마지막 발사를 판정한 뒤에 정함
        if (alive.Count >= 2 && _pending.Count > 0)
        {
            return messages;
        }

        // 가장 늦게 맞은 사람과, 그 시각에서 D 안에 맞은 사람들. 함께 쓰러진 것임
        var lastDown = new List<Guid>();
        foreach (var player in _players)
        {
            if (player.Pose == Pose.Down && player.DownAt >= lastDownAt - ShotJudge.MutualWindow)
            {
                lastDown.Add(player.Id);
            }
        }

        var result = RoundFlow.Result(alive, lastDown);
        if (result == null)
        {
            return messages;
        }

        // 남은 발사는 이긴 사람이 쏜 것이거나 맞고 D가 지난 뒤의 무효 발사뿐이라 맞을 사람이 없음. 다시 선 사람을 맞히지 않게 버림
        _pending.Clear();

        // 승리면 준비 중으로, 재대결이면 재대결 카운트다운으로 넘어감
        var next = RoundPhase.Lobby;
        if (result.Outcome == Outcome.Win)
        {
            // 판이 끝남. 결과를 보는 동안에도 준비 수가 0으로 보이게 지금 풂 (기술 설계서 메시지 ready)
            foreach (var player in _players)
            {
                player.Ready = false;
            }

            // 결과를 정한 이 틱의 state부터 오른 현상금이 보임. 재대결은 판이 끝나지 않아 세지 않음
            _players.Find(p => p.Id == result.PlayerIds[0])!.Wins++;
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

    // 지금 방에 있는 사람이 쓰지 않는 가장 작은 색 번호. 나간 사람의 색은 비워 두고 다음 사람에게 줌 (컨셉 기획서 사람 구별).
    // 방은 8명까지라 8색 안에서 늘 찾음. lock 안에서 부름
    private int FreeColor()
    {
        int color = 0;
        while (_players.Exists(p => p.Color == color))
        {
            color++;
        }
        return color;
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
