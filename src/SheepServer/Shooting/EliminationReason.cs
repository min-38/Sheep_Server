using System.Text.Json.Serialization;

namespace SheepServer.Shooting;

// 쓰러진 사유 (기술 설계서 메시지 eliminated). JSON에는 숫자가 아니라 메시지 표의 이름으로 나감
[JsonConverter(typeof(JsonStringEnumConverter<EliminationReason>))]
public enum EliminationReason
{
    // 머리나 두 번째 반샷을 맞음
    [JsonStringEnumMemberName("shot")]
    Shot,

    // 제한 시간 안에 쏘지 않아 자살함 (컨셉 기획서 규칙 제한 시간)
    [JsonStringEnumMemberName("timeout")]
    Timeout,
}
