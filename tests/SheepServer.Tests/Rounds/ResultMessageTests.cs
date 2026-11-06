namespace SheepServer.Tests.Rounds;

using System.Text.Json;
using SheepServer.Rounds;

public class ResultMessageTests
{
    [Fact]
    public void Serialize_Win_UsesMessageTableNames()
    {
        // Arrange
        var winner = Guid.NewGuid();

        // Act
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(new ResultMessage(Outcome.Win, [winner], 0), JsonSerializerOptions.Web));
        var root = doc.RootElement;

        // Assert
        // 기술 설계서 메시지 result: outcome은 숫자가 아니라 이름, playerIds는 id 배열, 승리면 rounds는 0
        Assert.Equal("result", root.GetProperty("type").GetString());
        Assert.Equal("win", root.GetProperty("outcome").GetString());
        Assert.Equal(new[] { winner }, root.GetProperty("playerIds").EnumerateArray().Select(id => id.GetGuid()));
        Assert.Equal(0, root.GetProperty("rounds").GetInt32());
    }

    [Fact]
    public void Serialize_Rematch_UsesMessageTableNames()
    {
        // Arrange
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        // Act
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(new ResultMessage(Outcome.Rematch, [a, b], 2), JsonSerializerOptions.Web));
        var root = doc.RootElement;

        // Assert
        Assert.Equal("result", root.GetProperty("type").GetString());
        Assert.Equal("rematch", root.GetProperty("outcome").GetString());
        Assert.Equal(new[] { a, b }, root.GetProperty("playerIds").EnumerateArray().Select(id => id.GetGuid()));
        Assert.Equal(2, root.GetProperty("rounds").GetInt32());
    }
}
