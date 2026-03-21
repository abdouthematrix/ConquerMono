namespace ConquerMono.Rendering.Drawing;

// ── MapCellDrawingComponent ───────────────────────────────────────────────────

/// <summary>Debug overlay that outlines every visible cell coloured by access type.</summary>
public sealed class MapCellDrawingComponent : BaseDrawingComponent, IDisposable
{
    private const int CW = 64, CHW = 32, CH = 32, CHH = 16;

    private readonly MapCellCollection          _cells;
    private readonly IsometricCoordinateSystem  _coords;
    private readonly CellVertexBuilder _vb;

    public MapCellDrawingComponent(
        MapCellCollection cells, IsometricCoordinateSystem coords, GraphicsDevice gd)
    {
        _cells  = cells;
        _coords = coords;
        _vb     = new CellVertexBuilder(coords.CellPoints, gd);
    }

    public override void UpdateScreen(Rectangle sr)
    {
        int nw = sr.Width  / CW + 2;
        int nh = sr.Width  / CH + 2;
        _vb.Begin(nw * 2 * nh);

        int xOff = sr.X % CW + CHW,  yOff = sr.Y % CH;
        int mw   = _cells.CollectionSize.Width, mh = _cells.CollectionSize.Height;

        for (int x = 0; x < nw * 2; x++)
        {
            int xb = x * CHW - xOff;
            int yb = -yOff - (x & 1) * CHH;
            for (int y = 0; y < nh; y++)
            {
                var sp = new Point(xb + sr.X + CHW, yb + y * CH + sr.Y + CHH);
                var mc = _coords.ScreenToMap(sp);
                int mx = (int)mc.X, my = (int)mc.Y;
                if (mx >= 0 && mx < mw && my >= 0 && my < mh)
                    _vb.AddCell(new Vector2(xb, yb + y * CH), _cells[mx, my].AccessColor);
            }
        }
        _vb.End();
    }

    public override void Draw(SpriteBatch sb, Matrix t) => _vb.Draw(t);
    public void Dispose() => _vb.Dispose();
}

// ── PortalDrawingComponent ────────────────────────────────────────────────────

/// <summary>Draws animated rotating portal sprites at each portal location.</summary>
public sealed class PortalDrawingComponent : BaseDrawingComponent, IDisposable
{
    private record struct ScreenPortal(Vector2 Location);

    private readonly IList<MapPortal>          _portals;
    private readonly IsometricCoordinateSystem _coords;
    private readonly TextureCache              _cache;
    private readonly List<ScreenPortal>        _visible = new();
    private Texture2D? _tex;
    private float _time;

    private const string PORTAL_TEX  = @"c3/effect/exit.dds";
    private const int    IMG_OFF     = 128;
    private const float  SCALE       = 2f;

    public PortalDrawingComponent(
        IList<MapPortal> portals, IsometricCoordinateSystem coords, TextureCache cache)
    {
        _portals = portals; _coords = coords; _cache = cache;
    }

    public override void UpdateScreen(Rectangle sr)
    {
        _visible.Clear();
        try { _tex ??= _cache.GetOrLoad(PORTAL_TEX); } catch { return; }

        foreach (var p in _portals)
        {
            var sp = _coords.MapToScreen(new Vector2(p.Location.X, p.Location.Y));
            if (sp.X < sr.X - IMG_OFF || sp.X > sr.Right  + IMG_OFF ||
                sp.Y < sr.Y - IMG_OFF || sp.Y > sr.Bottom + IMG_OFF) continue;
            _visible.Add(new ScreenPortal(new Vector2(sp.X - sr.X - IMG_OFF / 2,
                                                      sp.Y - sr.Y - IMG_OFF / 2)));
        }
    }

    public override void Draw(SpriteBatch sb, Matrix t)
    {
        if (_tex == null || !Enabled) return;
        _time = (_time + 0.016f) % 600f; // reset every 10 minutes — stays precise
        float rot  = _time / 2f * MathHelper.TwoPi;
        var   col  = Color.White * (0.65f + MathF.Sin(_time / 3f * MathHelper.TwoPi) * 0.35f);
        var   orig = new Vector2(_tex.Width / 2f, _tex.Height / 2f);

        sb.Begin(SpriteSortMode.Deferred, BlendState.NonPremultiplied, null, null, null, null, t);
        foreach (var p in _visible)
            sb.Draw(_tex, p.Location + orig, null, col, rot, orig, SCALE, SpriteEffects.None, 0f);
        sb.End();
    }

    public void Dispose() { _visible.Clear(); }
}

// ── SceneDrawingComponent ─────────────────────────────────────────────────────

/// <summary>Loads and renders animated scene objects (.scene files).</summary>
public sealed class SceneDrawingComponent : BaseDrawingComponent, IDisposable
{
    private record struct AnimPart(Vector2 Location, List<Texture2D> Frames, int Interval);

    private readonly IList<MapScene>            _scenes;
    private readonly IsometricCoordinateSystem  _coords;
    private readonly IAniDictionary             _ani;
    private readonly TextureCache               _cache;
    private readonly IPackageReader             _pkg;
    private readonly ISceneFileLoader           _loader;
    private readonly Dictionary<string, Scene>  _loaded  = new();
    private readonly List<AnimPart>             _visible = new();
    private readonly int _startTick = Environment.TickCount;

    private static readonly Color TINT = new(240, 255, 255, 255);

    public SceneDrawingComponent(
        IList<MapScene> scenes, IsometricCoordinateSystem coords,
        IAniDictionary ani, TextureCache cache,
        IPackageReader pkg, ISceneFileLoader loader)
    {
        _scenes = scenes; _coords = coords; _ani = ani;
        _cache  = cache;  _pkg    = pkg;    _loader = loader;

        foreach (var ms in _scenes) PreloadScene(ms);
    }

