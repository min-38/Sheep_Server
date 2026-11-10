using SheepServer.Shooting;

namespace SheepServer.Rooms;

public class Player
{
    public Player(Guid id, double x, double y, double angle, string nickname, int color, int character = 0, int seat = 0, Hand hand = Hand.Right)
    {
        Id = id;
        X = x;
        Y = y;
        Angle = angle;
        Ready = false;
        Nickname = nickname;
        Color = color;
        Character = character;
        Seat = seat;
        Hand = hand;
    }

    public Guid Id { get; }

    // 포스터에 쓰는 이름. 같은 이름도 받으므로 사람은 Id와 목수건 색으로 가름
    public string Nickname { get; }

    // 사람 색 번호(PersonColor). 방 안에서 겹치지 않고, 대기실에서 바꿀 수 있음(Room.Dress). 클라가 핵심 파츠를 이 색으로 칠함 (컨셉 기획서 사람 구별)
    public int Color { get; set; }

    // 캐릭터 번호(Character). 들어올 때 고르고 대기실에서 바꿀 수 있음(Room.Dress) (시스템 기획서 캐릭터 고르기)
    public int Character { get; set; }

    // 술집 바 좌석 번호(0 이상 방 인원 제한 미만). 들어올 때 받고 방에 있는 동안 바뀌지 않음. 색을 바꿔도 앉은 자리가 그대로이게 색과 따로 둠
    public int Seat { get; }

    // 총을 든 손. 들어올 때 받고 방에 있는 동안 바뀌지 않음. 설정에서 바꾼 손은 다음에 들어올 때 반영함 (SHEEP-133)
    public Hand Hand { get; }

    public double X { get; set; }
    public double Y { get; set; }
    public double Angle { get; set; }

    // 위아래 시선(라디안, 위가 +, -Room.MaxPitch 이상 Room.MaxPitch 이하). 처음과 다시 설 때 0.
    // 판정에는 쓰지 않고(fire가 쏜 순간의 pitch를 따로 보냄) 관전하는 클라가 화면을 그리는 데만 씀
    public double Pitch { get; set; }

    public Pose Pose { get; set; } = Pose.Stand;
    public bool Ready { get; set; }

    // 남은 총알. 처음엔 가득 참. 재대결 때는 재대결하는 사람 수만큼 다시 장전함
    public int Ammo { get; set; } = Revolver.Capacity;

    // 맞은 시각(ms, 서버 시계). 맞힌 발사의 쏜 시각임. 서 있으면 null.
    // 이 시각에서 D 안에 쏜 발사는 받고, 아무도 안 남았을 때 함께 쓰러진 사람들을 찾는 데 씀 (기술 설계서 명중 판정 함께 쓰러짐)
    public double? DownAt { get; set; }

    // 마지막으로 받아들인 발사의 쏜 시각(ms, 서버 시계). 아직 안 쐈으면 null
    public double? LastShotAt { get; set; }

    // 이번 라운드에 D 키로 총을 뽑기 시작한 시각(ms, 서버 시계). 아직 안 뽑았으면 null. 다른 사람이 뽑는 모습을 그리게 state에 실음 (SHEEP-123)
    public double? DrawnAt { get; set; }

    // 맞은 반샷 수. 몸을 맞으면 늘고 ShotJudge.WoundsToDown에 닿으면 쓰러짐. 판이 끝날 때까지 남음 (컨셉 기획서 규칙 머리는 즉사, 몸은 반샷)
    public int Wounds { get; set; }

    // 이번 라운드 걷기가 시작할 때의 반샷 수. 함께 쓰러져 다음 라운드로 가는 사람은 이 값으로 되돌림 (컨셉 기획서 규칙 함께 쓰러짐)
    public int RoundStartWounds { get; set; }

    // 이번 라운드에 걷다 멈춘 거리(칸). 멈추지 않았으면 null이고 최대 걸음까지 걸음 (컨셉 기획서 규칙 걷기와 멈추기).
    // 시작 배치에 다시 설 때 지움
    public double? StopDistance { get; set; }

    // 이 방에서 이긴 횟수. 클라가 × $100을 현상금으로 적음. 방을 나가면 Player와 함께 사라짐 (시스템 기획서 방 화면 현상금)
    public int Wins { get; set; }
}