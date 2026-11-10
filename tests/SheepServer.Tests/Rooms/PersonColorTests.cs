namespace SheepServer.Tests.Rooms;

using SheepServer.Rooms;

// 시스템 기획서 방 화면 사람 색: 번호는 0 이상 색 수 미만 정수
public class PersonColorTests
{
    [Fact]
    public void Count_EightColors()
    {
        // Assert
        // 빨강부터 검정까지 8색. 한 방 최대 인원과 같아 방 안에서 늘 겹치지 않게 줄 수 있음
        Assert.Equal(8, PersonColor.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(PersonColor.Count - 1)]
    public void IsValid_InRange_True(int color)
    {
        // Act
        bool valid = PersonColor.IsValid(color);

        // Assert
        Assert.True(valid);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(PersonColor.Count)]
    public void IsValid_OutOfRange_False(int color)
    {
        // Act
        bool valid = PersonColor.IsValid(color);

        // Assert
        Assert.False(valid);
    }
}
