namespace SheepServer.Rooms;

// 사람 색 번호 규칙 (시스템 기획서 방 화면 사람 색). 번호는 0 이상 Count 미만 정수이고, 메시지의 color로 오감
public static class PersonColor
{
    // 색 수. 한 방 최대 인원(8명)과 같아 방 안에서 늘 겹치지 않게 줄 수 있음. 클라 roomlist.ts의 BANDANA 수와 같아야 함
    public const int Count = 8;

    public static bool IsValid(int color) => color >= 0 && color < Count;
}
