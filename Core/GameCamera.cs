namespace ConquerMono.Core;


/// <summary>
/// Unified 2.5D camera for ConquerMono.
///
/// Puzzle-image pixels are the common coordinate system:
///   • <see cref="DrawWindow"/>   — the rectangle of puzzle-image pixels currently visible.
///   • <see cref="TransformMatrix"/> — scale matrix passed to every SpriteBatch Begin call.
///   • <see cref="ViewMatrix"/> /
///     <see cref="ProjectionMatrix"/> — orthographic 3-D matrices that place the 3-D
///                                     player mesh at the correct screen pixel using the
///                                     same coordinate origin as the 2-D tile pass.
///
/// The 3-D eye/projection is calibrated so that one world unit in X/Z maps to exactly
/// <see cref="PixelsPerUnit"/> pixels — the horizontal pixel stride across one isometric
/// cell diagonal.
/// </summary>
public sealed class GameCamera
{
    // ── Constants ─────────────────────────────────────────────────────────────
    public const float PixelsPerUnit = 45.25f;  // TileHalfW(32) × √2

    private const float DEFAULT_ZOOM  = 0.5f;
    private const float MAX_ZOOM      = 5.0f;

    // ── State ─────────────────────────────────────────────────────────────────
    private readonly GraphicsDevice _gd;
    private Puzzle?  _puzzle;

    private Vector2 _position = Vector2.Zero;
    private float   _zoom     = DEFAULT_ZOOM;

    // ── Public read-only properties ───────────────────────────────────────────
    public Rectangle DrawWindow      { get; private set; }
    public Matrix    TransformMatrix { get; private set; } = Matrix.Identity;
    public Matrix    ViewMatrix      { get; private set; } = Matrix.Identity;
    public Matrix    ProjectionMatrix{ get; private set; } = Matrix.Identity;

    public IsometricCoordinateSystem? CoordSystem { get; private set; }

    // ─────────────────────────────────────────────────────────────────────────
    public GameCamera(GraphicsDevice gd) => _gd = gd;

    // ── Setup ─────────────────────────────────────────────────────────────────

    /// <summary>Call once after a new map is loaded.</summary>
    public void Attach(Puzzle puzzle, IsometricCoordinateSystem coords)
    {
        _puzzle     = puzzle;
        CoordSystem = coords;
        _zoom       = DEFAULT_ZOOM;
        _position   = new Vector2(puzzle.Width / 4f, puzzle.Height / 4f);
        Recalculate();
    }

    // ── Properties with clamping ──────────────────────────────────────────────

    public Vector2 Position
    {
        get => _position;
        set { _position = ClampPos(value); Recalculate(); }
    }

    public float Zoom
    {
        get => _zoom;
        set { _zoom = Math.Clamp(value, MinZoom, MAX_ZOOM); Recalculate(); }
    }

    private float MinZoom
    {
        get
        {
            if (_puzzle == null) return 0.01f;
            var vp = _gd.Viewport;
            return Math.Max(vp.Width / (float)_puzzle.Width, vp.Height / (float)_puzzle.Height);
        }
    }

    // ── Public helpers ────────────────────────────────────────────────────────

    /// <summary>
    /// True while the user is actively panning (right-drag).
    /// <see cref="PlayerComponent"/> skips <see cref="Follow"/> when this is set
    /// so manual pan is never overwritten by the player-follow logic.
    /// </summary>
    public bool IsPanning { get; set; }

    public void PanByPixels(Vector2 delta) => Position -= delta / _zoom;

    public void ZoomAround(float factor, Vector2 screenPoint)
    {
        // Keep the world point under the mouse fixed
        var world = screenPoint / _zoom + _position;
        Zoom = _zoom * factor;
        Position = world - screenPoint / _zoom;
    }

    public void ResetView()
    {
        if (_puzzle == null) return;
        _zoom     = DEFAULT_ZOOM;
        _position = new Vector2(_puzzle.Width / 4f, _puzzle.Height / 4f);
        Recalculate();
    }

    public void FitToWindow()
    {
        if (_puzzle == null) return;
        var vp = _gd.Viewport;
        Zoom     = Math.Min(vp.Width / (float)_puzzle.Width,
                            vp.Height / (float)_puzzle.Height) * 0.9f;
        Position = Vector2.Zero;
    }

