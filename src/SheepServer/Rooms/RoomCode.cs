namespace SheepServer.Rooms;

// KXPM
public static class RoomCode
{
    const string _chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

    public static string GenerateRoomCode(int length = 6)
    {
        var random = new Random();
        var code = new char[length];

        for (int i = 0; i < length; i++)
        {
            code[i] = _chars[random.Next(_chars.Length)];
        }

        return new string(code);
    }
}