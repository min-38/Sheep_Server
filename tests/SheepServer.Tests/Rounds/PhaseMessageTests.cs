namespace SheepServer.Tests.Rounds;

using System.Text.Json;
using SheepServer.Rounds;

public class PhaseMessageTests
{
    [Theory]
    [InlineData(RoundPhase.Countdown, "countdown")]
    [InlineData(RoundPhase.Walk, "walk")]
    [InlineData(RoundPhase.Duel, "duel")]
    [InlineData(RoundPhase.Lobby, "lobby")] // 판이 끝나 준비 중으로 돌아가는 시각
    public void Serialize_UsesMessageTableNames(RoundPhase phase, string name)
    {
        // Act
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(new PhaseMessage(phase, 1500.25), JsonSerializerOptions.Web));
        var root = doc.RootElement;

        // Assert
        // 기술 설계서 메시지 표: type은 phase, 단계는 숫자가 아니라 이름, at은 서버 시각(ms)
        Assert.Equal("phase", root.GetProperty("type").GetString());
        Assert.Equal(name, root.GetProperty("phase").GetString());
        Assert.Equal(1500.25, root.GetProperty("at").GetDouble());
    }
}
