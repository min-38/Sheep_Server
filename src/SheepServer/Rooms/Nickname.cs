namespace SheepServer.Rooms;

// 닉네임 규칙 (시스템 기획서 방 화면 닉네임). 클라도 같은 규칙으로 막아야, 클라가 받아 준 이름을 서버가 버리지 않음
public static class Nickname
{
    public const int MinLength = 2;
    public const int MaxLength = 8;

    // Room·RoomRegistry를 이름 없이 부를 때(테스트) 쓰는 이름. 클라 메시지는 닉네임이 없으면 error라 여기까지 오지 않음 (SHEEP-73)
    public const string Placeholder = "총잡이";

    // 앞뒤 공백을 지우고 규칙에 맞으면 그 이름, 아니면 null. 한글(완성형 가~힣, 호환 자모 ㄱ~ㅣ)·영문·숫자만 받음.
    // 자모 바로 뒤의 한글 채움 문자(U+3164)는 보이지 않는 글자라 넣지 않음. 받는 글자는 모두 UTF-16 한 단위라 Length가 글자 수임
    public static string? Normalize(string? raw)
    {
        string? name = raw?.Trim();
        if (name == null || name.Length < MinLength || name.Length > MaxLength)
        {
            return null;
        }

        foreach (char c in name)
        {
            if (!(c is >= '가' and <= '힣' or >= 'ㄱ' and <= 'ㅣ' || char.IsAsciiLetterOrDigit(c)))
            {
                return null;
            }
        }
        return name;
    }
}
