namespace SheepServer.Rooms;

// 방 목록에 실을 한 사람. 포스터와 같은 닉네임과 목수건 색 번호(0~7)
public record RoomMember(string Nickname, int Color);
