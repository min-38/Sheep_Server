using System.Globalization;

namespace SheepServer.Rooms;

// 방 이름 규칙 (시스템 기획서 방 화면 방 만들기). 아무 글자 2~16자, 모든 언어.
// 클라도 같은 규칙으로 막아야, 클라가 받아 준 이름을 서버가 버리지 않음
public static class RoomName
{
    public const int MinLength = 2;
    public const int MaxLength = 16;

    // 앞뒤 공백을 지우고 규칙에 맞으면 그 이름, 아니면 null. 글자 수는 눈에 보이는 글자(문자소)로 셈.
    // 줄바꿈 같은 제어 문자는 목록과 칠판의 한 줄을 깨뜨려 받지 않음
    public static string? Normalize(string? raw)
    {
        string? name = raw?.Trim();
        if (name == null)
        {
            return null;
        }

        foreach (char c in name)
        {
            if (char.IsControl(c))
            {
                return null;
            }
        }

        int length = new StringInfo(name).LengthInTextElements;
        return length is >= MinLength and <= MaxLength ? name : null;
    }

    // 이름 없이 만든 방과 랜덤 입장으로 새로 만든 방의 이름. 모든 언어에서 같은 영어 이름
    public static string Default(string nickname) => $"{nickname}'s room";
}
