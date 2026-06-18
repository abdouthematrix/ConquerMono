using ConquerMono.Map.Rendering.Coordinates;
using ConquerMono.Map.Rendering.Primitives;

namespace ConquerMono.Map.Rendering.Drawing;

// All grid/debug overlay components are lightweight: they share the same
// VertexBuilder pattern but draw bounding-box outlines rather than filled
// tiles. Keeping them in one file avoids clutter.

// ── EffectDrawingComponent ────────────────────────────────────────────────────

/// <summary>Shows a coloured diamond at each 3-D effect location (debug overlay).</summary>
public sealed class EffectDrawingComponent : BaseDrawingComponent, IDisposable
{
    private readonly IList<Map3DEffect>         _effects;
    private readonly MapCellCollection          _cells;
    private readonly IsometricCoordinateSystem  _coords;
    private readonly CellVertexBuilder _vb;

    public EffectDrawingComponent(
        IList<Map3DEffect> effects, MapCellCollection cells,
        IsometricCoordinateSystem coords, GraphicsDevice gd)
    {
        _effects = effects; _cells = cells; _coords = coords;
        _vb = new CellVertexBuilder(coords.CellPoints, gd);
    }

    public override void UpdateScreen(Rectangle sr)
    {
        _vb.Begin(_effects.Count);
        if (!Enabled) { _vb.End(); return; }

        foreach (var eff in _effects)
        {
            var cell = _cells.World2Cell(eff.Location.X, eff.Location.Y);
            var sp   = _coords.MapToScreen(cell);
            if (sp.X < sr.X - 64 || sp.X > sr.Right + 64 ||
                sp.Y < sr.Y - 32 || sp.Y > sr.Bottom + 32) continue;
            _vb.AddCell(new Vector2(sp.X - sr.X - 32, sp.Y - sr.Y - 16),
                        new Color(255, 140, 0, 200));
        }
        _vb.End();
    }

    public override void Draw(SpriteBatch sb, Matrix t) { if (Enabled) _vb.Draw(t); }
    public void Dispose() => _vb.Dispose();
}

// ── SoundDrawingComponent ─────────────────────────────────────────────────────

/// <summary>Shows a coloured diamond at each ambient sound location (debug overlay).</summary>
public sealed class SoundDrawingComponent : BaseDrawingComponent, IDisposable
{
    private readonly IList<MapSound>            _sounds;
    private readonly MapCellCollection          _cells;
    private readonly IsometricCoordinateSystem  _coords;
    private readonly CellVertexBuilder _vb;

    public SoundDrawingComponent(
        IList<MapSound> sounds, MapCellCollection cells,
        IsometricCoordinateSystem coords, GraphicsDevice gd)
    {
        _sounds = sounds; _cells = cells; _coords = coords;
        _vb = new CellVertexBuilder(coords.CellPoints, gd);
    }

    public override void UpdateScreen(Rectangle sr)
    {
        _vb.Begin(_sounds.Count);
        if (!Enabled) { _vb.End(); return; }

        foreach (var s in _sounds)
        {
            var cell = _cells.World2Cell(s.Location.X, s.Location.Y);
            var sp   = _coords.MapToScreen(cell);
            if (sp.X < sr.X - 64 || sp.X > sr.Right + 64 ||
                sp.Y < sr.Y - 32 || sp.Y > sr.Bottom + 32) continue;
            _vb.AddCell(new Vector2(sp.X - sr.X - 32, sp.Y - sr.Y - 16),
                        new Color(160, 0, 255, 200));
        }
        _vb.End();
    }

    public override void Draw(SpriteBatch sb, Matrix t) { if (Enabled) _vb.Draw(t); }
    public void Dispose() => _vb.Dispose();
}

// ── PuzzleGridDrawingComponent ────────────────────────────────────────────────

/// <summary>Draws the pixel-space tile grid of the main puzzle (debug overlay).</summary>
public sealed class PuzzleGridDrawingComponent : BaseDrawingComponent, IDisposable
{
    private readonly Puzzle  _puzzle;
    private readonly CellVertexBuilder _vb;

    private static readonly Color LINE_COLOR = new(255, 255, 255, 40);

    public PuzzleGridDrawingComponent(Puzzle puzzle, GraphicsDevice gd)
    {
        _puzzle = puzzle;
        // Borrow cell-points (just a horizontal line for each tile row/col)
        _vb = new CellVertexBuilder(
            new[]
            {
                new Vector2(0, 0), new Vector2(_puzzle.TileSize, 0),
                new Vector2(_puzzle.TileSize, _puzzle.TileSize), new Vector2(0, _puzzle.TileSize)
            }, gd);
    }

    public override void UpdateScreen(Rectangle sr)
    {
        if (_puzzle.TileSize == 0) return;
        int cols = sr.Width  / _puzzle.TileSize + 2;
        int rows = sr.Height / _puzzle.TileSize + 2;
        _vb.Begin(cols * rows);

        int sx = sr.X / _puzzle.TileSize, sy = sr.Y / _puzzle.TileSize;
        int ox = -(sr.X % _puzzle.TileSize), oy = -(sr.Y % _puzzle.TileSize);

        for (int x = sx; x < sx + cols && x < _puzzle.HorizontalTiles; x++)
        for (int y = sy; y < sy + rows && y < _puzzle.VerticalTiles;   y++)
        {
            _vb.AddCell(
                new Vector2(ox + (x - sx) * _puzzle.TileSize,
                            oy + (y - sy) * _puzzle.TileSize),
                LINE_COLOR);
        }
        _vb.End();
    }

