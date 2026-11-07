using System.Text.Json.Serialization;

namespace SheepServer.Shooting;

// 맞은 부위 (기술 설계서 명중 판정 명중). JSON에는 숫자가 아니라 메시지 표의 이름으로 나감 (기술 설계서 메시지 shotResult)
[JsonConverter(typeof(JsonStringEnumConverter<HitZone>))]
public enum HitZone
{
    [JsonStringEnumMemberName("head")]
    Head,

    [JsonStringEnumMemberName("body")]
    Body,
}
