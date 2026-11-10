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
        // 제한 시간 안에 쏘지 않아 자살로 쓰러짐. 숫자가 아니라 메시지 표의 이름으로 나감. 맞은 자리는 없어 모두 null
        Assert.Equal("timeout", doc.RootElement.GetProperty("reason").GetString());
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("zone").ValueKind);
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("x").ValueKind);
    }

    [Fact]
    public void Serialize_Shot_CarriesHitPoint()
    {
        // Arrange
        var fallen = Guid.NewGuid();

        // Act
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(new EliminatedMessage(fallen, EliminationReason.Shot, HitZone.Head, 0.85, 6.45, 5.5), JsonSerializerOptions.Web));
        var root = doc.RootElement;

        // Assert
        // 쓰러뜨린 발사가 맞은 자리. 모두가 피와 상처를 그 자리에 그림 (SHEEP-126)
        Assert.Equal("head", root.GetProperty("zone").GetString());
        Assert.Equal(0.85, root.GetProperty("height").GetDouble());
        Assert.Equal(6.45, root.GetProperty("x").GetDouble());
        Assert.Equal(5.5, root.GetProperty("y").GetDouble());
    }
}
