namespace SheepServer.Tests.Shooting;

using System.Text.Json;
using SheepServer.Shooting;

public class WoundedMessageTests
{
    [Fact]
    public void Serialize_TypeOnly()
    {
        // Act
        string json = JsonSerializer.Serialize(new WoundedMessage(), JsonSerializerOptions.Web);

        // Assert
        // 기술 설계서 메시지 wounded: 필드 없이 type만. 누가 쐈는지는 알리지 않음
        Assert.Equal("""{"type":"wounded"}""", json);
    }
}
