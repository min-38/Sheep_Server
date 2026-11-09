namespace SheepServer.Tests.Rooms;

using SheepServer.Rooms;

// 시스템 기획서 캐릭터: 번호는 0 이상 캐릭터 수 미만 정수
public class CharacterTests
{
    [Fact]
    public void Count_EightCharacters()
    {
        // Assert
        // 0 찰스부터 7 디에고까지 (시스템 기획서 캐릭터 목록, SHEEP-117)
        Assert.Equal(8, Character.Count);
    }

    [Fact]
    public void IsValid_FirstCharacter_True()
    {
        // Act
        bool valid = Character.IsValid(0);

        // Assert
        Assert.True(valid);
    }

    [Fact]
    public void IsValid_LastCharacter_True()
    {
        // Act
        bool valid = Character.IsValid(Character.Count - 1);

        // Assert
        Assert.True(valid);
    }

    [Fact]
    public void IsValid_Negative_False()
    {
        // Act
        bool valid = Character.IsValid(-1);

        // Assert
        Assert.False(valid);
    }

    [Fact]
    public void IsValid_PastLast_False()
    {
        // Act
        bool valid = Character.IsValid(Character.Count);

        // Assert
        Assert.False(valid);
    }
}
