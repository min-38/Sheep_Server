namespace SheepServer.Tests.Shooting;

using System.Text.Json;
using SheepServer.Shooting;

public class ShotResultMessageTests
{
    [Fact]
    public void Serialize_Head_UsesMessageTableNames()
    {
        // Arrange
        var outcome = new ShotOutcome(Guid.NewGuid(), 100, Guid.NewGuid(), HitZone.Head, 0.8, -0.05, true, 6.45, 5.5);

        // Act
        string json = JsonSerializer.Serialize(new ShotResultMessage(outcome), JsonSerializerOptions.Web);

        // Assert
        // 기술 설계서 메시지 shotResult: 맞은 사람은 알리지 않음. 부위는 이름, 높이와 오른쪽 거리는 칸
        Assert.Equal("""{"type":"shotResult","zone":"head","height":0.8,"offset":-0.05,"down":true}""", json);
    }

    [Fact]
    public void Serialize_Miss_NullsExceptDown()
    {
        // Arrange
        var outcome = new ShotOutcome(Guid.NewGuid(), 100, null, null, 0, 0, false, 0, 0);

        // Act
        string json = JsonSerializer.Serialize(new ShotResultMessage(outcome), JsonSerializerOptions.Web);

        // Assert
        // 빗나가면 부위, 높이, 오른쪽 거리 모두 null. 0으로 보내면 몸 한가운데 바닥에 맞은 것과 구별되지 않음
        Assert.Equal("""{"type":"shotResult","zone":null,"height":null,"offset":null,"down":false}""", json);
    }
}
