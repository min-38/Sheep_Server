namespace SheepServer.Tests.Rooms;

using SheepServer.Rooms;

public class RoomNameTests
{
    [Theory]
    [InlineData("먼지 낀 술집", "먼지 낀 술집")]
    [InlineData("  Dusty Saloon ", "Dusty Saloon")]
    [InlineData("東京の酒場", "東京の酒場")]
    [InlineData("🎲🎲", "🎲🎲")]
    [InlineData("ab", "ab")]
    [InlineData("가나다라마바사아자차카타파하가나", "가나다라마바사아자차카타파하가나")]
    public void Normalize_ValidName_TrimmedName(string raw, string expected)
    {
        // Act
        string? name = RoomName.Normalize(raw);

        // Assert
        // 시스템 기획서 방 화면 방 만들기: 아무 글자 2~16자, 모든 언어. 앞뒤 공백은 지움
        Assert.Equal(expected, name);
    }

    [Theory]
    [InlineData("a")]
    [InlineData("가나다라마바사아자차카타파하가나다")]
    [InlineData("a\nb")]
    [InlineData("a\u0007b")]
    [InlineData("   ")]
    [InlineData("👨‍👩‍👧‍👦")]
    [InlineData(null)]
    public void Normalize_InvalidName_Null(string? raw)
    {
        // Act
        string? name = RoomName.Normalize(raw);

        // Assert
        // 글자 수는 눈에 보이는 글자로 셈. 가족 이모지는 여러 코드 포인트지만 한 글자라 1자임. 줄바꿈 같은 제어 문자는 받지 않음
        Assert.Null(name);
    }

    [Fact]
    public void Default_NicknameRoom()
    {
        // Assert
        // 랜덤 입장으로 만들어지거나 이름 없이 만든 방
        Assert.Equal("보안관의 방", RoomName.Default("보안관"));
    }
}