    private void PreloadScene(MapScene ms)
    {
        if (_loaded.ContainsKey(ms.ScenePath)) return;
        try
        {
            var scene = _loader.Load(_pkg.LoadFile(ms.ScenePath));
            _loaded[ms.ScenePath] = scene;
            foreach (var p in scene.SceneParts.Select(p => p.AniPath).Distinct())
                _ani.Add(p);
        }
        catch (Exception ex) { Debug.WriteLine($"[Scene] {ms.ScenePath}: {ex.Message}"); }
    }

    public override void UpdateScreen(Rectangle sr)
    {
        _visible.Clear();
        if (!Enabled) return;

        foreach (var ms in _scenes)
        {
            if (!_loaded.TryGetValue(ms.ScenePath, out var scene)) continue;
            foreach (var part in scene.SceneParts)
            {
                var cell = new Vector2(ms.Location.X + part.Location.X,
                                       ms.Location.Y + part.Location.Y);
                var sp = _coords.MapToScreen(cell);

                if (!InBounds(sp, sr, part.ImageOffset)) continue;

                var loc = new Vector2(sp.X - sr.X + part.ImageOffset.X,
                                      sp.Y - sr.Y + part.ImageOffset.Y);

                if (!_ani.TryGetFrames(part.AniPath, part.AniName, out var fps) || fps.Count == 0) continue;

                var frames = fps.Select(f => { try { return _cache.GetOrLoad(f); } catch { return null!; } })
                                .Where(t => t != null).ToList();
                if (frames.Count > 0)
                    _visible.Add(new AnimPart(loc, frames, Math.Max(1, part.Interval)));
            }
        }
    }

    public override void Draw(SpriteBatch sb, Matrix t)
    {
        if (!Enabled) return;
        int tick = Environment.TickCount - _startTick;
        sb.Begin(SpriteSortMode.Deferred, BlendState.NonPremultiplied, null, null, null, null, t);
        foreach (var p in _visible)
        {
            int fi = (tick / p.Interval) % p.Frames.Count;
            sb.Draw(p.Frames[fi], p.Location, TINT);
        }
        sb.End();
    }

    private static bool InBounds(Vector2 sp, Rectangle sr, MapPoint off) =>
        sp.X > sr.X - off.X - 64 && sp.X < sr.Right  + off.X + 64 &&
        sp.Y > sr.Y - off.Y - 32 && sp.Y < sr.Bottom + off.Y + 32;

    public void Dispose() { _visible.Clear(); _loaded.Clear(); }
}

// ── TerrainObjectDrawingComponent ─────────────────────────────────────────────

/// <summary>Renders animated terrain objects sorted by isometric depth.</summary>
public sealed class TerrainObjectDrawingComponent : BaseDrawingComponent, IDisposable
{
    private record struct AnimObj(Vector2 Location, List<Texture2D> Frames, int Interval, MapPoint Cell);

    private readonly IList<MapTerrainObject>    _objects;
    private readonly IsometricCoordinateSystem  _coords;
    private readonly IAniDictionary             _ani;
    private readonly TextureCache               _cache;
    private readonly List<AnimObj>              _visible = new();
    private readonly int _startTick = Environment.TickCount;

    private static readonly Color TINT = new(240, 255, 255, 255);

    public TerrainObjectDrawingComponent(
        IList<MapTerrainObject> objects, IsometricCoordinateSystem coords,
        IAniDictionary ani, TextureCache cache)
    {
        _objects = objects; _coords = coords; _ani = ani; _cache = cache;
        foreach (var p in objects.Select(o => o.AniPath).Distinct()) _ani.Add(p);
    }

    public override void UpdateScreen(Rectangle sr)
    {
        _visible.Clear();
        if (!Enabled) return;

        foreach (var obj in _objects)
        {
            var sp = _coords.MapToScreen(new Vector2(obj.Location.X, obj.Location.Y));
            if (!InBounds(sp, sr, obj.ImageOffset)) continue;

            var loc = new Vector2(sp.X - sr.X - obj.ImageOffset.X,
                                  sp.Y - sr.Y - obj.ImageOffset.Y);

            if (!_ani.TryGetFrames(obj.AniPath, obj.AniName, out var fps) || fps.Count == 0) continue;

            var frames = fps.Select(f => { try { return _cache.GetOrLoad(f); } catch { return null!; } })
                            .Where(t => t != null).ToList();
            if (frames.Count > 0)
                _visible.Add(new AnimObj(loc, frames, Math.Max(1, obj.Interval), obj.Location));
        }

        // Painter's algorithm — farther cells drawn first
        _visible.Sort((a, b) => (a.Cell.X + a.Cell.Y).CompareTo(b.Cell.X + b.Cell.Y));
    }

    public override void Draw(SpriteBatch sb, Matrix t)
    {
        if (!Enabled) return;
        int tick = Environment.TickCount - _startTick;
        sb.Begin(SpriteSortMode.Deferred, BlendState.NonPremultiplied, null, null, null, null, t);
        foreach (var o in _visible)
        {
            int fi = (tick / o.Interval) % o.Frames.Count;
            sb.Draw(o.Frames[fi], o.Location, TINT);
        }
        sb.End();
    }

    private static bool InBounds(Vector2 sp, Rectangle sr, MapPoint off) =>
        sp.X > sr.X - off.X - 64 && sp.X < sr.Right  + off.X + 64 &&
        sp.Y > sr.Y - off.Y - 32 && sp.Y < sr.Bottom + off.Y + 32;

    public void Dispose() => _visible.Clear();
}
