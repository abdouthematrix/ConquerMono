using ConquerMono.Components;
using ConquerMono.Infrastructure.FileLoaders;
using ConquerMono.Infrastructure.FileSystem;
using ConquerMono.Infrastructure.Repositories;
using ConquerMono.World;
using System.IO;
using System.Runtime;

namespace ConquerMono;

/// <summary>
/// Root game class.
///
/// Ownership map
/// ─────────────
///   ConquerGame
///     ├── GameSettings          (loaded from %AppData%)
///     ├── InputManager          (keyboard + mouse)
///     ├── TqPackageReader       (WDF / disk file router)
///     ├── AniDictionary         (ANI index cache)
///     ├── MapLoadingService     (map + puzzle + backdrop loading)
///     ├── MapViewerService      (drawing component manager + GameCamera)
///     ├── PlayerEntity          (position + stats)
///     ├── GameMapRepository     (gamemap.dat index)
///     │
///     └── DrawableGameComponents  (draw in DrawOrder)
///           MapRenderComponent  [0]  → delegates to MapViewerService
///           PlayerComponent    [10]  → 3-D mesh + shadow
///           HudComponent       [20]  → HUD overlay
/// </summary>
public sealed class ConquerGame : Game
{
    // ── MonoGame core ─────────────────────────────────────────────────────────
    private readonly GraphicsDeviceManager _gfx;
    public  SpriteBatch SpriteBatch { get; private set; } = null!;

    // ── Services (available to components via the game reference) ─────────────
    public GameSettings         Settings   { get; private set; } = null!;
    public InputManager         Input      { get; private set; } = null!;
    public PlayerEntity?        Player     { get; private set; }
    public MapViewerService?    MapViewer  { get; private set; }
    public GameMapRepository?   MapRepo    { get; private set; }

    /// <summary>The currently-loaded <see cref="MapData"/> (null until a map is loaded).</summary>
    public MapData? CurrentMapData { get; private set; }

    // ── Private infrastructure ────────────────────────────────────────────────
    private TqPackageReader?   _pkg;
    private MapLoadingService? _mapLoadingService;

