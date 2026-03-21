namespace ConquerMono.Infrastructure.FileLoaders;

public static class BinaryReaderExtensions
{
    public static string ReadASCIIString(this BinaryReader r, int length)
    {
        var bytes     = r.ReadBytes(length);
        var nullIndex = Array.IndexOf(bytes, (byte)0);
        return Encoding.ASCII.GetString(bytes, 0, nullIndex >= 0 ? nullIndex : length);
    }

    public static MapPoint ReadPoint(this BinaryReader r) =>
        new(r.ReadInt32(), r.ReadInt32());

    public static MapSize ReadSize(this BinaryReader r) =>
        new(r.ReadInt32(), r.ReadInt32());
}
