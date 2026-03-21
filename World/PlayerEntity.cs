namespace ConquerMono.World;

/// <summary>
/// Holds player state (position in cell space, stats) and applies WASD movement.
/// Works in the same cell coordinate space as <see cref="IsometricCoordinateSystem"/>:
///   increasing X = north-east, increasing Y = south-east.
/// </summary>
public sealed class PlayerEntity
{
    // ── Stats ─────────────────────────────────────────────────────────────────
    public string Name      { get; } = "Hero";
    public int    Level     { get; } = 15;
    public int    MaxHealth { get; } = 1000;
    public int    Health    { get; private set; } = 820;
    public int    MaxMana   { get; } = 500;
    public int    Mana      { get; private set; } = 340;

    // ── Position (in map cell units) ──────────────────────────────────────────
    public Vector2 CellPosition { get; private set; }
    public Vector2 FacingDir    { get; private set; } = new(1, 0);
    public bool    IsMoving     { get; private set; }

    /// <summary>Phase counter for walk animation (advances while moving).</summary>
    public float WalkPhase { get; private set; }

    private const float SPEED = 5f; // cells per second

    // ── Map bounds ────────────────────────────────────────────────────────────
    private MapData? _map;

    // ─────────────────────────────────────────────────────────────────────────
    public PlayerEntity(Vector2 startCell) => CellPosition = startCell;

    /// <summary>Attach a new map so that walkability checks use the correct cell grid.</summary>
    public void AttachMap(MapData map) => _map = map;

    // ── Update ────────────────────────────────────────────────────────────────
    public void Update(Vector2 inputDir, float dt)
    {
        IsMoving = inputDir != Vector2.Zero;
        if (!IsMoving) return;

        if (inputDir.LengthSquared() > 1f)
            inputDir = Vector2.Normalize(inputDir);

        FacingDir  = inputDir;
        WalkPhase  = (WalkPhase + dt * MathF.Tau * 2.2f) % MathF.Tau;

        Vector2 next = CellPosition + inputDir * SPEED * dt;

        if (IsWalkable(next))
        {
            CellPosition = next;
            return;
        }

        // Axis sliding
        var slideX = new Vector2(next.X, CellPosition.Y);
        if (IsWalkable(slideX)) { CellPosition = slideX; return; }

        var slideY = new Vector2(CellPosition.X, next.Y);
        if (IsWalkable(slideY)) { CellPosition = slideY; }
    }

    // ── Walkability ───────────────────────────────────────────────────────────
    private bool IsWalkable(Vector2 cell)
    {
        if (_map == null) return true;
        int x = (int)MathF.Round(cell.X);
        int y = (int)MathF.Round(cell.Y);
        return _map.Cells.CollectionSize.Width  > x && x >= 0 &&
               _map.Cells.CollectionSize.Height > y && y >= 0 &&
               _map.Cells[x, y].IsWalkable;
    }
}
