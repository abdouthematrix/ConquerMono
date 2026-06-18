namespace ConquerMono.Conquer.Services;


/// <summary>
/// Central service that owns the set of <see cref="IDrawingComponent"/>s for a loaded map,
/// manages the <see cref="GameCamera"/>, and dispatches per-frame updates and draws.
///
/// This is a direct port of the WPF <c>MapViewerService</c> adapted for MonoGame:
///  • No WPF dependencies — all input is forwarded from <see cref="InputManager"/>.
///  • The SpriteBatch is provided externally (owned by <see cref="ConquerGame"/>).
///  • Layer toggles use the same <see cref="DrawingAspect"/> enum.
/// </summary>
public sealed class MapViewerService : IDisposable
{
    // ── Dependencies ──────────────────────────────────────────────────────────
    private readonly MapLoadingService  _mapLoader;
    private readonly IAniDictionary     _ani;
    private readonly IPackageReader     _pkg;
    private readonly ISceneFileLoader   _sceneLoader;
    private readonly GraphicsDevice     _gd;

    // ── Live state ────────────────────────────────────────────────────────────
    private MapData?  _mapData;
    private Puzzle?   _puzzle;
    private TextureCache? _texCache;
    private IsometricCoordinateSystem? _coords;

    private readonly Dictionary<DrawingAspect, List<IDrawingComponent>> _components = new();

    // ── Public surface ────────────────────────────────────────────────────────
    public GameCamera Camera { get; }
    public bool       IsMapLoaded => _mapData != null && _puzzle != null;
    public IsometricCoordinateSystem? CoordinateSystem => _coords;
    public MapData?   CurrentMapData => _mapData;

    public MapViewerService(
        MapLoadingService  mapLoader,
        IAniDictionary     ani,
        IPackageReader     pkg,
        ISceneFileLoader   sceneLoader,
        GraphicsDevice     gd)
    {
        _mapLoader   = mapLoader;
        _ani         = ani;
        _pkg         = pkg;
        _sceneLoader = sceneLoader;
        _gd          = gd;
        Camera       = new GameCamera(gd);
    }

    // ── Map loading ───────────────────────────────────────────────────────────

    public void LoadMap(string path, int tileSize)
    {
        DisposeComponents();
        _texCache?.Clear();
        _texCache ??= new TextureCache(_pkg, _gd);

        (_mapData, _puzzle) = _mapLoader.LoadMap(path, tileSize);
        _coords = new IsometricCoordinateSystem(_puzzle, _mapData);
        Camera.Attach(_puzzle, _coords);

        BuildComponents();
        UpdateAllComponents();
    }

    // ── Layer control ─────────────────────────────────────────────────────────

    public void SetLayerEnabled(DrawingAspect aspect, bool enabled)
    {
        if (!_components.TryGetValue(aspect, out var list)) return;
        foreach (var c in list)
        {
            c.Enabled = enabled;
            if (enabled) c.UpdateScreen(Camera.DrawWindow);
        }
    }

    public bool IsLayerEnabled(DrawingAspect aspect)
    {
        return _components.TryGetValue(aspect, out var list) && list.Any(c => c.Enabled);
    }

    // ── Per-frame ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Called once per frame after the camera position/zoom may have changed.
    /// Forwards the new <see cref="GameCamera.DrawWindow"/> to all enabled components.
    /// </summary>
    public void Update()
    {
        if (!IsMapLoaded) return;
        UpdateAllComponents();
    }

    /// <summary>Draw all enabled layers in correct painter's order.</summary>
    public void Draw(SpriteBatch sb)
    {
        if (!IsMapLoaded) return;

        var t = Camera.TransformMatrix;
        DrawLayer(sb, t, DrawingAspect.Backdrop);
        DrawLayer(sb, t, DrawingAspect.Puzzle);
        DrawLayer(sb, t, DrawingAspect.TerrainObject);
        DrawLayer(sb, t, DrawingAspect.Scene);        
        DrawLayer(sb, t, DrawingAspect.Portals);
        DrawLayer(sb, t, DrawingAspect.Effect);
        DrawLayer(sb, t, DrawingAspect.Sound);
        // Debug overlays last
        DrawLayer(sb, t, DrawingAspect.MapCell);
        DrawLayer(sb, t, DrawingAspect.BackdropGrid);
        DrawLayer(sb, t, DrawingAspect.PuzzleGrid);
        DrawLayer(sb, t, DrawingAspect.TerrainObjectGrid);
        DrawLayer(sb, t, DrawingAspect.SceneGrid);
    }

