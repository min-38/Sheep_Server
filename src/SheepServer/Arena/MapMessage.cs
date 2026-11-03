namespace SheepServer.Arena;

public class MapMessage
{
    public int Width { get; }
    public int Height { get; }
    public int[][] Cells { get; }

    public string Type => "map";

    public MapMessage(int width, int height, int[][] cells)
    {
        Width = width;
        Height = height;
        Cells = cells;
    }
}
