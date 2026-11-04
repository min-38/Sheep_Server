using System.Diagnostics;

namespace SheepServer.Clock;

// 서버의 시계 하나. serverTime의 ts와 phase의 at이 같은 시계여야 클라가 자기 시계로 바꿀 수 있음 (기술 설계서 시간 맞추기)
public static class ServerClock
{
    // 되돌아가지 않는 시계. 컴퓨터 시각을 바꿔도 흔들리지 않음
    private static readonly Stopwatch Watch = Stopwatch.StartNew();

    // 처음 쓴 뒤 지난 시간(ms). 서버가 뜨고 첫 요청 즈음부터 잼
    public static double Now => Watch.Elapsed.TotalMilliseconds;
}