    // ─────────────────────────────────────────────────────────────────────────
    public ConquerGame()
    {
        _gfx = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth  = 1280,
            PreferredBackBufferHeight = 720,
            SynchronizeWithVerticalRetrace = true,
        };
        Content.RootDirectory = "Content";
        IsMouseVisible  = true;
        IsFixedTimeStep = true;
        TargetElapsedTime = TimeSpan.FromSeconds(1.0 / 60.0);
    }

    // ─────────────────────────────────────────────────────────────────────────
    protected override void Initialize()
    {
        Window.Title = "ConquerMono  ·  2.5D Isometric Action RPG";

        // ── Settings ──────────────────────────────────────────────────────────
        Settings = GameSettings.Load();
        Input    = new InputManager();

        if (!Settings.IsValid())
        {
            var directory = @"C:\Users\AbdouMatrix\Downloads\CO\6090";
            Settings.ConquerDirectory = directory;
            Settings.GameMapFilePath = Path.Combine(directory, "ini", "gamemap.dat");
        }

        // ── Infrastructure (only if Conquer directory is configured) ──────────
        if (Settings.IsValid())
        {
            _pkg = new TqPackageReader(Settings.ConquerDirectory);
            var mapLoader    = new MapFileLoader();
            var puzzleLoader = new PuzzleFileLoader(Settings.ConquerDirectory);
            MapRepo          = new GameMapRepository(Settings.GameMapFilePath);
            _mapLoadingService = new MapLoadingService(_pkg, mapLoader, puzzleLoader);
            // MapViewerService requires GraphicsDevice — deferred to LoadContent
        }

        // ── DrawableGameComponents ────────────────────────────────────────────
        Components.Add(new MapRenderComponent(this));
        Components.Add(new PlayerComponent(this));
        Components.Add(new HudComponent(this));

        base.Initialize();
    }

    protected override void LoadContent()
    {
        SpriteBatch = new SpriteBatch(GraphicsDevice);

        if (!Settings.IsValid() || _pkg == null || _mapLoadingService == null)
        {
            // No valid Conquer directory — show a setup prompt in the window title
            Window.Title = "ConquerMono — Please set ConquerDirectory in settings.json";
            base.LoadContent();
            return;
        }

        var ani         = new AniDictionary(Settings.ConquerDirectory);
        var sceneLoader = new SceneFileLoader();

        MapViewer = new MapViewerService(
            _mapLoadingService, ani, _pkg, sceneLoader, GraphicsDevice);

        // Default player position — will be overridden once a map is loaded
        Player = new PlayerEntity(Vector2.Zero);

        // Load the default (or last-used) map
        TryLoadDefaultMap();

        base.LoadContent();
    }

    // ── Update ────────────────────────────────────────────────────────────────
    protected override void Update(GameTime gt)
    {
        Input.Update();
        if (Input.IsPressed(Keys.Escape)) Exit();

        base.Update(gt); // propagates to all DrawableGameComponents
    }

    // ── Draw ──────────────────────────────────────────────────────────────────
    protected override void Draw(GameTime gt)
    {
        GraphicsDevice.Clear(new Color(12, 10, 22));
        base.Draw(gt);
    }

    // ── Map loading helper ────────────────────────────────────────────────────

    /// <summary>
    /// Loads a map by its <see cref="GameMap"/> entry and wires it to the player / camera.
    /// Can be called at any time (e.g. from a future map-select menu).
    /// </summary>
    public void LoadMap(GameMap map)
    {
        if (MapViewer == null) return;
        try
        {
            MapViewer.LoadMap(map.Path, map.TileSize);
            CurrentMapData = MapViewer.CurrentMapData;

            // Place the player roughly in the centre of the map
            if (CurrentMapData != null)
            {
                var spawnCell = FindSpawnCell(CurrentMapData);
                Player = new PlayerEntity(spawnCell);
                Player.AttachMap(CurrentMapData);
            }

            Settings.LastMapPath = map.Path;
            Settings.Save();
            Window.Title = $"ConquerMono  ·  {map.DisplayName}";
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ConquerGame] LoadMap error: {ex.Message}");
            Window.Title = $"ConquerMono  ·  Load failed: {ex.Message}";
        }
    }

    // ── Private ───────────────────────────────────────────────────────────────

    private void TryLoadDefaultMap()
    {
        if (MapRepo == null) return;

        // Try last-used map first
        GameMap? map = null;
        if (!string.IsNullOrEmpty(Settings.LastMapPath))
            map = MapRepo.GetAllMaps().Values.FirstOrDefault(m => m.Path == Settings.LastMapPath);

        // Fall back to default ID
        map ??= MapRepo.GetById(Settings.DefaultMapId);

        // Fall back to first available map
        map ??= MapRepo.GetAllMaps().Values.FirstOrDefault();

        if (map != null) LoadMap(map);
    }

    /// <summary>
    /// Searches outward from the map centre in a spiral until a walkable cell is found.
    /// Returns the centre if nothing walkable is found within radius 20.
    /// </summary>
    private static Vector2 FindSpawnCell(MapData map)
    {
        var centre = new Vector2(map.Bounds.Width / 2f, map.Bounds.Height / 2f);
        int cx = (int)centre.X, cy = (int)centre.Y;

        for (int r = 0; r <= 20; r++)
        for (int dx = -r; dx <= r; dx++)
        for (int dy = -r; dy <= r; dy++)
        {
            // Only test the perimeter of each radius ring
            if (Math.Abs(dx) != r && Math.Abs(dy) != r) continue;
            int tx = cx + dx, ty = cy + dy;
            if (tx >= 0 && tx < map.Cells.CollectionSize.Width &&
                ty >= 0 && ty < map.Cells.CollectionSize.Height &&
                map.Cells[tx, ty].IsWalkable)
                return new Vector2(tx, ty);
        }
        return centre;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            MapViewer?.Dispose();
            _pkg?.Dispose();
            SpriteBatch?.Dispose();
        }
        base.Dispose(disposing);
    }
}
