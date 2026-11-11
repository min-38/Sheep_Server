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

    // 판 도중 서 있다가 방을 나가 자살함. 나가기 전에 쏜 발사에 쓰러졌으면 Shot임 (사용자 결정 2026-11-11, SHEEP-200)
    [JsonStringEnumMemberName("left")]
    Left,
}
