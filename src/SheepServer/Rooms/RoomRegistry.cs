using System;
using System.Collections.Generic;

namespace SheepServer.Rooms;

public class RoomRegistry
{
    private Dictionary<string, Room> _rooms;

    private readonly Func<string> _generateCode;

    public RoomRegistry(Func<string>? generateCode = null)
    {
        // 룸 배열 초기화
        _rooms = new Dictionary<string, Room>();

        // 테스트는 정해진 코드를 넘겨 겹침 처리를 확인함. 서버는 무작위 코드를 씀
        _generateCode = generateCode ?? (() => RoomCode.GenerateRoomCode());
    }

    // Create
    public string AddRoom(Guid hostId, int maxPlayers)
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

            // 룸 생성 시 방 만든 방장은 자동 입장
            JoinRoom(newRoom, hostId); // "host"는 방장 ID로 가정

            return roomId;
        }
    }

    // Read
    public Room? GetRoomById(string? roomId)
    {
        if (roomId == null) return null;
        return _rooms.TryGetValue(roomId, out var room) ? room : null;
    }

    // Delete
    public void RemoveRoom(string roomId)
    {
        lock (_rooms)
        {
            var roomToRemove = GetRoomById(roomId);
            if (roomToRemove != null && roomToRemove.CurrentPlayers == 0)
            {
                _rooms.Remove(roomId);
            }
        }
    }

    public bool JoinRoom(string roomId, Guid playerId)
    {
        lock (_rooms)
        {
            var room = GetRoomById(roomId);
            if (room != null)
                return JoinRoom(room, playerId);
        }
        return false; // Room not found
    }

    private bool JoinRoom(Room room, Guid playerId)
    {
        if (room != null)
        {
            try
            {
                room.AddPlayer(playerId);
                return true;
            }
            catch (InvalidOperationException)
            {
                // Room is full
                return false;
            }
        }
        return false; // Room not found
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
                _rooms.Remove(room.RoomId);

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
}