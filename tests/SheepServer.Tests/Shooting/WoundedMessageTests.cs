namespace SheepServer.Tests.Shooting;

using System.Text.Json;
using SheepServer.Shooting;

public class WoundedMessageTests
{
    [Fact]
    public void Serialize_TargetAndHitPoint()
    {
        // Arrange
        var target = Guid.NewGuid();

        // Act
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(new WoundedMessage(target, 0.5, 3.25, 4.5), JsonSerializerOptions.Web));
        var root = doc.RootElement;

        // Assert
        // 기술 설계서 메시지 wounded: 맞은 사람과 맞은 자리(부위, 높이, 격자 좌표). 방 안 모두에게 감. 누가 쐈는지는 담지 않음 (SHEEP-126)
        Assert.Equal("wounded", root.GetProperty("type").GetString());
        Assert.Equal(target, root.GetProperty("playerId").GetGuid());
        Assert.Equal("body", root.GetProperty("zone").GetString());
        Assert.Equal(0.5, root.GetProperty("height").GetDouble());
        Assert.Equal(3.25, root.GetProperty("x").GetDouble());
        Assert.Equal(4.5, root.GetProperty("y").GetDouble());
    }
}
