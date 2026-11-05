using System.Text.Json.Serialization;

namespace SheepServer.Rooms;

[JsonConverter(typeof(JsonStringEnumConverter<Pose>))]
public enum Pose
{
    [JsonStringEnumMemberName("stand")]
    Stand,

    [JsonStringEnumMemberName("walk")]
    Walk
}