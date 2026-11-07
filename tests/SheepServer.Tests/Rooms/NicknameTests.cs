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
    public void Normalize_ValidName_TrimmedName(string raw, string expected)
    {
        // Act
        string? name = Nickname.Normalize(raw);

        // Assert
        // 시스템 기획서 방 화면 닉네임: 앞뒤 공백을 지우고 2~8자, 한글·영문·숫자만
        Assert.Equal(expected, name);
    }

    [Theory]
    [InlineData("가")]
    [InlineData("가나다라마바사아자")]
    [InlineData("보 안")]
    [InlineData("보안관!")]
    [InlineData("ㅋㅋ")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Normalize_InvalidName_Null(string? raw)
    {
        // Act
        string? name = Nickname.Normalize(raw);

        // Assert
        // 자모만 쓴 이름은 포스터에서 읽기 어려워 받지 않음. 완성형 한글(가~힣)만 한글로 봄
        Assert.Null(name);
    }
}
