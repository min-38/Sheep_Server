using System.Threading.Channels;

namespace SheepServer.Rooms;

// 연결 하나의 보낼 큐. 틱 루프, 발사 알림, 받기 루프의 답은 넣기만 하고, 보내기는 RunAsync 하나가 순서대로 함.
// 그래서 느린 연결 하나가 방의 틱이나 다른 사람의 받기 루프를 늦추지 않음 (기술 설계서 아키텍처)
public class SendQueue
{
    // 밀려도 되는 메시지 수. 틱마다 state가 하나씩 쌓여 약 5초어치임. 넘으면 그 연결을 끊음.
    // ponytail: 임시 값. 인터넷으로 해 보며 정하고, 끊는 대신 오래된 state만 버릴지도 그때 봄
    public const int Capacity = 100;

    private readonly Channel<object> _channel = Channel.CreateBounded<object>(new BoundedChannelOptions(Capacity) { SingleReader = true });
    private readonly Func<object, Task> _send;
    private readonly Action _overflow;

    // send는 실제로 소켓에 쓰는 함수, overflow는 한도를 넘었을 때 연결을 끊는 함수
    public SendQueue(Func<object, Task> send, Action overflow)
    {
        _send = send;
        _overflow = overflow;
    }

    // 기다리지 않음. 가득 차면 넣지 않고 큐를 닫은 뒤 overflow를 한 번만 부름.
    // 판 단계나 결과를 빼고 보내면 클라가 판을 잘못 알게 되므로, 메시지를 버리지 않고 연결을 끊음
    public void Enqueue(object message)
    {
        if (!_channel.Writer.TryWrite(message) && _channel.Writer.TryComplete())
        {
            _overflow();
        }
    }

    // 연결을 닫을 때 부름. 이미 넣은 메시지는 RunAsync가 마저 보냄
    public void Complete() => _channel.Writer.TryComplete();

    // 큐가 닫히고 다 보낼 때까지 하나씩 보냄. 보내기가 겹치면 WebSocket이 던지므로 이 작업 하나만 보냄
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await foreach (var message in _channel.Reader.ReadAllAsync(cancellationToken))
        {
            await _send(message);
        }
    }
}
