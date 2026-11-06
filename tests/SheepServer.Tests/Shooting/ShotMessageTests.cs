namespace SheepServer.Tests.Shooting;

using System.Text.Json;
using SheepServer.Shooting;

public class ShotMessageTests
{
    [Fact]
    public void Serialize_UsesMessageTableNames()
    {
        // Arrange
        var shooter = Guid.NewGuid();

        // Act
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(new ShotMessage(shooter, 3.25), JsonSerializerOptions.Web));
        var root = doc.RootElement;

        // Assert
        // 기술 설계서 메시지 shot: 쏜 사람 id와 쏜 각도(fire의 angle 그대로)
        Assert.Equal("shot", root.GetProperty("type").GetString());
        Assert.Equal(shooter, root.GetProperty("playerId").GetGuid());
        Assert.Equal(3.25, root.GetProperty("angle").GetDouble());
    }
}
