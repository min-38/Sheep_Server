using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SheepServer.Rooms;

public class RoomRegistry
{
    private Dictionary<string, Room> _rooms;
    private Dictionary<string, CancellationTokenSource> _roomCancellationTokens = new Dictionary<string, CancellationTokenSource>();


    private readonly Func<string> _generateCode;

    private readonly ILogger<RoomRegistry> _logger;

    public RoomRegistry(Func<string>? generateCode = null, ILogger<RoomRegistry>? logger = null)
    {
        // 룸 배열 초기화
        _rooms = new Dictionary<string, Room>();

        // 테스트는 정해진 코드를 넘겨 겹침 처리를 확인함. 서버는 무작위 코드를 씀
        _generateCode = generateCode ?? (() => RoomCode.GenerateRoomCode());

        // 서버는 DI가 로거를 넣어 줌. 테스트처럼 로거 없이 만들면 아무것도 찍지 않음
        _logger = logger ?? NullLogger<RoomRegistry>.Instance;
    }

    // Create
    // nickname은 검사를 마친 방장 이름. 없으면 임시 이름 (Room.AddPlayer)
    public string AddRoom(Guid hostId, int maxPlayers, Func<object, Task>? send = null, string? nickname = null, bool isPrivate = false)
    {
        lock (_rooms)
        {
            string roomId = _generateCode();
            // 동일한 room id가 있으면 다시 생성
            while (_rooms.ContainsKey(roomId))
            {
                roomId = _generateCode();
            }

            if (maxPlayers < 1)
            {
                throw new ArgumentException("MaxPlayers must be greater than 0.");
            }

            if (maxPlayers > 8)
            {
                throw new ArgumentException("MaxPlayers must be less than or equal to 8.");
            }

            Room newRoom = new Room(roomId, maxPlayers, isPrivate);
            _rooms.Add(roomId, newRoom);

            CancellationTokenSource cts = new CancellationTokenSource();
            TickLoop tickLoop = new TickLoop(newRoom, _logger);
            _roomCancellationTokens[roomId] = cts;

            // await 안 함. 루프는 방이 지워질 때까지 끝나지 않음
            _ = tickLoop.StartAsync(cts.Token);

            // 룸 생성 시 방 만든 방장은 자동 입장
            JoinRoom(newRoom, hostId, send, nickname); // "host"는 방장 ID로 가정

            return roomId;
        }
    }

    // Read
    public Room? GetRoomById(string? roomId)
    {
        if (roomId == null) return null;
        return _rooms.TryGetValue(roomId, out var room) ? room : null;
    }

    public JoinResult JoinRoom(string? roomId, Guid playerId, Func<object, Task>? send = null, string? nickname = null)
    {
        lock (_rooms)
        {
            var room = GetRoomById(roomId);
            if (room == null) return JoinResult.NotFound;
            return JoinRoom(room, playerId, send, nickname);
        }
    }

    // 랜덤 입장 (시스템 기획서 방 화면 랜덤 입장). 들어갈 수 있는 공개 방(판 도중 아님, 꽉 차지 않음) 중 사람이 가장 많은 방에 넣음.
    // 사람이 많은 방에 넣어야 판이 빨리 시작함. 같으면 코드 순. 없으면 공개 방을 새로 만듦. 들어간 방 코드와 새로 만들었는지를 돌려줌
    public (string Code, bool Created) QuickJoin(Guid playerId, int maxPlayers, Func<object, Task>? send = null, string? nickname = null)
    {
        lock (_rooms)
        {
            var open = _rooms.Values
                .Where(r => !r.IsPrivate && !r.InRound && r.CurrentPlayers < r.MaxPlayers)
                .OrderByDescending(r => r.CurrentPlayers)
                .ThenBy(r => r.RoomId, StringComparer.Ordinal)
                .FirstOrDefault();

            // 고른 뒤 틱 루프가 판을 시작했으면 들어가지 못함. 그때는 새로 만듦
            if (open != null && JoinRoom(open, playerId, send, nickname) == JoinResult.Joined)
            {
                return (open.RoomId, false);
            }
            return (AddRoom(playerId, maxPlayers, send, nickname), true);
        }
    }

    public bool LeaveRoom(string? roomId, Guid playerId)
    {
        lock (_rooms)
        {
            var room = GetRoomById(roomId);
            if (room == null) return false;

            try
            {
                room.RemovePlayer(playerId);
            }
            catch (InvalidOperationException)
            {
                // 이미 나간 플레이어. 끊김 처리에서 두 번 불릴 수 있음
                return false;
            }

            // 마지막 사람이 나가면 방도 지움. 그래야 방 코드가 다시 쓰일 수 있음
            if (room.CurrentPlayers == 0)
            {
                if (_roomCancellationTokens.TryGetValue(room.RoomId, out var cts))
                {
                    cts.Cancel();
                    cts.Dispose();
                    _roomCancellationTokens.Remove(room.RoomId);
                }
                _rooms.Remove(room.RoomId);
            }


            return true;
        }
    }


    // 빈 방 있으면 바로 지우는 기능
    // public void RemoveEmptyRooms()
    // {
    //     lock (_rooms)
    //     {
    //         RemoveAll(room => room.CurrentPlayers == 0);
    //     }
    // }

    private JoinResult JoinRoom(Room room, Guid playerId, Func<object, Task>? send, string? nickname)
    {
        try
        {
            room.AddPlayer(playerId, send, nickname);
            return JoinResult.Joined;
        }
        catch (InvalidOperationException)
        {
            // 꽉 찼거나 판 도중임. 판은 이번 이슈에서 끝나지 않아, 판 도중이면 지금도 판 도중임
            return room.InRound ? JoinResult.Playing : JoinResult.Full;
        }
    }
}