using System;
using Microsoft.Xna.Framework;

namespace ConquerMono.World;

// ── Movement state ────────────────────────────────────────────────────────────

public enum MovementState { Idle, Walking, Running, Jumping }

// ── Conquer Discrete 8-Way Directions ──────────────────────────────────────────

public enum ConquerAngle : byte
{
    SouthWest = 0,
    West = 1,
    NorthWest = 2,
    North = 3,
    NorthEast = 4,
    East = 5,
    SouthEast = 6,
    South = 7
}

// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Player state and movement in cell space conforming to original Conquer Online 8-way rules.
/// </summary>
public sealed class PlayerEntity : IRoleAppearance
{
    public uint Look { get; set; } = 1; // Small Female
    public uint ArmorId { get; set; } = 181350; // DarkWizard
    public uint ArmetId { get; set; } = 0; // Hair
    public uint RWeaponId { get; set; } = 601439; // HanzoKatana
    public uint LWeaponId { get; set; } = 601439; // HanzoKatana
    public uint MountId { get; set; } = 8010000; // Horse

    // ── Conquer Grid Direction Arrays ─────────────────────────────────────────
    public static readonly sbyte[] XDir = new sbyte[] { 0, -1, -1, -1, 0, 1, 1, 1 };
    public static readonly sbyte[] YDir = new sbyte[] { 1, 1, 0, -1, -1, -1, 0, 1 };

    // ── Stats ─────────────────────────────────────────────────────────────────
    public string Name { get; } = "Hero";
    public int Level { get; } = 15;
    public int MaxHealth { get; } = 1000;
    public int Health { get; private set; } = 820;
    public int MaxMana { get; } = 500;
    public int Mana { get; private set; } = 340;

    // ── Position & orientation ────────────────────────────────────────────────
    public Vector2 CellPosition { get; private set; }

    /// <summary>
    /// Discrete Conquer Online grid direction (The absolute source of truth).
    /// </summary>
    public ConquerAngle FacingDir { get; set; } = ConquerAngle.South;

    /// <summary>
    /// Calculated 3D radian value. Initialized dynamically from FacingDir.
    /// </summary>
    public float FacingAngle { get; private set; }

    public float WalkPhase { get; private set; }

    // ── State ─────────────────────────────────────────────────────────────────
    public MovementState State { get; private set; } = MovementState.Idle;
    public bool IsMoving => State is MovementState.Walking or MovementState.Running;
    public bool IsJumping => State == MovementState.Jumping;

    // ── Jump state ────────────────────────────────────────────────────────────
    private float _jumpTimer;
    private float _jumpDuration;
    private Vector2 _jumpStart;
    private Vector2 _jumpEnd;
    private Vector2 _jumpPeak;
    private float _jumpPeakAlt;
    private float _jumpStartAlt;
    private float _jumpEndAlt;

    public float JumpHeight { get; private set; }

    // ── Jump constants (matching Role.cpp) ────────────────────────────────────
    private const float DEFAULT_JUMP_HEIGHT = 120f / 32f;   // ≈ 3.75 cells
    private const float BASE_JUMP_DURATION = 0.6f;
    private const float SCALE_DIST = 300f / 32f;            // ≈ 9.375 cells

    // ── Constants ─────────────────────────────────────────────────────────────
    private const float ARRIVE_RADIUS = 0.15f;
    private const float ROTATE_SPEED = 18f; // Slightly increased for snappy alignment
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

    public void Jump(float baseDuration = BASE_JUMP_DURATION, Vector2? overrideTarget = null)
    {
        if (State == MovementState.Jumping) return;

        _jumpStart = CellPosition;
        _jumpEnd = overrideTarget ?? (_target ?? CellPosition);

        float dist = Vector2.Distance(_jumpStart, _jumpEnd);

        _jumpDuration = MathF.Max(baseDuration, baseDuration + baseDuration * dist / SCALE_DIST);

        _jumpStartAlt = 0f;
        _jumpEndAlt = 0f;
        _jumpPeakAlt = _jumpStartAlt
                      + MathF.Max(DEFAULT_JUMP_HEIGHT, MathF.Sqrt(dist * 32f) / 3f / 32f)
                      + 20f / 32f;

        _jumpPeak = (_jumpStart + _jumpEnd) * 0.5f;

        State = MovementState.Jumping;
        _jumpTimer = 0f;
        JumpHeight = 0f;
        WalkPhase = 0f;
    }

    /// <summary>
    /// Instantly forces the entity to face a specific ConquerAngle, bypassing smooth rotation.
    /// </summary>
    public void FaceInstantly(ConquerAngle newDirection)
    {
        FacingDir = newDirection;
        FacingAngle = GetFacingAngleFromConquerAngle(newDirection);
    }

    // ── Update ────────────────────────────────────────────────────────────────

