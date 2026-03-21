namespace ConquerMono.Rendering.Drawing;

public class PuzzleDrawingComponent : BaseDrawingComponent, IDisposable
{
    private record struct ScreenTile(Vector2 Location, Texture2D Texture);

    protected readonly Puzzle        _puzzle;
    protected readonly IAniDictionary _ani;
    protected readonly TextureCache  _cache;
    private   readonly List<ScreenTile> _tiles = new();

    private const int EXTRA = 2;

    public PuzzleDrawingComponent(Puzzle puzzle, IAniDictionary ani, TextureCache cache)
    {
        _puzzle = puzzle;
        _ani    = ani;
        _cache  = cache;
        _ani.Add(_puzzle.AniPath);
    }

    public override void UpdateScreen(Rectangle sr)
    {
        _tiles.Clear();
        if (!Enabled || _puzzle.TileSize == 0) return;

        int numX = Math.Min(sr.Width  / _puzzle.TileSize + EXTRA, _puzzle.HorizontalTiles);
        int numY = Math.Min(sr.Height / _puzzle.TileSize + EXTRA, _puzzle.VerticalTiles);
        int sx   = sr.X / _puzzle.TileSize;
        int sy   = sr.Y / _puzzle.TileSize;
        int offX = -(sr.X % _puzzle.TileSize);
        int offY = -(sr.Y % _puzzle.TileSize);

        for (int x = sx; x < sx + numX; x++)
        for (int y = sy; y < sy + numY; y++)
        {
            if (x < 0 || x >= _puzzle.HorizontalTiles || y < 0 || y >= _puzzle.VerticalTiles) continue;
            var drawPos = new Vector2(offX + (x - sx) * _puzzle.TileSize,
                                     offY + (y - sy) * _puzzle.TileSize);
            LoadTile(_puzzle.Tiles[x, y], drawPos);
        }
    }

    protected virtual void LoadTile(short tileId, Vector2 location)
    {
        if (tileId == -1) return;
        var key = $"Puzzle{tileId}";
        if (!_ani.TryGetFrames(_puzzle.AniPath, key, out var frames) || frames.Count == 0) return;
        try
        {
            var tex = _cache.GetOrLoad(frames[0]);
            _tiles.Add(new ScreenTile(location, tex));
        }
        catch (Exception ex) { Debug.WriteLine($"[Puzzle] tile load fail: {ex.Message}"); }
    }

    public override void Draw(SpriteBatch sb, Matrix transform)
    {
        if (!Enabled) return;
        sb.Begin(SpriteSortMode.Deferred, BlendState.NonPremultiplied, null, null, null, null, transform);
        foreach (var t in _tiles) sb.Draw(t.Texture, t.Location, Color.White);
        sb.End();
    }

    private bool _disposed;
    protected virtual void Dispose(bool disposing) { if (!_disposed) { _tiles.Clear(); _disposed = true; } }
    public void Dispose() { Dispose(true); GC.SuppressFinalize(this); }
}
