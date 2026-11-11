using System;
using System.Collections.Generic;
using SheepServer.Arena;
using SheepServer.Rounds;
using SheepServer.Shooting;

namespace SheepServer.Rooms;

public class Room
{
    // 위아래 시선의 한계(라디안). 클라 turn.ts의 MAX_PITCH와 같아야 함 (시스템 기획서 조작 위아래 보기)
    public const double MaxPitch = 0.5;

    private static readonly double CenterX = ArenaMap.Fixed.Width / 2.0;
    private static readonly double CenterY = ArenaMap.Fixed.Height / 2.0;

    private readonly string _roomId;
    private readonly object _lock = new();

    // 플레이어마다 메시지(state, phase)를 보내는 방법. 연결은 Program.cs가 갖고 있고 TickLoop은 이것만 부름
    private readonly Dictionary<Guid, Func<object, Task>> _senders = new();

    // 쓰러진 관전자 → 그 사람이 보는 대상. 대상에게만 가는 메시지를 관전자에게도 복사함 (기술 설계서 메시지 spectate).
    // 한 사람은 한 대상만 봄
    private readonly Dictionary<Guid, Guid> _watching = new();

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

    // 인원수를 받아 시작 자리를 섞은 순서를 줌 (StartPlacement.Order). 판과 재대결마다 부름 (SHEEP-186)
    private readonly Func<int, int[]> _startOrder;

    // name은 검사를 마친 방 이름. 테스트처럼 이름이 상관없으면 비워 둠.
    // startOrder가 없으면 판마다 무작위로 섞음. 테스트는 누가 어디 서는지 알아야 해 정한 순서를 넣음
    public Room(string roomId, int maxPlayers, bool isPrivate = false, string name = "", Func<int, int[]>? startOrder = null)
    {
        _startOrder = startOrder ?? (count => StartPlacement.Order(count, Random.Shared.Next()));
        _roomId = roomId;
        MaxPlayers = maxPlayers;
        IsPrivate = isPrivate;
        Name = name;
        _currentPlayers = 0;
        _players = new List<Player>();
    }

    public string RoomId => _roomId;

    // 비공개 방은 방 목록에 나오지 않고 방 코드로만 들어옴 (시스템 기획서 방 화면 방 만들기)
    public bool IsPrivate { get; }

    // 대기실 보기와 술집 칠판에 보이는 이름. 방이 있는 동안 바뀌지 않음
    public string Name { get; }

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

    // send는 테스트처럼 보낼 곳이 없으면 null. nickname은 검사를 마친 이름이고, 없으면 임시 이름을 씀.
    // character는 검사를 마친 캐릭터 번호(Character.IsValid). color는 검사를 마친 사람 색 번호(PersonColor.IsValid)이고,
    // 없거나 방 안 다른 사람이 쓰면 비어 있는 가장 작은 색을 줌 (기술 설계서 메시지 joinRoom color). hand는 총을 든 손
    public void AddPlayer(Guid playerId, Func<object, Task>? send = null, string? nickname = null, int character = 0, int? color = null, Hand hand = Hand.Right)
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

            int given = color is int wanted && !_players.Exists(p => p.Color == wanted) ? wanted : FreeColor();
            _players.Add(new Player(playerId, 0, 0, 0, nickname ?? Nickname.Placeholder, given, character, FreeSeat(), hand));

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

