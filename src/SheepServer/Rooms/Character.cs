namespace SheepServer.Rooms;

// 캐릭터 번호 규칙 (시스템 기획서 캐릭터). 번호는 0 이상 Count 미만 정수이고, 메시지의 character로 오감
public static class Character
{
    // 고를 수 있는 캐릭터 수. 클라 src/assets/characters/의 캐릭터 JSON 수와 같아야 함 (SHEEP-88).
    // ponytail: 지금은 0번 찰스 하나뿐. 캐릭터를 더할 때 같이 늘림
    public const int Count = 1;

    public static bool IsValid(int character) => character >= 0 && character < Count;
}
