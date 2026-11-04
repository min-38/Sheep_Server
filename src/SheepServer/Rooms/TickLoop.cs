using ILogger = Microsoft.Extensions.Logging.ILogger;
using System.Diagnostics;

namespace SheepServer.Rooms;

// 방 하나의 틱 루프. 방이 생길 때 시작하고, 방이 지워져 토큰이 취소되면 멈춤
public class TickLoop
{
    private static readonly TimeSpan _tickInterval = TimeSpan.FromMilliseconds(50);    // 기술 설계서: 20Hz
    private readonly Room _room;
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private readonly ILogger _logger;

    public TickLoop(Room room, ILogger logger)
    {
        _room = room;
        _logger = logger;
    }

    // 토큰이 취소될 때까지 끝나지 않음
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(_tickInterval);

        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                _stopwatch.Restart();
                _room.Step();
                _stopwatch.Stop();

                if (_stopwatch.ElapsedMilliseconds > _tickInterval.TotalMilliseconds)
                {
                    _logger.LogWarning("[TickLoop] ROOM {RoomId}, Tick processing took {ElapsedMilliseconds} ms, which exceeds the tick interval of {TickInterval} ms.", _room.RoomId, _stopwatch.ElapsedMilliseconds, _tickInterval.TotalMilliseconds);
                }

                // 1초에 한 번만 찍음. 매 틱 찍으면 방 하나에 초당 20줄
                if (_room.TickNumber % 20 == 0)
                {
                    _logger.LogDebug("[TickLoop] ROOM {RoomId}, Tick Number {TickNumber}", _room.RoomId, _room.TickNumber);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 방이 지워져 토큰이 취소됨. 정상 종료라 Error로 찍지 않음
            _logger.LogInformation("[TickLoop] ROOM {RoomId}, Tick Loop cancelled.", _room.RoomId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TickLoop] ROOM {RoomId}, Tick Loop encountered an error.", _room.RoomId);
        }
    }
}
