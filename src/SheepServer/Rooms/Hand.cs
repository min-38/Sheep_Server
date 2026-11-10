using System.Text.Json.Serialization;

namespace SheepServer.Rooms;

// 총을 든 손 (시스템 기획서 방 화면 설정). 들어올 때 createRoom·joinRoom의 hand로 받고 방에 있는 동안 바뀌지 않음.
// 판정에는 쓰지 않고 다른 사람의 클라가 그 사람을 그리는 데만 씀 (SHEEP-133)
[JsonConverter(typeof(JsonStringEnumConverter<Hand>))]
public enum Hand
{
    [JsonStringEnumMemberName("right")]
    Right,

    [JsonStringEnumMemberName("left")]
    Left
}
