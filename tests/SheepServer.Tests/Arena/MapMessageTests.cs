namespace SheepServer.Tests.Arena;

using System.Text.Json;
using SheepServer.Arena;

public class MapMessageTests
{
    private readonly MapMessage _message = new(ArenaMap.Fixed.Width, ArenaMap.Fixed.Height, ArenaMap.Fixed.Cells);

    [Fact]
    public void Serialize_ThenDeserialize_KeepsWidthHeightCells()
    {
        // Act
        string json = JsonSerializer.Serialize(_message, JsonSerializerOptions.Web);
        var restored = JsonSerializer.Deserialize<MapMessage>(json, JsonSerializerOptions.Web);

        // Assert
        Assert.NotNull(restored);
        Assert.Equal(_message.Width, restored.Width);
        Assert.Equal(_message.Height, restored.Height);
        Assert.Equal(_message.Cells, restored.Cells);
    }

    [Fact]
    public void Serialize_UsesMessageTableNames()
    {
        // Act
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(_message, JsonSerializerOptions.Web));
        var root = doc.RootElement;

        // Assert
        // 기술 설계서 메시지 표: type은 map, 필드 이름은 camelCase
        Assert.Equal("map", root.GetProperty("type").GetString());
        Assert.True(root.TryGetProperty("width", out _));
        Assert.True(root.TryGetProperty("height", out _));
        Assert.True(root.TryGetProperty("cells", out _));
    }
}
