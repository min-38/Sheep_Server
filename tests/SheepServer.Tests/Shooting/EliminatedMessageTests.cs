namespace SheepServer.Tests.Shooting;

using System.Text.Json;
using SheepServer.Shooting;

public class EliminatedMessageTests
{
    [Fact]
    public void Serialize_UsesMessageTableNames()
    {
        // Arrange
        var fallen = Guid.NewGuid();

        // Act
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(new EliminatedMessage(fallen), JsonSerializerOptions.Web));
        var root = doc.RootElement;

        // Assert
        // 기술 설계서 메시지 eliminated: 쓰러진 사람 id
        Assert.Equal("eliminated", root.GetProperty("type").GetString());
        Assert.Equal(fallen, root.GetProperty("playerId").GetGuid());
    }
}
