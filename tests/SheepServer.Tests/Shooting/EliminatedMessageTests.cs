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
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(new EliminatedMessage(fallen, EliminationReason.Shot), JsonSerializerOptions.Web));
        var root = doc.RootElement;

        // Assert
        // 기술 설계서 메시지 eliminated: 쓰러진 사람 id와 사유
        Assert.Equal("eliminated", root.GetProperty("type").GetString());
        Assert.Equal(fallen, root.GetProperty("playerId").GetGuid());
        Assert.Equal("shot", root.GetProperty("reason").GetString());
    }

    [Fact]
    public void Serialize_Timeout_ReasonTimeout()
    {
        // Arrange
        var fallen = Guid.NewGuid();

        // Act
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(new EliminatedMessage(fallen, EliminationReason.Timeout), JsonSerializerOptions.Web));

        // Assert
        // 제한 시간 안에 쏘지 않아 자살로 쓰러짐. 숫자가 아니라 메시지 표의 이름으로 나감
        Assert.Equal("timeout", doc.RootElement.GetProperty("reason").GetString());
    }
}