    public void Update(Vector2 keyboardDir, bool running, float dt,
                       float walkSpeed = DEFAULT_WALK, float runSpeed = DEFAULT_RUN)
    {
        _walkSpeed = walkSpeed;
        _runSpeed = runSpeed;

        // ── 1. Global Angle Synchronization ───────────────────────────────────
        // The discrete logical FacingDir (ConquerAngle) is the absolute source of truth.
        // We always smoothly rotate the visual 3D model to match it, even when standing still.
        float desiredAngle = GetFacingAngleFromConquerAngle(FacingDir);
        FacingAngle = SmoothAngle(FacingAngle, desiredAngle, ROTATE_SPEED * dt);

        // ── 2. Jumping ────────────────────────────────────────────────────────
        if (State == MovementState.Jumping)
        {
            _jumpTimer += dt;
            WalkPhase = JumpProgress * MathF.PI;
            JumpHeight = ComputeJumpHeight();

            // End of jump
            if (_jumpTimer >= _jumpDuration)
            {
                State = MovementState.Idle;
                JumpHeight = 0f;
                WalkPhase = 0f;
            }

            // Determine mid-air drift direction
            Vector2 airDir = Vector2.Zero;
            if (keyboardDir != Vector2.Zero)
            {
                airDir = Vector2.Normalize(keyboardDir);
            }
            else if (_target.HasValue)
            {
                Vector2 toTarget = _target.Value - CellPosition;
                float dist = toTarget.Length();
                if (dist <= ARRIVE_RADIUS) _target = null;
                else airDir = toTarget / dist;
            }

            // Apply mid-air drift
            if (airDir != Vector2.Zero)
            {
                // Update the logical direction while mid-air
                FacingDir = GetConquerAngleFromVector(airDir);

                // Snap movement vector to discrete Conquer grid vectors
                Vector2 discreteMove = new Vector2(XDir[(int)FacingDir], YDir[(int)FacingDir]);
                if (discreteMove != Vector2.Zero) discreteMove.Normalize();

                TryMove(discreteMove * _walkSpeed * dt);
            }
            return; // Skip walking logic while airborne
        }

        // ── 3. Walk / Run ─────────────────────────────────────────────────────
        Vector2 moveDir = Vector2.Zero;

        // Determine intended movement direction (Keyboard overrides Mouse)
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
                running = false;
            }
            else
            {
                moveDir = toTarget / dist;
                running = _targetIsRun;
            }
        }

        // Apply ground movement
        if (moveDir != Vector2.Zero)
        {
            State = running ? MovementState.Running : MovementState.Walking;

            // Set the logical discrete direction. The global sync (Phase 1) handles the 3D rotation.
            FacingDir = GetConquerAngleFromVector(moveDir);

            // Advance walk/run animation phase
            WalkPhase = (WalkPhase + dt * MathF.Tau * 2.2f) % MathF.Tau;

            // Enforce discrete structural movement matching the 8-way step arrays
            Vector2 discreteMove = new Vector2(XDir[(int)FacingDir], YDir[(int)FacingDir]);
            if (discreteMove != Vector2.Zero) discreteMove.Normalize();

            TryMove(discreteMove * (running ? _runSpeed : _walkSpeed) * dt);
        }
        else
        {
            State = MovementState.Idle;
        }
    }

    private float ComputeJumpHeight()
    {
        float distFromStart = Vector2.Distance(CellPosition, _jumpStart);
        float distFromEnd = Vector2.Distance(CellPosition, _jumpEnd);
        float distToPeak = Vector2.Distance(_jumpStart, _jumpPeak);
        float distFromPeak = Vector2.Distance(_jumpPeak, _jumpEnd);

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

    // ── Conquer Coordinate Transformation Helpers ───────────────────────────

    /// <summary>
    /// Calculates the 8-way discrete Conquer direction between two coordinates.
    /// </summary>
    public static int ConquerDirection(int x1, int y1, int x2, int y2)
    {
        double angle = Math.Atan2(y2 - y1, x2 - x1);
        angle -= Math.PI / 2;

        if (angle < 0) angle += 2 * Math.PI;

        angle *= 8 / (2 * Math.PI);
        return (int)angle % 8; // % 8 bounds guard handles rare exact 2*PI rounding cases safely
    }

    /// <summary>
    /// Maps continuous input directional vectors directly to discrete 8-way Conquer indices.
    /// </summary>
    private ConquerAngle GetConquerAngleFromVector(Vector2 dir)
    {
        if (dir == Vector2.Zero) return FacingDir;

        double angle = Math.Atan2(dir.Y, dir.X);
        angle -= Math.PI / 2;

        if (angle < 0) angle += 2 * Math.PI;

        angle *= 8 / (2 * Math.PI);
        return (ConquerAngle)((int)angle % 8);
    }

    /// <summary>
    /// Translates a discrete Conquer direction index into exact 3D world space radians.
    /// </summary>
    public static float GetFacingAngleFromConquerAngle(ConquerAngle dir)
    {
        return ((int)dir + 2) % 8 * (MathF.PI / 4f);
    }

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