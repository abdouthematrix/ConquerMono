namespace ConquerMono.World;

// ── Movement state ────────────────────────────────────────────────────────────

public enum MovementState { Idle, Walking, Running, Jumping }

// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Player state and movement in cell space.
///
/// Jump arc
/// ────────
/// Ported from Role.cpp :: GetJumpHeight() and GetJumpFrameInterval().
///
/// Peak altitude (in cell units):
///   peak = max(DEFAULT_JUMP_HEIGHT, distance / 3) + 0.2
///
/// Vertical arc (two-phase sine, matching original):
///   Ascending  (current → peak):  sin(π × distToNow  / distToHighest / 2)
///   Descending (peak   → end):    sin(π × distToNow  / distToEnd     / 2)
///
/// Jump duration scales with target distance (Role.cpp :: GetJumpFrameInterval):
///   duration = max(BASE_JUMP_DURATION, BASE_JUMP_DURATION × distance / SCALE_DIST)
///
/// JumpHeight is exposed so PlayerComponent can apply it as a Y bob offset.
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
    public float FacingAngle { get; private set; } = MathF.PI / 4f;
    public Vector2 FacingDir => new(MathF.Cos(FacingAngle), MathF.Sin(FacingAngle));
    public float WalkPhase { get; private set; }

    // ── State ─────────────────────────────────────────────────────────────────
    public MovementState State { get; private set; } = MovementState.Idle;
    public bool IsMoving => State is MovementState.Walking or MovementState.Running;
    public bool IsJumping => State == MovementState.Jumping;

    // ── Jump state ────────────────────────────────────────────────────────────
    private float _jumpTimer;
    private float _jumpDuration;
    private Vector2 _jumpStart;     // cell position when jump began
    private Vector2 _jumpEnd;       // cell target (may equal start for Space-jump)
    private Vector2 _jumpPeak;      // midpoint in cell space
    private float _jumpPeakAlt;   // peak altitude in cell units
    private float _jumpStartAlt;  // ground altitude at start (0 in flat maps)
    private float _jumpEndAlt;    // ground altitude at end   (0 in flat maps)

    /// <summary>
    /// Current vertical offset in cell units (positive = up).
    /// Computed each frame from the Role.cpp two-phase sine formula.
    /// PlayerComponent converts this to world-space bob via PlayerModelScale.
    /// </summary>
    public float JumpHeight { get; private set; }

    // ── Jump constants (matching Role.cpp) ────────────────────────────────────
    // _DEFAULT_JUMP_HEIGH = 120 world units.
    // CO world units ≈ cell × 32 pixels; we work in cell space (÷ 32).
    private const float DEFAULT_JUMP_HEIGHT = 120f / 32f;   // ≈ 3.75 cells
    private const float BASE_JUMP_DURATION = 0.6f;         // seconds at distance 0
    // Role.cpp: dwInterval × dbDicAll / 300 + 1  (dbDicAll in world-px, ÷ 32 → cells × 32 / 300)
    // Simplified: duration = BASE + BASE × cellDistance / SCALE_CELLS
    private const float SCALE_DIST = 300f / 32f;            // ≈ 9.375 cells

    // ── Constants ─────────────────────────────────────────────────────────────
    private const float ARRIVE_RADIUS = 0.15f;
    private const float ROTATE_SPEED = 12f;
    private const float DEFAULT_WALK = 5f;
    private const float DEFAULT_RUN = 10f;

    // ── Runtime speeds ────────────────────────────────────────────────────────
    private float _walkSpeed = DEFAULT_WALK;
    private float _runSpeed = DEFAULT_RUN;

    // ── Click-to-move ─────────────────────────────────────────────────────────
    private Vector2? _target;
    private bool _targetIsRun;
    public bool HasTarget => _target.HasValue;
    public Vector2? Target => _target;

    // ── Map reference ─────────────────────────────────────────────────────────
    private MapData? _map;

    // ── Legacy progress (kept for PlayerComponent compatibility) ──────────────
    public float JumpProgress => _jumpDuration > 0 ? _jumpTimer / _jumpDuration : 0f;

    // ─────────────────────────────────────────────────────────────────────────
    public PlayerEntity(Vector2 startCell) => CellPosition = startCell;

    public void AttachMap(MapData map) => _map = map;

    // ── API ───────────────────────────────────────────────────────────────────

    public void SetTarget(Vector2 cellTarget, bool run = false)
    {
        _target = FindNearestWalkable(cellTarget) ?? cellTarget;
        _targetIsRun = run;
    }

    public void ClearTarget() => _target = null;

    /// <summary>
    /// Trigger a jump toward <paramref name="overrideTarget"/> (or in-place if null).
    /// Duration and arc height are computed from the distance, matching Role.cpp.
    /// <paramref name="baseDuration"/> is the clip's natural length at distance 0.
    /// </summary>
    public void Jump(float baseDuration = BASE_JUMP_DURATION, Vector2? overrideTarget = null)
    {
        if (State == MovementState.Jumping) return;

        _jumpStart = CellPosition;
        _jumpEnd = overrideTarget ?? (_target ?? CellPosition);

        float dist = Vector2.Distance(_jumpStart, _jumpEnd);

        // Scale duration with distance (Role.cpp :: GetJumpFrameInterval)
        //   dwInterval × dbDicAll / 300 + 1  →  baseDuration × dist / SCALE_DIST
        _jumpDuration = MathF.Max(baseDuration, baseDuration + baseDuration * dist / SCALE_DIST);

        // Peak altitude scales with distance  (Role.cpp :: GetJumpHeight)
        //   nHightestAltitude = nBeginAlt + sqrt(distWorldPx) / 3 + 20
        // In cell units: peak = startAlt + sqrt(dist×32) / 3 / 32 + 20/32
        _jumpStartAlt = 0f;  // flat map; terrain height not tracked in cell space
        _jumpEndAlt = 0f;
        _jumpPeakAlt = _jumpStartAlt
                      + MathF.Max(DEFAULT_JUMP_HEIGHT,
                                  MathF.Sqrt(dist * 32f) / 3f / 32f)
                      + 20f / 32f;

        // Peak is at the midpoint of the path (Role.cpp sets it to (begin+end)/2)
        _jumpPeak = (_jumpStart + _jumpEnd) * 0.5f;

        State = MovementState.Jumping;
        _jumpTimer = 0f;
        JumpHeight = 0f;
        WalkPhase = 0f;
        // Keep _target so movement continues mid-air
    }

    // ── Update ────────────────────────────────────────────────────────────────

    public void Update(Vector2 keyboardDir, bool running, float dt,
                       float walkSpeed = DEFAULT_WALK, float runSpeed = DEFAULT_RUN)
    {
        _walkSpeed = walkSpeed;
        _runSpeed = runSpeed;

        // ── Jumping ───────────────────────────────────────────────────────────
        if (State == MovementState.Jumping)
        {
            _jumpTimer += dt;
            WalkPhase = JumpProgress * MathF.PI;

            // Compute vertical height using Role.cpp two-phase sine
            JumpHeight = ComputeJumpHeight();

            if (_jumpTimer >= _jumpDuration)
            {
                State = MovementState.Idle;
                JumpHeight = 0f;
                WalkPhase = 0f;
            }

            // Move during air phase
            Vector2 airDir = Vector2.Zero;
            if (keyboardDir != Vector2.Zero)
                airDir = Vector2.Normalize(keyboardDir);
            else if (_target.HasValue)
            {
                Vector2 toTarget = _target.Value - CellPosition;
                float dist = toTarget.Length();
                if (dist <= ARRIVE_RADIUS) _target = null;
                else airDir = toTarget / dist;
            }

            if (airDir != Vector2.Zero)
            {
                float desired = MathF.Atan2(airDir.Y, airDir.X);
                FacingAngle = SmoothAngle(FacingAngle, desired, ROTATE_SPEED * dt);
                TryMove(airDir * _walkSpeed * dt);
            }
            return;
        }

        // ── Walk / Run ────────────────────────────────────────────────────────
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
            if (dist <= ARRIVE_RADIUS) { _target = null; running = false; }
            else { moveDir = toTarget / dist; running = _targetIsRun; }
        }

        if (moveDir != Vector2.Zero)
        {
            State = running ? MovementState.Running : MovementState.Walking;
            float desired = MathF.Atan2(moveDir.Y, moveDir.X);
            FacingAngle = SmoothAngle(FacingAngle, desired, ROTATE_SPEED * dt);
            WalkPhase = (WalkPhase + dt * MathF.Tau * 2.2f) % MathF.Tau;
            TryMove(moveDir * (running ? _runSpeed : _walkSpeed) * dt);
        }
        else
        {
            State = MovementState.Idle;
        }
    }

    // ── Jump arc (Role.cpp :: GetJumpHeight) ──────────────────────────────────

    /// <summary>
    /// Returns the current vertical offset in cell units using the two-phase
    /// sine formula from Role.cpp.
    ///
    /// Ascending  half (begin → peak):
    ///   height = startAlt + (peakAlt - startAlt) × sin(π × distNow / distToPeak / 2)
    ///
    /// Descending half (peak → end):
    ///   height = endAlt   + (peakAlt - endAlt)   × sin(π × distNow / distToEnd  / 2)
    /// </summary>
    private float ComputeJumpHeight()
    {
        // World distances from current position
        float distFromStart = Vector2.Distance(CellPosition, _jumpStart);
        float distFromEnd = Vector2.Distance(CellPosition, _jumpEnd);
        float distToPeak = Vector2.Distance(_jumpStart, _jumpPeak);
        float distFromPeak = Vector2.Distance(_jumpPeak, _jumpEnd);

        // Ascending if we are closer to start than to peak
        bool ascending = Vector2.DistanceSquared(CellPosition, _jumpStart)
                       < Vector2.DistanceSquared(CellPosition, _jumpPeak);

        if (ascending)
        {
            float heightDef = _jumpPeakAlt - _jumpStartAlt;
            float denom = MathF.Max(0.001f, distToPeak);
            double angle = Math.PI * distFromStart / denom / 2.0;
            return _jumpStartAlt + heightDef * (float)Math.Sin(angle);
        }
        else
        {
            float heightDef = _jumpPeakAlt - _jumpEndAlt;
            float denom = MathF.Max(0.001f, distFromPeak);
            double angle = Math.PI * distFromEnd / denom / 2.0;
            return _jumpEndAlt + heightDef * (float)Math.Sin(angle);
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

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