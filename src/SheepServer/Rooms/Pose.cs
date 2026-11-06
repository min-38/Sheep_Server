using System.Text.Json.Serialization;

namespace SheepServer.Rooms;

[JsonConverter(typeof(JsonStringEnumConverter<Pose>))]
public enum Pose
{
    [JsonStringEnumMemberName("stand")]
    Stand,

    [JsonStringEnumMemberName("walk")]
    Walk,

    // 맞으면 판이 끝날 때까지 이 동작임. 재대결하는 사람만 재대결 카운트다운에 다시 섬
    [JsonStringEnumMemberName("down")]
    Down
}