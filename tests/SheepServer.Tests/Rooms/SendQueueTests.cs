namespace SheepServer.Tests.Rooms;

using Microsoft.Extensions.Logging.Abstractions;
using SheepServer.Rooms;

public class SendQueueTests
{
    [Fact]
    public async Task RunAsync_SendsInOrderUntilCompleted()
    {
        // Arrange
        var sent = new List<object>();
        var queue = new SendQueue(m =>
        {
            sent.Add(m);
            return Task.CompletedTask;
        }, () => { });
        queue.Enqueue(1);
        queue.Enqueue(2);
        queue.Enqueue(3);
        queue.Complete();

        // Act
        await queue.RunAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

        // Assert
        // 넣은 순서대로 감. 판 단계 알림이 state보다 먼저 가야 하는 순서를 지킴 (기술 설계서 메시지 phase)
        Assert.Equal(new object[] { 1, 2, 3 }, sent);
    }

    [Fact]
    public void Enqueue_OverCapacity_OverflowOnce()
    {
        // Arrange
        // 보내는 작업을 돌리지 않아 하나도 빠지지 않음. 끝없이 늦는 연결과 같음
        int overflows = 0;
        var queue = new SendQueue(_ => Task.CompletedTask, () => overflows++);
        for (int i = 0; i < SendQueue.Capacity; i++)
        {
            queue.Enqueue(i);
        }

        // Act
        int beforeFull = overflows;
        queue.Enqueue(SendQueue.Capacity);
        queue.Enqueue(SendQueue.Capacity + 1);

        // Assert
        // 한도까지는 받고, 넘으면 한 번만 알림. 받는 쪽은 그 연결을 끊음
        Assert.Equal(0, beforeFull);
        Assert.Equal(1, overflows);
    }

    [Fact]
    public void Enqueue_AfterComplete_NoOverflow()
    {
        // Arrange
        int overflows = 0;
        var queue = new SendQueue(_ => Task.CompletedTask, () => overflows++);
        queue.Complete();

        // Act
        // 연결을 닫는 중에 틱 루프가 넣는 메시지. 밀린 것이 아니라 끊지 않음
        queue.Enqueue(1);

        // Assert
        Assert.Equal(0, overflows);
    }

    [Fact]
    public async Task TickLoop_OneConnectionStuck_OthersStillGetEveryTick()
    {
        // Arrange
        var room = new Room("TestRoom", 4);
        var ticks = new List<int>();
        var gotThree = new TaskCompletionSource();
        var never = new TaskCompletionSource();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // 보내기가 끝나지 않는 연결. 큐 없이 기다리면 방의 틱 루프가 여기서 멈춤
        var stuck = new SendQueue(_ => never.Task, () => { });
        _ = stuck.RunAsync(timeout.Token);
        room.AddPlayer(Guid.NewGuid(), EnqueueTo(stuck));
        var healthy = new SendQueue(message =>
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
        }, () => { });
        _ = healthy.RunAsync(timeout.Token);
        room.AddPlayer(Guid.NewGuid(), EnqueueTo(healthy));

        // Act
        var loop = new TickLoop(room, NullLogger.Instance).StartAsync(timeout.Token);
        await gotThree.Task.WaitAsync(timeout.Token);
        timeout.Cancel();
        await loop;

        // Assert
        // 늦는 사람이 있어도 다른 사람은 틱마다 state를 받음 (기술 설계서 아키텍처)
        Assert.Equal(new[] { 1, 2, 3 }, ticks.Take(3));
    }

    // 방은 보내기를 Func<object, Task>로 받음. Program.cs처럼 큐에 넣고 바로 끝나는 Task를 줌
    private static Func<object, Task> EnqueueTo(SendQueue queue) => message =>
    {
        queue.Enqueue(message);
        return Task.CompletedTask;
    };
}
