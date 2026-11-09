using System.Globalization;
using System.Text;

namespace SheepServer.Rooms;

// 닉네임 규칙 (시스템 기획서 방 화면 닉네임). 클라도 같은 규칙으로 막아야, 클라가 받아 준 이름을 서버가 버리지 않음
public static class Nickname
{
    public const int MinLength = 2;
    public const int MaxLength = 8;

    // Room·RoomRegistry를 이름 없이 부를 때(테스트) 쓰는 이름. 클라 메시지는 닉네임이 없으면 error라 여기까지 오지 않음 (SHEEP-73)
    public const string Placeholder = "총잡이";

    // 앞뒤 공백을 지우고 규칙에 맞으면 그 이름, 아니면 null. 어느 나라 글자든 받고, 글자 수는 눈에 보이는 글자(문자소)로 셈.
    // 보이지 않는 글자가 들어 있으면 받지 않음. 빈 이름이나 남의 이름 흉내를 막기 위함
    public static string? Normalize(string? raw)
    {
        string? name = raw?.Trim();
        if (name == null)
        {
            return null;
        }

        // char는 이모지 같은 글자를 반쪽(서로게이트)으로 나눠 글자 종류를 알 수 없어, 글자 하나(Rune)씩 봄
        foreach (Rune rune in name.EnumerateRunes())
        {
            if (IsInvisible(rune))
            {
                return null;
            }
        }

        int length = new StringInfo(name).LengthInTextElements;
        return length is >= MinLength and <= MaxLength ? name : null;
    }

    // 제어(Cc)·서식(Cf)·줄과 문단 구분(Zl, Zp) 문자와 한글 채움 문자. 한글 채움 문자는 글자(Lo)로 분류돼 따로 막음
    private static bool IsInvisible(Rune rune) =>
        Rune.GetUnicodeCategory(rune) is UnicodeCategory.Control or UnicodeCategory.Format
            or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator
        || rune.Value is 0x115F or 0x1160 or 0x3164 or 0xFFA0;
}
