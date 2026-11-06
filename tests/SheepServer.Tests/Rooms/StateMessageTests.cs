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
        var player = new PlayerState(Guid.NewGuid(), 1.5, 2.5, 0.5, pose, false);

        // Act
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(new StateMessage(1, [player]), JsonSerializerOptions.Web));
        var sent = doc.RootElement.GetProperty("players")[0];

        // Assert
        // 기술 설계서 메시지 state: 동작은 숫자가 아니라 이름으로 감
        Assert.Equal(name, sent.GetProperty("pose").GetString());
    }
}
