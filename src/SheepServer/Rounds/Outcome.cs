using System.Text.Json.Serialization;

namespace SheepServer.Rounds;

// 판 결과의 종류. JSON에는 숫자가 아니라 메시지 표의 이름으로 나감 (기술 설계서 메시지 result)
[JsonConverter(typeof(JsonStringEnumConverter<Outcome>))]
public enum Outcome
{
    [JsonStringEnumMemberName("win")]
    Win,

    [JsonStringEnumMemberName("rematch")]
    Rematch,
}