    public override void Draw(SpriteBatch sb, Matrix t) { if (Enabled) _vb.Draw(t); }
    public void Dispose() => _vb.Dispose();
}

// ── BackdropGridDrawingComponent ──────────────────────────────────────────────

/// <summary>Same as PuzzleGrid but sized to a backdrop puzzle (debug overlay).</summary>
public sealed class BackdropGridDrawingComponent : BaseDrawingComponent, IDisposable
{
    private readonly PuzzleGridDrawingComponent _inner;

    public BackdropGridDrawingComponent(Puzzle backdrop, Puzzle main, GraphicsDevice gd)
    {
        _inner = new PuzzleGridDrawingComponent(backdrop, gd);
    }

    public override void UpdateScreen(Rectangle sr) => _inner.UpdateScreen(sr);
    public override void Draw(SpriteBatch sb, Matrix t) { if (Enabled) _inner.Draw(sb, t); }
    public void Dispose() => _inner.Dispose();
}

// ── TerrainObjectGridDrawingComponent ─────────────────────────────────────────

/// <summary>Draws bounding diamonds around each terrain object (debug overlay).</summary>
public sealed class TerrainObjectGridDrawingComponent : BaseDrawingComponent, IDisposable
{
    private readonly IList<MapTerrainObject>    _objects;
    private readonly IsometricCoordinateSystem  _coords;
    private readonly IAniDictionary             _ani;
    private readonly TextureCache               _cache;
    private readonly CellVertexBuilder _vb;

    private static readonly Color GRID_COLOR = new(0, 220, 220, 120);

    public TerrainObjectGridDrawingComponent(
        IList<MapTerrainObject> objects, IsometricCoordinateSystem coords,
        IAniDictionary ani, TextureCache cache, GraphicsDevice? gd)
    {
        _objects = objects; _coords = coords; _ani = ani; _cache = cache;
        _vb = new CellVertexBuilder(coords.CellPoints, gd!);
    }

    public override void UpdateScreen(Rectangle sr)
    {
        _vb.Begin(_objects.Count);
        if (!Enabled) { _vb.End(); return; }

        foreach (var obj in _objects)
        {
            var sp = _coords.MapToScreen(new Vector2(obj.Location.X, obj.Location.Y));
            if (sp.X < sr.X - 128 || sp.X > sr.Right + 128 ||
                sp.Y < sr.Y -  64 || sp.Y > sr.Bottom +  64) continue;
            _vb.AddCell(new Vector2(sp.X - sr.X - 32, sp.Y - sr.Y - 16), GRID_COLOR);
        }
        _vb.End();
    }

    public override void Draw(SpriteBatch sb, Matrix t) { if (Enabled) _vb.Draw(t); }
    public void Dispose() => _vb.Dispose();
}

// ── SceneGridDrawingComponent ─────────────────────────────────────────────────

/// <summary>Draws bounding diamonds for every scene part (debug overlay).</summary>
public sealed class SceneGridDrawingComponent : BaseDrawingComponent, IDisposable
{
    private readonly IList<MapScene>            _scenes;
    private readonly IsometricCoordinateSystem  _coords;
    private readonly IAniDictionary             _ani;
    private readonly TextureCache               _cache;
    private readonly IPackageReader             _pkg;
    private readonly ISceneFileLoader           _loader;
    private readonly CellVertexBuilder _vb;
    private readonly Dictionary<string, Scene>  _loaded = new();

    private static readonly Color GRID_COLOR = new(0, 80, 255, 120);

    public SceneGridDrawingComponent(
        IList<MapScene> scenes, IsometricCoordinateSystem coords,
        IAniDictionary ani, TextureCache cache,
        IPackageReader pkg, ISceneFileLoader loader, GraphicsDevice gd)
    {
        _scenes = scenes; _coords = coords; _ani = ani;
        _cache  = cache;  _pkg    = pkg;    _loader = loader;
        _vb = new CellVertexBuilder(coords.CellPoints, gd);

        foreach (var ms in _scenes)
        {
            try
            {
                if (!_loaded.ContainsKey(ms.ScenePath))
                    _loaded[ms.ScenePath] = _loader.Load(_pkg.LoadFile(ms.ScenePath));
            }
            catch { /* skip */ }
        }
    }

    public override void UpdateScreen(Rectangle sr)
    {
        _vb.Begin(_scenes.Count * 4);
        if (!Enabled) { _vb.End(); return; }

        foreach (var ms in _scenes)
        {
            if (!_loaded.TryGetValue(ms.ScenePath, out var scene)) continue;
            foreach (var part in scene.SceneParts)
            {
                var cell = new Vector2(ms.Location.X + part.Location.X,
                                       ms.Location.Y + part.Location.Y);
                var sp = _coords.MapToScreen(cell);
                if (sp.X < sr.X - 128 || sp.X > sr.Right + 128 ||
                    sp.Y < sr.Y -  64 || sp.Y > sr.Bottom +  64) continue;
                _vb.AddCell(new Vector2(sp.X - sr.X - 32, sp.Y - sr.Y - 16), GRID_COLOR);
            }
        }
        _vb.End();
    }

    public override void Draw(SpriteBatch sb, Matrix t) { if (Enabled) _vb.Draw(t); }
    public void Dispose() { _vb.Dispose(); _loaded.Clear(); }
}
