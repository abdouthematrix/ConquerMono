namespace ConquerMono.World;

/// <summary>
/// Player state and movement in cell space.
///
/// Movement modes (both can coexist):
///   • Left-click     — sets a click-to-move target; player walks toward it,
///                      rotating to face the direction of travel.
///   • WASD / Arrows  — direct input; overrides and cancels click-to-move.
///
/// Rotation is smoothed: <see cref="FacingAngle"/> interpolates toward the
/// desired direction each frame, giving the 3-D mesh a natural turn.
/// </summary>
public sealed class PlayerEntity
{
    // ── Stats ─────────────────────────────────────────────────────────────────
    public string Name { get; } = "Hero";
    public int Level { get; } = 15;
    public int MaxHealth { get; } = 1000;
    public int Health { get; private set; } = 820;
    public int MaxMana { get; } = 500;
    public int Mana { get; private set; } = 340;

    // ── Position & orientation ────────────────────────────────────────────────
    public Vector2 CellPosition { get; private set; }
    public bool IsMoving { get; private set; }

    /// <summary>Current facing angle in radians (0 = +X cell axis).</summary>
    public float FacingAngle { get; private set; }

    /// <summary>Unit vector derived from FacingAngle for 3-D mesh rotation.</summary>
    public Vector2 FacingDir => new(MathF.Cos(FacingAngle), MathF.Sin(FacingAngle));

    /// <summary>Walk-bob phase (mod 2π).</summary>
    public float WalkPhase { get; private set; }

    // ── Constants ─────────────────────────────────────────────────────────────
    private const float SPEED = 5f;    // cells/sec
    private const float ARRIVE_RADIUS = 0.15f; // cells — snap threshold
    private const float ROTATE_SPEED = 12f;   // rad/sec smooth turn

    // ── Click-to-move ─────────────────────────────────────────────────────────
    private Vector2? _target;
    public bool HasTarget => _target.HasValue;
    public Vector2? Target => _target;

    // ── Map ───────────────────────────────────────────────────────────────────
    private MapData? _map;

    // ─────────────────────────────────────────────────────────────────────────
    public PlayerEntity(Vector2 startCell)
    {
        CellPosition = startCell;
        FacingAngle = MathF.PI / 4f;
    }

    public void AttachMap(MapData map) => _map = map;

    // ── API ───────────────────────────────────────────────────────────────────

    /// <summary>Set a click-to-move destination in cell space.</summary>
    public void SetTarget(Vector2 cellTarget)
    {
        _target = FindNearestWalkable(cellTarget) ?? cellTarget;
    }

    public void ClearTarget() => _target = null;

    // ── Update ────────────────────────────────────────────────────────────────

    public void Update(Vector2 keyboardDir, float dt)
    {
        Vector2 moveDir = Vector2.Zero;

        if (keyboardDir != Vector2.Zero)
        {
            _target = null;
            moveDir = Vector2.Normalize(keyboardDir);
        }
        else if (_target.HasValue)
        {
            Vector2 toTarget = _target.Value - CellPosition;
            float dist = toTarget.Length();
            if (dist <= ARRIVE_RADIUS)
            {
                _target = null;
            }
            else
            {
                moveDir = toTarget / dist;
            }
        }

        IsMoving = moveDir != Vector2.Zero;

        if (IsMoving)
        {
            float desired = MathF.Atan2(moveDir.Y, moveDir.X);
            FacingAngle = SmoothAngle(FacingAngle, desired, ROTATE_SPEED * dt);
            WalkPhase = (WalkPhase + dt * MathF.Tau * 2.2f) % MathF.Tau;
            TryMove(moveDir * SPEED * dt);
        }
    }

    // ── Private ───────────────────────────────────────────────────────────────

    private void TryMove(Vector2 delta)
    {
        Vector2 next = CellPosition + delta;
        if (IsWalkable(next)) { CellPosition = next; return; }
        var sx = new Vector2(next.X, CellPosition.Y);
        if (IsWalkable(sx)) { CellPosition = sx; return; }
        var sy = new Vector2(CellPosition.X, next.Y);
        if (IsWalkable(sy)) { CellPosition = sy; }
    }

    private bool IsWalkable(Vector2 cell)
    {
        if (_map == null) return true;
        int x = (int)MathF.Round(cell.X);
        int y = (int)MathF.Round(cell.Y);
        return x >= 0 && x < _map.Cells.CollectionSize.Width &&
               y >= 0 && y < _map.Cells.CollectionSize.Height &&
               _map.Cells[x, y].IsWalkable;
    }

    private Vector2? FindNearestWalkable(Vector2 cell)
    {
        int cx = (int)MathF.Round(cell.X);
        int cy = (int)MathF.Round(cell.Y);
        for (int r = 0; r <= 3; r++)
            for (int dx = -r; dx <= r; dx++)
                for (int dy = -r; dy <= r; dy++)
                {
                    if (Math.Abs(dx) != r && Math.Abs(dy) != r) continue;
                    var c = new Vector2(cx + dx, cy + dy);
                    if (IsWalkable(c)) return c;
                }
        return null;
    }

    private static float SmoothAngle(float current, float target, float maxDelta)
    {
        float diff = target - current;
        while (diff > MathF.PI) diff -= MathF.Tau;
        while (diff < -MathF.PI) diff += MathF.Tau;
        return current + Math.Clamp(diff, -maxDelta, maxDelta);
    }
}