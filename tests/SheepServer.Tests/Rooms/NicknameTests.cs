namespace SheepServer.Tests.Rooms;

using SheepServer.Rooms;

public class NicknameTests
{
    [Theory]
    [InlineData("보안관", "보안관")]
    [InlineData("  Kid7 ", "Kid7")]
    [InlineData("가나", "가나")]
    [InlineData("가나다라마바사아", "가나다라마바사아")]
    [InlineData("Billy99", "Billy99")]
    [InlineData("ㅁㅁ", "ㅁㅁ")]
    [InlineData("ㅋㅋ보안관", "ㅋㅋ보안관")]
    [InlineData("東京太郎", "東京太郎")]
    [InlineData("张三", "张三")]
    [InlineData("Иван", "Иван")]
    [InlineData("Müller", "Müller")]
    [InlineData("Mu\u0308ller", "Mu\u0308ller")]
    [InlineData("José", "José")]
    [InlineData("보 안", "보 안")]
    [InlineData("보안관!", "보안관!")]
    [InlineData("<script>", "<script>")]
    [InlineData("🎲🎲", "🎲🎲")]
    [InlineData("🎲🎲🎲🎲🎲🎲🎲🎲", "🎲🎲🎲🎲🎲🎲🎲🎲")]
    public void Normalize_ValidName_TrimmedName(string raw, string expected)
    {
        // Act
        string? name = Nickname.Normalize(raw);

        // Assert
        // 시스템 기획서 방 화면 닉네임: 앞뒤 공백을 지우고 2~8자, 어느 나라 글자든 받음. 글자 수는 눈에 보이는 글자(문자소)로 셈.
        // 그래서 u와 붙임 꼴 점(U+0308)은 한 글자이고, 이모지 8개는 UTF-16으로 16칸이어도 8자임.
        // <script>도 글자 그대로 받음. 클라는 이름을 HTML로 읽지 않음(textContent와 캔버스 글자)
        Assert.Equal(expected, name);
    }

    [Theory]
    [InlineData("가")]
    [InlineData("가나다라마바사아자")]
    [InlineData("🎲🎲🎲🎲🎲🎲🎲🎲🎲")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("a\nb")] // 제어 문자(Cc): 줄바꿈
    [InlineData("a\u0007b")] // 제어 문자(Cc): 벨
    [InlineData("ab\u200B")] // 서식 문자(Cf): 폭 없는 공백
    [InlineData("\u202Eab")] // 서식 문자(Cf): 글자 방향을 오른쪽에서 왼쪽으로 바꿈
    [InlineData("\uFEFFab")] // 서식 문자(Cf): 바이트 순서 표시. 공백이 아니라 Trim이 지우지 않음
    [InlineData("\U0001F468\u200D\U0001F469\u200D\U0001F467")] // 서식 문자(Cf): 폭 없는 이음 문자로 이은 가족 이모지
    [InlineData("a\u2028b")] // 줄 구분 문자(Zl)
    [InlineData("a\u2029b")] // 문단 구분 문자(Zp)
    [InlineData("\u3164\u3164")] // 한글 채움 문자
    [InlineData("ab\u3164")]
    [InlineData("a\u115Fb")]
    [InlineData("a\u1160b")]
    [InlineData("a\uFFA0b")]
    public void Normalize_InvalidName_Null(string? raw)
    {
        // Act
        string? name = Nickname.Normalize(raw);

        // Assert
        // 2~8자를 벗어나거나, 보이지 않는 글자가 들어 있으면 받지 않음. 보이지 않는 글자로 빈 이름을 만들거나
        // 남의 이름을 흉내 내지 못하게 하기 위함 (시스템 기획서 방 화면 닉네임)
        Assert.Null(name);
    }

    [Fact]
    public void Placeholder_IsValidNickname()
    {
        // Assert
        // 이름 없이 부를 때 쓰는 임시 이름도 규칙에 맞아야 방 이름과 포스터가 깨지지 않음
        Assert.Equal(Nickname.Placeholder, Nickname.Normalize(Nickname.Placeholder));
    }
}