    // ── Private ───────────────────────────────────────────────────────────────

    private void BuildComponents()
    {
        if (_puzzle == null || _mapData == null || _coords == null || _texCache == null) return;

        // Backdrops
        var backdropList = new List<IDrawingComponent>();
        var bgridList    = new List<IDrawingComponent>();
        foreach (var layer in _mapData.Layers)
        foreach (var backdrop in layer.Backdrops.Where(b => b.Puzzle != null))
        {
            backdropList.Add(new BackdropDrawingComponent(backdrop.Puzzle!, _puzzle, _ani, _texCache));
            bgridList   .Add(new BackdropGridDrawingComponent(backdrop.Puzzle!, _puzzle, _gd));
        }
        Register(DrawingAspect.Backdrop,     backdropList);
        Register(DrawingAspect.BackdropGrid, bgridList, enabled: false);

        Register(DrawingAspect.Puzzle,
            new PuzzleDrawingComponent(_puzzle, _ani, _texCache));

        Register(DrawingAspect.MapCell,
            new MapCellDrawingComponent(_mapData.Cells, _coords, _gd),
            enabled: false);

        Register(DrawingAspect.Portals,
            new PortalDrawingComponent(_mapData.Portals, _coords, _texCache));

        Register(DrawingAspect.Scene,
            new SceneDrawingComponent(_mapData.Scenes, _coords, _ani, _texCache, _pkg, _sceneLoader));

        Register(DrawingAspect.TerrainObject,
            new TerrainObjectDrawingComponent(_mapData.TerrainObjects, _coords, _ani, _texCache));

        Register(DrawingAspect.PuzzleGrid,
            new PuzzleGridDrawingComponent(_puzzle, _gd),
            enabled: false);

        Register(DrawingAspect.TerrainObjectGrid,
            new TerrainObjectGridDrawingComponent(_mapData.TerrainObjects, _coords, _ani, _texCache, _gd),
            enabled: false);

        Register(DrawingAspect.SceneGrid,
            new SceneGridDrawingComponent(_mapData.Scenes, _coords, _ani, _texCache, _pkg, _sceneLoader, _gd),
            enabled: false);

        Register(DrawingAspect.Effect,
            new EffectDrawingComponent(_mapData.Effects, _mapData.Cells, _coords, _gd));

        Register(DrawingAspect.Sound,
            new SoundDrawingComponent(_mapData.Sounds, _mapData.Cells, _coords, _gd));
    }

    private void Register(DrawingAspect aspect, IDrawingComponent c, bool enabled = true)
    {
        c.Enabled = enabled;
        _components[aspect] = new List<IDrawingComponent> { c };
    }

    private void Register(DrawingAspect aspect, List<IDrawingComponent> list, bool enabled = true)
    {
        foreach (var c in list) c.Enabled = enabled;
        _components[aspect] = list;
    }

    private void UpdateAllComponents()
    {
        var win = Camera.DrawWindow;
        foreach (var list in _components.Values)
        foreach (var c in list.Where(c => c.Enabled))
            c.UpdateScreen(win);
    }

    private void DrawLayer(SpriteBatch sb, Matrix t, DrawingAspect aspect)
    {
        if (!_components.TryGetValue(aspect, out var list)) return;
        foreach (var c in list.Where(c => c.Enabled))
            c.Draw(sb, t);
    }

    private void DisposeComponents()
    {
        foreach (var list in _components.Values)
        foreach (var c in list)
            (c as IDisposable)?.Dispose();
        _components.Clear();
    }

    public void Dispose()
    {
        DisposeComponents();
        _texCache?.Dispose();
    }
}
