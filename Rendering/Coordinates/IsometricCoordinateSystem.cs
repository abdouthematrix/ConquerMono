namespace ConquerMono.Rendering.Coordinates;

/// <summary>
/// Converts between cell (map) space and pixel (puzzle-image) space.
///
/// All drawing components and the game camera use puzzle-image pixels as their
/// common 2-D coordinate system; the 3-D player renderer ultimately works in
/// the same system through the <see cref="Services.GameCamera"/> matrices.
/// </summary>
public sealed class IsometricCoordinateSystem
{
    private readonly Puzzle  _puzzle;
    private readonly MapData _map;

    /// <summary>Four corners of one cell diamond in local pixel space.</summary>
    public Vector2[] CellPoints { get; }

    public IsometricCoordinateSystem(Puzzle puzzle, MapData map)
    {
        _puzzle = puzzle;
        _map    = map;

        int cw = map.Cells.CellWidth;
        int cd = map.Cells.CellDepth;

        CellPoints = new[]
        {
            new Vector2(1,       cd / 2),
            new Vector2(cw / 2,  1),
            new Vector2(cw - 1,  cd / 2),
            new Vector2(cw / 2,  cd - 1)
        };
    }

    // ── Map → Screen ──────────────────────────────────────────────────────────

    /// <summary>Cell coordinates → puzzle-image pixel position.</summary>
    public Vector2 MapToScreen(Vector2 cell) => new(
        (cell.X - cell.Y) * 32f + _puzzle.Width  / 2f,
        (cell.X + cell.Y - (_map.Bounds.Height - 1)) * 16f + _puzzle.Height / 2f);

    public Vector2 MapToScreen(float cellX, float cellY) =>
        MapToScreen(new Vector2(cellX, cellY));

    // ── Screen → Map ──────────────────────────────────────────────────────────

    /// <summary>
    /// Puzzle-image pixel → cell coordinates (fractional).
    /// Exact inverse of <see cref="MapToScreen(Vector2)"/>:
    ///   px = (x-y)*32 + PW/2   →  x-y = (px - PW/2) / 32
    ///   py = (x+y-(H-1))*16 + PH/2  →  x+y = (py - PH/2) / 16 + (H-1)
    /// </summary>
    public Vector2 ScreenToMap(Vector2 screen)
    {
        float pw = _puzzle.Width;
        float ph = _puzzle.Height;
        int   bh = _map.Bounds.Height;

        float diff = (screen.X - pw * 0.5f) / 32f;           // x - y
        float sum  = (screen.Y - ph * 0.5f) / 16f + (bh - 1); // x + y

        return new Vector2((sum + diff) * 0.5f, (sum - diff) * 0.5f);
    }

    public Vector2 ScreenToMap(Point screen) =>
        ScreenToMap(new Vector2(screen.X, screen.Y));
}
