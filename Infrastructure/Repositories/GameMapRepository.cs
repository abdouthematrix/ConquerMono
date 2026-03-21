namespace ConquerMono.Infrastructure.Repositories;

public sealed class GameMapRepository
{
    private readonly Dictionary<int, GameMap> _maps = new();

    public GameMapRepository(string gameMapFilePath)
    {
        if (!File.Exists(gameMapFilePath)) return;

        using var fs = new FileStream(gameMapFilePath, FileMode.Open, FileAccess.Read);
        using var r  = new BinaryReader(fs);

        int count = r.ReadInt32();
        for (int i = 0; i < count; i++)
        {
            var m = new GameMap
            {
                Id       = r.ReadInt32(),
                Path     = ReadString(r, r.ReadInt32()),
                TileSize = r.ReadInt32()
            };
            _maps.TryAdd(m.Id, m);
        }
    }

    public IReadOnlyDictionary<int, GameMap> GetAllMaps() => _maps;
    public GameMap? GetById(int id) => _maps.TryGetValue(id, out var m) ? m : null;

    public GameMap? GetByName(string name)
    {
        foreach (var m in _maps.Values)
            if (Path.GetFileNameWithoutExtension(m.Path)
                    .Equals(name, StringComparison.OrdinalIgnoreCase))
                return m;
        return null;
    }

    private static string ReadString(BinaryReader r, int len)
    {
        var b = r.ReadBytes(len);
        return Encoding.ASCII.GetString(b);
    }
}
