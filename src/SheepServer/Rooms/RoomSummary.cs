namespace SheepServer.Rooms;

// 방 목록(rooms)의 한 줄 (기술 설계서 메시지 rooms). Playing은 판 도중(카운트다운이 정해진 뒤부터 lobby 시각까지)인지.
// 꽉 찼는지는 Players 수와 Max로 클라가 가름
public record RoomSummary(string Code, string Name, List<RoomMember> Players, int Max, bool Playing);
