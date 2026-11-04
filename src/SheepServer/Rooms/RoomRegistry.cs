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
    public string AddRoom(Guid hostId, int maxPlayers, Func<object, Task>? send = null)
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

            if (maxPlayers > 7)
            {
                throw new ArgumentException("MaxPlayers must be less than or equal to 7.");
            }

            Room newRoom = new Room(roomId, maxPlayers);
            _rooms.Add(roomId, newRoom);

            CancellationTokenSource cts = new CancellationTokenSource();
            TickLoop tickLoop = new TickLoop(newRoom, _logger);
            _roomCancellationTokens[roomId] = cts;

            // await 안 함. 루프는 방이 지워질 때까지 끝나지 않음
            _ = tickLoop.StartAsync(cts.Token);

            // 룸 생성 시 방 만든 방장은 자동 입장
            JoinRoom(newRoom, hostId, send); // "host"는 방장 ID로 가정

            return roomId;
        }
    }

    // Read
    public Room? GetRoomById(string? roomId)
    {
        if (roomId == null) return null;
        return _rooms.TryGetValue(roomId, out var room) ? room : null;
    }

    public JoinResult JoinRoom(string? roomId, Guid playerId, Func<object, Task>? send = null)
    {
        lock (_rooms)
        {
            var room = GetRoomById(roomId);
            if (room == null) return JoinResult.NotFound;
            return JoinRoom(room, playerId, send);
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

    private JoinResult JoinRoom(Room room, Guid playerId, Func<object, Task>? send)
    {
        try
        {
            room.AddPlayer(playerId, send);
            return JoinResult.Joined;
        }
        catch (InvalidOperationException)
        {
            // 꽉 찼거나 판 도중임. 판은 이번 이슈에서 끝나지 않아, 판 도중이면 지금도 판 도중임
            return room.InRound ? JoinResult.Playing : JoinResult.Full;
        }
    }
}