namespace SheepServer.Tests.Rooms;

using System.Net.WebSockets;
using Microsoft.Extensions.Logging.Abstractions;
using SheepServer.Rooms;

public class TickLoopTests
{
    [Fact]
    public async Task StartAsync_OneSendFails_OthersStillGetEveryTick()
    {
        // Arrange
        var room = new Room("TestRoom", 4);
        var ticks = new List<int>();
        var gotThree = new TaskCompletionSource();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // 막 나간 사람의 소켓처럼 보내기가 던짐. 이 때문에 방 전체의 틱 루프가 멈추면 안 됨
        room.AddPlayer(Guid.NewGuid(), _ => throw new WebSocketException());
        // state 말고 판 단계 알림도 같은 길로 가므로 state만 셈
        room.AddPlayer(Guid.NewGuid(), message =>
        {
            if (message is StateMessage state)
            {
                ticks.Add(state.Tick);
                if (ticks.Count == 3)
                {
                    gotThree.TrySetResult();
                }
            }
            return Task.CompletedTask;
        });

        // Act
        var loop = new TickLoop(room, NullLogger.Instance).StartAsync(timeout.Token);
        await gotThree.Task.WaitAsync(timeout.Token);
        timeout.Cancel();
        await loop;

        // Assert
        Assert.Equal(new[] { 1, 2, 3 }, ticks.Take(3));
    }
}