    // now는 나간 시각(ms, 서버 시계). 판 도중(카운트다운부터)에 나간 사람은 판이 끝날 때까지 몸을 남기고, 서 있었으면 W 뒤에 자살로 쓰러뜨림 (LeaveOut).
    // 술집을 나서는 장면까지는 결투장에 서기 전이라 그냥 뺌 (사용자 결정 2026-11-11, SHEEP-200)
    public void RemovePlayer(Guid playerId, double now)
    {
        lock (_lock)
        {
            var player = _players.Find(p => p.Id == playerId && p.LeftAt == null);
            if (player != null)
            {
                _currentPlayers--;
                if (_round.Phase == RoundPhase.Lobby)
                {
                    _players.Remove(player);
                }
                else
                {
                    player.LeftAt = now;

                    // 나간 사람이 쏜 발사는 판정 전이면 버림 (기술 설계서 메시지 fire)
                    _pending.RemoveAll(s => s.ShooterId == playerId);
                }
                _senders.Remove(playerId);

                // 나간 사람이 보던 것과 나간 사람을 보던 것을 지움. .NET Core 3.0부터 Dictionary는 도는 중에 Remove해도 됨
                _watching.Remove(playerId);
                foreach (var (spectator, target) in _watching)
                {
                    if (target == playerId)
                    {
                        _watching.Remove(spectator);
                    }
                }

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

    // pitch는 위아래 시선(라디안, 위가 +). 보내지 않은 클라는 null이고 그때는 그대로 둠
    public void SetAngle(Guid playerId, double angle, double? pitch = null)
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

                // 범위 밖이거나 숫자가 아니면 위아래 시선만 버림. NaN은 비교가 늘 false라 여기서 걸러짐
                if (pitch is double p && Math.Abs(p) <= MaxPitch)
                {
                    player.Pitch = p;
                }
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

    // 대기실에서 캐릭터와 사람 색을 바꿈. 클라가 보낸 값이라 던지지 않고, 범위 밖이거나 다른 사람이 쓰는 색이거나 판 도중이거나
    // 준비한 동안이면 통째로 버림 (SHEEP-170). 바꾼 값은 다음 state로 감 (기술 설계서 메시지 dress)
    public void Dress(Guid playerId, int character, int color)
    {
        lock (_lock)
        {
            if (InRound || !Character.IsValid(character) || !PersonColor.IsValid(color) || _players.Exists(p => p.Color == color && p.Id != playerId))
            {
                return;
            }

            var player = _players.Find(p => p.Id == playerId);
            if (player != null && !player.Ready)
            {
                player.Character = character;
                player.Color = color;
            }
        }
    }

    // D 키로 총을 뽑기 시작함 (기술 설계서 메시지 draw, SHEEP-123). at은 클라가 누른 시각, now는 서버가 받은 시각(ms). 둘 다 서버 시계. 답은 없고
    // 다음 state의 drawnAt으로 모두가 앎. "시작!" 전에 누른 것은 버림(사용자 결정 2026-11-10). 쓰러진 사람과 이미 뽑은 사람도 버림
    public void Draw(Guid playerId, double at, double now)
    {
        lock (_lock)
        {
            // Fire와 같은 까닭으로 단계와 시각을 함께 봄. 결과가 정해져 다음 단계가 생기면 버림
            bool dueling = (_round.Phase == RoundPhase.Duel && _round.Next == null) || (_round.Phase == RoundPhase.Walk && now >= _round.NextAt);
            if (!dueling || !double.IsFinite(at) || at < _duelAt)
            {
                return;
            }

            var player = _players.Find(p => p.Id == playerId);
            if (player == null || player.Pose == Pose.Down || player.DrawnAt != null)
            {
                return;
            }

            // 누른 시각은 쏜 시각처럼 W까지만 앞당겨 줌
            player.DrawnAt = Math.Max(ShotJudge.ClampAt(at, now), _duelAt);
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

            // 맞은 뒤에 쏜 발사는 버림. 맞은 시각과 ms까지 같으면 받아 서로 맞히면 함께 쓰러짐 (SHEEP-160)
            if (shooter.DownAt != null && at > shooter.DownAt.Value)
            {
                return messages;
            }

            // 총을 다 뽑기 전에 쏜 발사는 버림. 뽑지 않았으면 쏠 수 없음 (사용자 결정 2026-11-09, SHEEP-123)
            if (shooter.DrawnAt == null || at < shooter.DrawnAt.Value + Revolver.DrawDuration)
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
            messages.Add(new ShotMessage(playerId, angle, pitch));
            return messages;
        }
    }

    // 쓰러진 사람이 볼 사람을 고름. 받으면 대상의 지금 반샷 수와 남은 총알을 담은 답을, 버리거나 그만두면(targetId가 null) null을 돌려줌
    // (기술 설계서 메시지 spectate). 서 있는 사람은 받지 않아 누가 반샷인지 새지 않음. 쓰러진 사람은 판 도중에만 있어 판 도중인지는 따로 보지 않음.
    // 대상도 서 있어야 함. 관전은 살아 있는 사람의 시점이고(컨셉 기획서 규칙 탈락한 뒤), 모두 함께 쓰러져 다시 설 사람끼리 반샷을 엿보지 못하게 함
    public SpectatingMessage? Spectate(Guid spectatorId, Guid? targetId)
    {
        lock (_lock)
        {
            if (targetId == null)
            {
                _watching.Remove(spectatorId);
                return null;
            }

            var spectator = _players.Find(p => p.Id == spectatorId);
            var target = _players.Find(p => p.Id == targetId);
            // 판 도중 나간 사람은 W 동안 서 있어도 방에 없는 사람이라 볼 수 없음 (SHEEP-200)
            if (spectator == null || spectator.Pose != Pose.Down || target == null || target.Pose == Pose.Down || target.LeftAt != null)
            {
                return null;
            }

            _watching[spectatorId] = target.Id;
            return new SpectatingMessage(target.Id, target.Wounds, target.Ammo);
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

            // 나간 뒤 W가 지난 사람을 쓰러뜨림. 시간 초과보다 먼저 봐야 제한 시각 전에 나간 사람이 시간 초과가 아니라 나가기로 쓰러짐
            messages.AddRange(LeaveOut(now));

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
                // 판 도중 나간 사람의 몸은 판이 끝나야 뺌 (SHEEP-200)
                _players.RemoveAll(p => p.LeftAt != null);
                PlaceAtStart(_players, Revolver.Capacity);
                foreach (var player in _players)
                {
                    player.Wounds = 0;
                    player.Kills = 0;
                }
            }

            // 새 판 카운트다운이 정해질 때와 시작할 때 모두 제자리에서 중심과 정면을 봄. 준비 중에 늦게 온 turn과, 들고 날 때 자리만 옮기는 ArrangePlayers가
            // 시작 각도를 바꿔 둠. 자리는 옮기지 않음. 판이 정해진 뒤 나간 사람이 있어도 남은 사람은 그 자리에 섬.
            // 정해질 때 맞추는 것은 술집을 나서는 장면 끝에 결투장이 드러날 때 이미 가운데를 보게 하려는 것이고 (SHEEP-167),
            // 시작할 때 다시 맞추는 것은 SetAngle이 그 장면 동안의 turn은 아직 받아 늦게 온 turn이 각도를 바꿀 수 있기 때문임
            bool scheduled = _round.Next == null && next.Phase == RoundPhase.Lobby && next.Next == RoundPhase.Countdown;
            bool starts = _rematchers == null && _round.Phase != RoundPhase.Countdown && next.Phase == RoundPhase.Countdown;

            // 판마다 시작 자리를 섞어 세움. 정해질 때 섞어야 술집을 나서는 장면 끝에 결투장이 드러날 때 이미 그 자리에 섬.
            // 준비 중에 섞으면 누가 들고 날 때 ArrangePlayers가 들어온 순서로 되돌림 (SHEEP-186)
            if (scheduled)
            {
                PlaceAtStart(Shuffled(_players), Revolver.Capacity);
            }
            if (scheduled || starts)
            {
                foreach (var player in _players)
                {
                    // Atan2는 -π~π를 돌려줌. 2π를 더한 나머지로 turn과 같은 0 이상 2π 미만으로 맞춤. 0보다 아주 조금 작은 값도 2π가 되지 않음
                    player.Angle = (Math.Atan2(CenterY - player.Y, CenterX - player.X) + 2 * Math.PI) % (2 * Math.PI);
                    player.Pitch = 0;
                }
            }

            // 재대결 카운트다운이 시작하면 재대결하는 사람만 그 인원수로 다시 세우고 장전함. 나머지는 쓰러진 자리에 그대로 둠
            if (_rematchers != null && _round.Phase != RoundPhase.Countdown && next.Phase == RoundPhase.Countdown)
            {
                var rematching = new List<Player>();
                foreach (var player in _players)
                {
                    // 결과가 정해진 뒤 나간 사람은 다음 라운드로 가지 않고 그 자리에 남음 (SHEEP-200)
                    if (_rematchers.Contains(player.Id) && player.LeftAt == null)
                    {
                        rematching.Add(player);

                        // 쓰러졌다가 다시 서는 사람(함께 쓰러짐)은 반샷을 그 라운드 전으로 되돌림. 살아남은 사람의 반샷은 그대로 둠.
                        // PlaceAtStart가 세우고 나면 Stand라 가를 수 없어 그 전에 봄
                        if (player.Pose == Pose.Down)
                        {
                            player.Wounds = player.RoundStartWounds;

                            // 다시 서므로 쓰러뜨린 사람의 킬도 되돌림 (SHEEP-169)
                            var killer = _players.Find(p => p.Id == player.DownedBy);
                            if (killer != null)
                            {
                                killer.Kills--;
                            }
                        }
                    }
                }
                PlaceAtStart(Shuffled(rematching), _rematchRounds);
                _rematchers = null;
            }

            if (_round.Phase != RoundPhase.Walk && next.Phase == RoundPhase.Walk)
            {
                _walkAt = _round.NextAt;
                _duelAt = next.NextAt;

                // 이 라운드에 함께 쓰러지면 이 값으로 되돌림. 쓰러진 사람은 재대결에 끼지 못해 돌지도 걷지도 않음
                foreach (var player in _players)
                {
                    if (player.Pose != Pose.Down)
                    {
                        player.RoundStartWounds = player.Wounds;
                    }
                }
            }

            _round = next;

            if (_walkAt != null)
            {
                // 틱마다 더하지 않고 걷기 시각부터 지난 시간으로 정함. 늦게 온 틱이 있어도 덜 돌거나 덜 걷지 않음.
                // 먼저 TurnDuration 동안 제자리에서 뒤돌고(SHEEP-120), TurnPause 동안 서서 뜸을 들인 뒤(SHEEP-141), WalkDuration 동안 걸음
                double elapsed = now - _walkAt.Value;
                double turned = Math.Clamp(elapsed / RoundFlow.TurnDuration, 0, 1);
                double ratio = Math.Clamp((elapsed - RoundFlow.TurnDuration - RoundFlow.TurnPause) / RoundFlow.WalkDuration, 0, 1);
                double distance = StartPlacement.Radius + RoundFlow.WalkDistance * ratio;

                foreach (var player in _players)
                {
                    if (player.Pose == Pose.Down)
                    {
                        continue;
                    }

                    // 자리는 늘 중심에서 바깥으로 뻗은 선 위라 위치로 바깥을 구함. 준비 중에 돌아 둔 각도와 상관없음
                    double outward = Math.Atan2(player.Y - CenterY, player.X - CenterX);
                    player.X = CenterX + Math.Cos(outward) * distance;
                    player.Y = CenterY + Math.Sin(outward) * distance;

                    // 중심을 보던 데서 늘 각도가 느는 쪽으로 반 바퀴 돎. 천천히 돌기 시작해 천천히 멈춤(smoothstep).
                    // 2π를 더한 나머지로 turn과 같은 0 이상 2π 미만으로 맞춤
                    double eased = turned * turned * (3 - 2 * turned);
                    player.Angle = (outward - Math.PI + Math.PI * eased + 4 * Math.PI) % (2 * Math.PI);

                    // 도는 동안에도 제자리에서 발을 떼어 Walk. 클라가 걷기 시각부터 지난 시간으로 도는 발과 걷는 발을 가름.
                    // 뜸을 들이는 동안과 다 걸은 뒤에는 Stand
                    player.Pose = turned < 1 || (ratio > 0 && ratio < 1) ? Pose.Walk : Pose.Stand;
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
                playerStates.Add(new PlayerState(player.Id, player.X, player.Y, player.Angle, player.Pose, player.Ready, player.Nickname, player.Color, player.Wins, player.Pitch, player.Character, player.Seat, player.Hand, player.DrawnAt, player.Kills));
            }
            messages.Add(new StateMessage(_tickNumber, playerStates));
            return messages;
        }
    }

    // 방 목록에 실을 지금 모습. 들어온 순서대로 사람마다 닉네임과 색 (기술 설계서 메시지 rooms)
    public RoomSummary Summarize()
    {
        lock (_lock)
        {
            var members = new List<RoomMember>();

            // 판 도중 나간 사람은 몸만 남아 있고 방에 없음 (SHEEP-200)
            foreach (var player in _players.Where(p => p.LeftAt == null))
            {
                members.Add(new RoomMember(player.Nickname, player.Color));
            }
            return new RoomSummary(_roomId, Name, members, _maxPlayers, InRound);
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

    // players를 그 인원수로 시작 배치에 세우고 rounds발을 장전함. 판이 끝나 준비 중으로 돌아갈 때와 재대결 카운트다운에 씀. lock 안에서 부름
    private void PlaceAtStart(List<Player> players, int rounds)
    {
        for (int i = 0; i < players.Count; i++)
        {
            var spot = StartPlacement.At(players.Count, i, CenterX, CenterY);
            var player = players[i];
            player.X = spot.X;
            player.Y = spot.Y;
            player.Angle = spot.Angle;
            player.Pose = Pose.Stand;
            player.Pitch = 0; // 다시 서면 정면을 봄 (시스템 기획서 조작 위아래 보기)
            player.DownAt = null;
            player.DownedBy = null;
            player.Ammo = rounds;
            player.LastShotAt = null;
            player.DrawnAt = null;

            // 다시 선 사람은 관전이 풀림. 서 있는 동안 누가 반샷인지 받지 않게 함
            _watching.Remove(player.Id);
        }
    }

    // 판정할 차례가 된 발사를 판정해 맞은 사람에게 반샷을 더하거나 쓰러뜨림. 쏜 사람에게만 갈 사격 결과,
    // 쓰러지지 않은 맞은 사람에게만 갈 반샷 알림(둘 다 그 사람을 보는 관전자에게도 감), 모두에게 갈 탈락 알림,
    // 쓰러진 사람에게만 갈 쓰러뜨린 사람 알림을 돌려줌. lock 안에서 부름
    private List<object> JudgeShots(double now)
    {
        var messages = new List<object>();
        var due = _pending.FindAll(s => s.At + ShotJudge.MaxDelay <= now);
        if (due.Count == 0)
        {
            return messages;
        }
        _pending.RemoveAll(s => s.At + ShotJudge.MaxDelay <= now);

        // 판 도중 나가 아직 서 있는 사람은 나간 시각에 빠진 것으로 넘김. 그보다 먼저 쏜 발사만 그 사람을 맞힘 (SHEEP-200)
        var players = new List<(Guid Id, double X, double Y, double? DownAt, int Wounds)>();
        foreach (var player in _players)
        {
            players.Add((player.Id, player.X, player.Y, player.DownAt ?? player.LeftAt, player.Wounds));
        }

        foreach (var outcome in ShotJudge.Judge(due, players, ArenaMap.Fixed))
        {
            // 쏜 사람이 판정 전에 나갔으면 받을 사람이 없어 아무에게도 안 감 (TickLoop.MessagesFor)
            AddDirect(messages, outcome.ShooterId, new ShotResultMessage(outcome));
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
                target.DownedBy = outcome.ShooterId;

                // 쏜 사람이 판정 전에 나갔으면 셀 사람이 없음 (SHEEP-169)
                var shooter = _players.Find(p => p.Id == outcome.ShooterId);
                if (shooter != null)
                {
                    shooter.Kills++;
                }
                messages.Add(new EliminatedMessage(target.Id, EliminationReason.Shot, outcome.Zone, outcome.Height, outcome.X, outcome.Y, outcome.ShooterId));

                // 판정이 쏜 시각 순서라 이 발사가 마지막으로 맞힌 발사임. 쓰러진 사람의 다시 보기에만 쓰여 관전자에게 복사하지 않음
                messages.Add(new Direct(target.Id, new DownedByMessage(outcome.ShooterId, outcome.At, outcome.Zone!.Value)));
            }
            else
            {
                // 반샷은 모두가 봄 (사용자 결정 2026-11-09, SHEEP-126). 모두에게 가므로 관전자에게 따로 복사하지 않음
                messages.Add(new WoundedMessage(target.Id, outcome.Height, outcome.X, outcome.Y));
            }
        }
        return messages;
    }

    // 판 도중 서 있다가 나간 사람을 나간 시각에서 W가 지난 틱에 자살로 쓰러뜨리고, 모두에게 갈 탈락 알림을 돌려줌. 나간 시각보다 먼저 쏜 발사는
    // 그때까지 다 와서 이 틱의 JudgeShots에서 이미 판정됨. 그 발사에 쓰러졌으면 쏜 사람의 킬로 남아 여기서는 건너뜀. 승리가 정해진 뒤 나간
    // 이긴 사람은 결투가 끝난 뒤라 선 채로 둠 (사용자 결정 2026-11-11, SHEEP-200). lock 안에서 부름
    private List<object> LeaveOut(double now)
    {
        var messages = new List<object>();
        if (_round.Next == RoundPhase.Lobby)
        {
            return messages;
        }

        foreach (var player in _players)
        {
            if (player.LeftAt is not double leftAt || player.Pose == Pose.Down || now < leftAt + ShotJudge.MaxDelay)
            {
                continue;
            }

            player.Pose = Pose.Down;
            player.DownAt = leftAt;
            messages.Add(new EliminatedMessage(player.Id, EliminationReason.Left));
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
        var messages = new List<object>();

        // 서 있다 나간 사람은 W 뒤에 쓰러짐(LeaveOut). 그 전에 정하면 나간 사람을 남은 사람으로 셈 (SHEEP-200)
        if (_players.Exists(p => p.LeftAt != null && p.Pose != Pose.Down))
        {
            return messages;
        }

        // 방에 남은 사람 중 쓰러지지 않은 사람과 남은 총알, 그리고 가장 늦게 맞은 시각을 찾음. 나간 사람의 몸은 셈하지 않아 다음 라운드로 가지 않음
        var present = _players.FindAll(p => p.LeftAt == null);
        var alive = new List<(Guid Id, int Ammo)>();
        double? lastDownAt = null;
        foreach (var player in present)
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

        // 쓰러진 사람이 맞은 시각과 같은 ms에 쏜 발사가 아직 오지 않았을 수 있음. W가 지나야 정함 (기술 설계서 메시지 result)
        if (lastDownAt != null && now < lastDownAt.Value + ShotJudge.MaxDelay)
        {
            return messages;
        }

        // 둘 이상 남았으면 아직 판정하지 않은 발사가 누군가를 쓰러뜨릴 수 있음. 모두 총알이 없어도 마지막 발사를 판정한 뒤에 정함
        if (alive.Count >= 2 && _pending.Count > 0)
        {
            return messages;
        }

        // 가장 늦게 맞은 사람과, 같은 시각에 맞은 사람들. 함께 쓰러진 것임
        var lastDown = new List<Guid>();
        foreach (var player in present)
        {
            if (player.Pose == Pose.Down && player.DownAt == lastDownAt)
            {
                lastDown.Add(player.Id);
            }
        }

        var result = RoundFlow.Result(alive, lastDown);
        if (result == null)
        {
            return messages;
        }

        // 남은 발사는 이긴 사람이 쏜 것이거나 맞은 뒤의 무효 발사뿐이라 맞을 사람이 없음. 다시 선 사람을 맞히지 않게 버림
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
        _round = new Round(RoundPhase.Duel, next, now + (next == RoundPhase.Lobby ? RoundFlow.WinResultDuration : RoundFlow.ResultDuration));

        // Step은 판이 바뀐 틱에만 다음 단계를 알리는데, 여기서는 판을 직접 바꿔 Step이 바뀐 것을 모름. 그래서 여기서 알림
        messages.Add(result);
        messages.Add(new PhaseMessage(next, _round.NextAt));
        return messages;
    }

    // to에게만 갈 메시지를 담고, to를 보는 관전자에게도 바로 뒤에 같은 메시지를 담음. 그래서 관전자는 대상과 같은 순서로 받음. lock 안에서 부름
    private void AddDirect(List<object> messages, Guid to, object message)
    {
        messages.Add(new Direct(to, message));
        foreach (var (spectator, target) in _watching)
        {
            if (target == to)
            {
                messages.Add(new Direct(spectator, message));
            }
        }
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

    // 지금 방에 있는 사람이 쓰지 않는 가장 작은 좌석 번호. 인원 제한까지만 받아 좌석 수 안에서 늘 찾음. lock 안에서 부름
    private int FreeSeat()
    {
        int seat = 0;
        while (_players.Exists(p => p.Seat == seat))
        {
            seat++;
        }
        return seat;
    }

    // players를 섞은 시작 자리 순서로 늘어놓음. 이대로 PlaceAtStart에 넘기면 k번째 사람이 k번 자리에 섬 (SHEEP-186)
    private List<Player> Shuffled(List<Player> players) => [.. _startOrder(players.Count).Select(i => players[i])];

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