    /// <summary>Convert a puzzle-image cell coordinate to viewport pixels.</summary>
    public Vector2 CellToViewport(Vector2 cell)
    {
        if (CoordSystem == null) return Vector2.Zero;
        var screen = CoordSystem.MapToScreen(cell);
        return (screen - _position) * _zoom;
    }

    /// <summary>Convert viewport pixels to puzzle-image cell coordinates.</summary>
    public Vector2 ViewportToCell(Vector2 viewportPx)
    {
        if (CoordSystem == null) return Vector2.Zero;
        var screen = viewportPx / _zoom + _position;
        return CoordSystem.ScreenToMap(screen);
    }

    // ── Private ───────────────────────────────────────────────────────────────

    private void Recalculate()
    {
        if (_puzzle == null) return;

        var vp   = _gd.Viewport;

        // ── 2-D transform matrix ──────────────────────────────────────────────
        // Drawing components subtract DrawWindow.X/Y themselves when converting
        // MapToScreen() puzzle-pixel coords to viewport-local coords.
        // The SpriteBatch transform therefore only needs to apply zoom scale.
        TransformMatrix = Matrix.CreateScale(_zoom);

        // ── Draw window (puzzle-image pixels) ─────────────────────────────────
        float ww = Math.Min(vp.Width  / _zoom, _puzzle.Width);
        float wh = Math.Min(vp.Height / _zoom, _puzzle.Height);
        _position = ClampPos(_position);

        DrawWindow = new Rectangle(
            (int)_position.X, (int)_position.Y,
            (int)ww,          (int)wh);

        // ── 3-D matrices (must place mesh at puzzle-image pixel screenPos) ────
        // Projection is computed once per zoom change; View is recomputed each
        // frame by TrackCell() once the player position is known.
        const float D = 20f;
        float H = D * MathF.Sqrt(2f / 3f);
        _viewEyeOffset  = new Vector3(D, H, D);

        float orthoW = ww / PixelsPerUnit;
        float orthoH = wh / PixelsPerUnit;
        ProjectionMatrix = Matrix.CreateOrthographic(orthoW, orthoH, -500f, 500f);

        // Default View at origin until TrackCell is called
        ViewMatrix = Matrix.CreateLookAt(_viewEyeOffset, Vector3.Zero, Vector3.Up);
    }

    // ── Eye offset cached for TrackCell ──────────────────────────────────────
    private Vector3 _viewEyeOffset = new(20f, 20f * 0.8165f, 20f);

    /// <summary>
    /// Update the 3-D View matrix so the camera looks at <paramref name="cellPos"/>.
    /// Call this every frame from <see cref="Components.PlayerComponent"/> after
    /// moving the player, before calling Draw.
    /// </summary>
    /// <summary>
    /// Centre the camera on <paramref name="puzzlePixel"/> (a puzzle-image coordinate).
    /// Does nothing when the whole map already fits on screen — avoids fighting
    /// <see cref="ClampPos"/> every frame at extreme zoom-out levels.
    /// </summary>
    public void Follow(Vector2 puzzlePixel)
    {
        if (_puzzle == null) return;
        var vp = _gd.Viewport;

        float maxX = Math.Max(0, _puzzle.Width  - vp.Width  / _zoom);
        float maxY = Math.Max(0, _puzzle.Height - vp.Height / _zoom);

        // Map fits entirely on screen — nothing to scroll; keep position at 0,0.
        if (maxX <= 0 && maxY <= 0) return;

        Position = new Vector2(
            puzzlePixel.X - vp.Width  / _zoom / 2f,
            puzzlePixel.Y - vp.Height / _zoom / 2f);
    }

    public void TrackCell(Vector2 cellPos)
    {
        var target = new Vector3(cellPos.X, 0f, cellPos.Y);
        ViewMatrix = Matrix.CreateLookAt(target + _viewEyeOffset, target, Vector3.Up);
    }

    private Vector2 ClampPos(Vector2 v)
    {
        if (_puzzle == null) return v;
        var vp = _gd.Viewport;
        float maxX = Math.Max(0, _puzzle.Width  - vp.Width  / _zoom);
        float maxY = Math.Max(0, _puzzle.Height - vp.Height / _zoom);
        return new Vector2(Math.Clamp(v.X, 0, maxX), Math.Clamp(v.Y, 0, maxY));
    }
}
