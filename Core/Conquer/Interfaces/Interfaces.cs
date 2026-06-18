namespace ConquerMono.Conquer.Interfaces;

// ── File I/O ────────────────────────────────────────────────────────
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
// ── Drawing ───────────────────────────────────────────────────────────────────

public interface IDrawingComponent
{
    bool Enabled { get; set; }
    void UpdateScreen(Rectangle screenRect);
    void Draw(SpriteBatch spriteBatch, Matrix transformMatrix);
}
