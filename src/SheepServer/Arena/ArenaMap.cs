namespace SheepServer.Arena;

public class ArenaMap
{
    // ponytail: 크기는 임시 값. 다 걸으면 중심에서 4칸이고 벽 안쪽까지 9.5칸이라 넉넉함.
    // 걷는 거리를 늘리면 RoomTests의 Step_SevenFinishWalk_AllOnEmptyCells가 벽에 닿는지 확인함
    private const int Size = 21;

    // 고정된 결투장 하나를 모든 방이 같이 씀 (기술 설계서 결투장)
    public static readonly ArenaMap Fixed = new ArenaMap(Size, Size, BuildCells());

    public ArenaMap(int width, int height, int[][] cells)
    {
        Width = width;
        Height = height;
        Cells = cells;
    }

    public int Width { get; }
    public int Height { get; }
    public int[][] Cells { get; }

    // Sheep_Client src/map.ts(SHEEP-20)의 임시 맵을 옮겨 옴. 둘레만 벽인 빈 결투장
    private static int[][] BuildCells()
    {
        var cells = new int[Size][];
        for (int y = 0; y < Size; y++)
        {
            cells[y] = new int[Size];
            for (int x = 0; x < Size; x++)
            {
                if (x == 0 || y == 0 || x == Size - 1 || y == Size - 1)
                {
                    cells[y][x] = 1;
                }
            }
        }
        return cells;
    }
}
