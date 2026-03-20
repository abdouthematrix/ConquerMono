using Microsoft.Xna.Framework;

namespace ConquerMono.World;

/// <summary>
/// Holds player state (position, stats) and applies movement each frame.
/// Does NOT reference any MonoGame rendering types — pure game logic.
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

    // ── Movement ──────────────────────────────────────────────────────────────
    public Vector2 TilePosition { get; private set; }
    public Vector2 FacingDir    { get; private set; } = new(1, 1);
    public bool    IsMoving     { get; private set; }

    private const float Speed = 5f;   // tiles per second

    // ── Foot-bob animation state ───────────────────────────────────────────────
    public float WalkPhase { get; private set; }

    // ─────────────────────────────────────────────────────────────────────────
    public PlayerEntity(Vector2 startTile)
    {
        TilePosition = startTile;
    }

    public void Update(Vector2 inputDir, float dt, GameMap map)
    {
        IsMoving = inputDir != Vector2.Zero;

        if (!IsMoving) return;

        if (inputDir.LengthSquared() > 1f)
            inputDir = Vector2.Normalize(inputDir);

        FacingDir  = inputDir;
        WalkPhase += dt * MathF.Tau * 2f;   // two full cycles per second

        Vector2 next = TilePosition + inputDir * Speed * dt;

        // Try full movement; if blocked, slide along each axis separately
        int nx = (int)MathF.Round(next.X);
        int ny = (int)MathF.Round(next.Y);

        if (map.IsWalkable(nx, ny))
        {
            TilePosition = next;
        }
        else
        {
            // Slide X
            Vector2 slideX = new(next.X, TilePosition.Y);
            if (map.IsWalkable((int)MathF.Round(slideX.X), (int)MathF.Round(slideX.Y)))
                TilePosition = slideX;
            else
            {
                // Slide Z
                Vector2 slideY = new(TilePosition.X, next.Y);
                if (map.IsWalkable((int)MathF.Round(slideY.X), (int)MathF.Round(slideY.Y)))
                    TilePosition = slideY;
            }
        }

        // Clamp to map
        TilePosition = new Vector2(
            Math.Clamp(TilePosition.X, 0.5f, map.Width  - 1.5f),
            Math.Clamp(TilePosition.Y, 0.5f, map.Height - 1.5f));
    }
}
