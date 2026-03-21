namespace ConquerMono.Infrastructure.FileLoaders;

// ── PuzzleFileLoader ──────────────────────────────────────────────────────────

public sealed class PuzzleFileLoader : IPuzzleFileLoader
{
    private readonly string _conquerDir;

    public PuzzleFileLoader(string conquerDirectory) => _conquerDir = conquerDirectory;

    public Puzzle Load(Stream stream)
    {
        using var r = new BinaryReader(stream);

        var puzzle = new Puzzle
        {
            PuzzleType      = r.ReadASCIIString(8),
            AniPath         = r.ReadASCIIString(256),
            HorizontalTiles = r.ReadInt32(),
            VerticalTiles   = r.ReadInt32()
        };

        puzzle.Tiles = new short[puzzle.HorizontalTiles, puzzle.VerticalTiles];
        for (int y = 0; y < puzzle.VerticalTiles; y++)
        for (int x = 0; x < puzzle.HorizontalTiles; x++)
            puzzle.Tiles[x, y] = r.ReadInt16();

        if (puzzle.PuzzleType == "PUZZLE2")
        {
            puzzle.HorizontalRate = r.ReadInt32();
            puzzle.VerticalRate   = r.ReadInt32();
        }

        return puzzle;
    }

    public int GetTileSize(Puzzle puzzle, IPackageReader packageReader)
    {
        var aniPath = Path.Combine(_conquerDir, puzzle.AniPath);
        if (!File.Exists(aniPath)) return 0;

        using var aniStream = File.OpenRead(aniPath);
        var ani = new Animation.AniParser().Parse(aniStream);

        var tile = puzzle.Tiles[0, 0];
        if (tile == -1) return 0;

        var frames = ani.GetFrames($"Puzzle{tile}");
        if (frames.Count == 0) return 0;

        try
        {
            using var texStream = packageReader.LoadFile(frames[0]);
            if (Path.GetExtension(frames[0]).Equals(".dds", StringComparison.OrdinalIgnoreCase))
                return Graphics.DDSHelper.GetWidth(texStream);
        }
        catch { /* fall through */ }

        return 0;
    }
}

// ── SceneFileLoader ───────────────────────────────────────────────────────────

public sealed class SceneFileLoader : ISceneFileLoader
{
    public Scene Load(Stream stream)
    {
        using var r = new BinaryReader(stream);

        var scene = new Scene();
        int count = r.ReadInt32();

        for (int i = 0; i < count; i++)
        {
            var part = new ScenePart
            {
                AniPath     = r.ReadASCIIString(256),
                AniName     = r.ReadASCIIString(64),
                ImageOffset = r.ReadPoint(),
                Interval    = r.ReadInt32(),
                Size        = r.ReadSize(),
                Thick       = r.ReadInt32(),
                Location    = r.ReadPoint(),
                Height      = r.ReadInt32()
            };

            part.Cells = new MapCell[part.Size.Width, part.Size.Height];
            for (int j = 0; j < part.Size.Height; j++)
            for (int k = 0; k < part.Size.Width;  k++)
            {
                part.Cells[k, j] = new MapCell
                {
                    Access  = (MapCellAccessType)r.ReadInt32(),
                    Surface = (short)r.ReadInt32(),
                    Height  = (short)r.ReadInt32()
                };
            }

            scene.SceneParts.Add(part);
        }

        return scene;
    }
}
