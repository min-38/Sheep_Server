using System.Text.Json.Serialization;

namespace SheepServer.Rooms;

[JsonConverter(typeof(JsonStringEnumConverter<Pose>))]
public enum Pose
{
    [JsonStringEnumMemberName("stand")]
    Stand,

    [JsonStringEnumMemberName("walk")]
    Walk,

    // 맞으면 판이 끝날 때까지 이 동작임
    [JsonStringEnumMemberName("down")]
    Down
}