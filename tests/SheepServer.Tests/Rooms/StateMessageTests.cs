namespace SheepServer.Tests.Rooms;

using System.Text.Json;
using SheepServer.Rooms;

public class StateMessageTests
{
    [Theory]
    [InlineData(Pose.Stand, "stand")]
    [InlineData(Pose.Walk, "walk")]
    [InlineData(Pose.Down, "down")]
    public void Serialize_PoseUsesMessageTableNames(Pose pose, string name)
    {
        // Arrange
        var player = new PlayerState(Guid.NewGuid(), 1.5, 2.5, 0.5, pose, false, "보안관", 0, 0, 0, 0, 0);

        // Act
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(new StateMessage(1, [player]), JsonSerializerOptions.Web));
        var sent = doc.RootElement.GetProperty("players")[0];

        // Assert
        // 기술 설계서 메시지 state: 동작은 숫자가 아니라 이름으로 감
        Assert.Equal(name, sent.GetProperty("pose").GetString());
    }

    [Fact]
    public void Serialize_NicknameAndColor()
    {
        // Arrange
        var player = new PlayerState(Guid.NewGuid(), 1.5, 2.5, 0.5, Pose.Stand, true, "보안관", 3, 0, 0, 0, 0);

        // Act
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(new StateMessage(1, [player]), JsonSerializerOptions.Web));
        var sent = doc.RootElement.GetProperty("players")[0];

        // Assert
        // 기술 설계서 메시지 state: 포스터와 목수건에 쓸 닉네임과 색 번호(0~7)
        Assert.Equal("보안관", sent.GetProperty("nickname").GetString());
        Assert.Equal(3, sent.GetProperty("color").GetInt32());
    }

    [Fact]
    public void Serialize_Wins()
    {
        // Arrange
        var player = new PlayerState(Guid.NewGuid(), 1.5, 2.5, 0.5, Pose.Stand, false, "보안관", 3, 2, 0, 0, 0);

        // Act
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(new StateMessage(1, [player]), JsonSerializerOptions.Web));
        var sent = doc.RootElement.GetProperty("players")[0];

        // Assert
        // 기술 설계서 메시지 state: 이 방에서 이긴 횟수. 클라가 현상금(× $100)으로 적음
        Assert.Equal(2, sent.GetProperty("wins").GetInt32());
    }

    [Fact]
    public void Serialize_Pitch()
    {
        // Arrange
        var player = new PlayerState(Guid.NewGuid(), 1.5, 2.5, 0.5, Pose.Stand, false, "보안관", 3, 0, 0.25, 0, 0);

        // Act
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(new StateMessage(1, [player]), JsonSerializerOptions.Web));
        var sent = doc.RootElement.GetProperty("players")[0];

        // Assert
        // 기술 설계서 메시지 state: 위아래 시선. turn의 pitch와 같음
        Assert.Equal(0.25, sent.GetProperty("pitch").GetDouble());
    }

    [Fact]
    public void Serialize_Character()
    {
        // Arrange
        var player = new PlayerState(Guid.NewGuid(), 1.5, 2.5, 0.5, Pose.Stand, false, "보안관", 3, 0, 0, 0, 0);

        // Act
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(new StateMessage(1, [player]), JsonSerializerOptions.Web));
        var sent = doc.RootElement.GetProperty("players")[0];

        // Assert
        // 기술 설계서 메시지 state: 들어올 때 고른 캐릭터 번호. 클라가 그 사람의 모델을 고름 (SHEEP-90)
        Assert.Equal(0, sent.GetProperty("character").GetInt32());
    }

    [Fact]
    public void Serialize_Seat()
    {
        // Arrange
        var player = new PlayerState(Guid.NewGuid(), 1.5, 2.5, 0.5, Pose.Stand, false, "보안관", 3, 0, 0, 0, 2);

        // Act
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(new StateMessage(1, [player]), JsonSerializerOptions.Web));
        var sent = doc.RootElement.GetProperty("players")[0];

        // Assert
        // 기술 설계서 메시지 state: 술집 바 좌석 번호. 색을 바꿔도 그대로임 (SHEEP-118)
        Assert.Equal(2, sent.GetProperty("seat").GetInt32());
    }
}
