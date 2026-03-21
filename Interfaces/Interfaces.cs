namespace ConquerMono.Interfaces;

// ── Package / File I/O ────────────────────────────────────────────────────────

public interface IPackageReader : IDisposable
{
    void   AddPackage(string fileName);
    Stream LoadFile(string fileName);
}

public interface IMapFileLoader
{
    MapData Load(Stream stream);
}

public interface IPuzzleFileLoader
{
    Puzzle Load(Stream stream);
    int    GetTileSize(Puzzle puzzle, IPackageReader packageReader);
}

public interface ISceneFileLoader
{
    Scene Load(Stream stream);
}

// ── Animation ─────────────────────────────────────────────────────────────────

public interface IAniDictionary
{
    void                        Add(string aniPath);
    IReadOnlyList<string>       this[string aniPath, string animationName] { get; }
    string?                     GetFrame(string aniPath, string animationName, int frameIndex = 0);
    bool                        IsLoaded(string aniPath);
}

// ── Drawing ───────────────────────────────────────────────────────────────────

public interface IDrawingComponent
{
    bool Enabled { get; set; }
    void UpdateScreen(Rectangle screenRect);
    void Draw(SpriteBatch spriteBatch, Matrix transformMatrix);
}
