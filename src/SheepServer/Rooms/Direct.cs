namespace SheepServer.Rooms;

// 한 사람에게만 보낼 메시지의 봉투. Step이 돌려주는 목록에 섞이고, TickLoop.MessagesFor가 받는 사람만 안의 Message를 받게 거름.
// 봉투 자체는 밖으로 나가지 않음
public record Direct(Guid To, object Message);
