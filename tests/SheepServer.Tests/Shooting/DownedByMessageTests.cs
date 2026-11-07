namespace SheepServer.Tests.Shooting;

using System.Text.Json;
using SheepServer.Shooting;

public class DownedByMessageTests
{
    [Fact]
    public void Serialize_UsesMessageTableNames()
    {
        // Arrange
        var shooter = Guid.NewGuid();

        // Act
        string json = JsonSerializer.Serialize(new DownedByMessage(shooter, 1234.5), JsonSerializerOptions.Web);

        // Assert
        // 기술 설계서 메시지 downedBy: 쓰러뜨린 사람 id와 그 발사의 쏜 시각(ms)
        Assert.Equal($$"""{"type":"downedBy","playerId":"{{shooter}}","at":1234.5}""", json);
    }
}
