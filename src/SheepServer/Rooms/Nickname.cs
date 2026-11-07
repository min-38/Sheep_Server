namespace SheepServer.Rooms;

// 닉네임 규칙 (시스템 기획서 방 화면 닉네임). 클라도 같은 규칙으로 막아야, 클라가 받아 준 이름을 서버가 버리지 않음
public static class Nickname
{
    public const int MinLength = 2;
    public const int MaxLength = 8;

    // 클라가 닉네임을 보내기 전에 쓰는 임시 이름.
    // ponytail: 첫 화면(M8 계획 Task C1)이 닉네임을 보내면 지우고, nickname이 없으면 error로 바꿈
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
