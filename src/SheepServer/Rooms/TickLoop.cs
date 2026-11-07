using ILogger = Microsoft.Extensions.Logging.ILogger;
using System.Diagnostics;
using SheepServer.Clock;

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

    // playerId에게 보낼 메시지만 순서대로 냄. 다른 사람에게 갈 Direct는 건너뛰고, 내게 온 Direct는 봉투를 벗겨 안의 메시지만 냄
    public static IEnumerable<object> MessagesFor(Guid playerId, IEnumerable<object> messages)
    {
        foreach (var message in messages)
        {
            if (message is not Direct direct)
            {
                yield return message;
            }
            else if (direct.To == playerId)
            {
                yield return direct.Message;
            }
        }
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
                // 판 단계 시각과 serverTime이 같은 시계를 씀. 아래 _stopwatch는 틱마다 다시 재는 다른 시계라 쓰지 않음
                var messages = _room.Step(ServerClock.Now);
                _stopwatch.Stop();

                if (_stopwatch.ElapsedMilliseconds > _tickInterval.TotalMilliseconds)
                {
                    _logger.LogWarning("[TickLoop] ROOM {RoomId}, Tick processing took {ElapsedMilliseconds} ms, which exceeds the tick interval of {TickInterval} ms.", _room.RoomId, _stopwatch.ElapsedMilliseconds, _tickInterval.TotalMilliseconds);
                }

                // 기술 설계서: 틱마다 방 안 모두에게 상태를 보냄. 판 단계 알림이 있으면 state보다 먼저 감.
                // 연결마다 보낼 큐(SendQueue)에 넣기만 해, 느린 연결 하나가 방의 틱을 늦추지 않음
                foreach (var (id, send) in _room.GetSenders())
                {
                    try
                    {
                        foreach (var message in MessagesFor(id, messages))
                        {
                            await send(message);
                        }
                    }
                    // 막 나간 사람의 연결이면 던짐. 아래 catch까지 가면 이 방의 틱 루프가 영영 멈추므로 그 사람만 건너뜀
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "[TickLoop] ROOM {RoomId}, Failed to send state.", _room.RoomId);
                    }
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
