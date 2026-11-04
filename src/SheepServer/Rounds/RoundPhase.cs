using System.Text.Json.Serialization;

namespace SheepServer.Rounds;

// JSON에는 숫자가 아니라 메시지 표의 이름으로 나감 (기술 설계서 메시지 phase)
[JsonConverter(typeof(JsonStringEnumConverter<RoundPhase>))]
public enum RoundPhase
{
    [JsonStringEnumMemberName("lobby")]
    Lobby,

    [JsonStringEnumMemberName("countdown")]
    Countdown,

    [JsonStringEnumMemberName("walk")]
    Walk,

    [JsonStringEnumMemberName("duel")]
    Duel,
}
