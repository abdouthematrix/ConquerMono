namespace ConquerMono.World;

/// <summary>
/// Holds the tile grid and exposes walkability queries.
/// Terrain is generated procedurally using layered sine waves
/// (no external assets required).
/// </summary>
public sealed class GameMap
{
    public int Width  { get; }
    public int Height { get; }

    private readonly TileType[,] _tiles;

    public GameMap(int width, int height)
    {
        Width  = width;
        Height = height;
        _tiles = new TileType[width, height];
        Generate();
    }

    // ── Procedural generation ─────────────────────────────────────────────────
    private void Generate()
    {
        for (int y = 0; y < Height; y++)
        for (int x = 0; x < Width;  x++)
        {
            float n = HeightNoise(x * 0.12f, y * 0.12f);
            _tiles[x, y] = n switch
            {
                < -0.30f => TileType.Water,
                < -0.05f => TileType.Sand,
                <  0.35f => TileType.Grass,
                <  0.55f => TileType.Stone,
                _        => TileType.Stone,
            };
        }

        // Carve cross-shaped roads through the centre
        int cx = Width  / 2;
        int cy = Height / 2;
        for (int i = 0; i < Width;  i++) _tiles[i,  cy] = TileType.Road;
        for (int j = 0; j < Height; j++) _tiles[cx,  j] = TileType.Road;

        // Ensure spawn area is walkable grass
        for (int dy = -2; dy <= 2; dy++)
        for (int dx = -2; dx <= 2; dx++)
        {
            int tx = cx + dx, ty = cy + dy;
            if (InBounds(tx, ty) && _tiles[tx, ty] == TileType.Water)
                _tiles[tx, ty] = TileType.Grass;
        }
    }

    /// <summary>Layered sine-wave pseudo-noise — deterministic, no Random needed.</summary>
    private static float HeightNoise(float x, float y) =>
        MathF.Sin(x * 1.3f + y * 0.7f) * 0.45f +
        MathF.Sin(x * 0.5f - y * 1.2f) * 0.30f +
        MathF.Sin(x * 2.2f + y * 1.9f) * 0.15f +
        MathF.Cos(x * 0.8f + y * 0.4f) * 0.10f;

    // ── Accessors ─────────────────────────────────────────────────────────────
    public bool InBounds(int x, int y) =>
        x >= 0 && y >= 0 && x < Width && y < Height;

    public TileType GetTile(int x, int y) =>
        InBounds(x, y) ? _tiles[x, y] : TileType.Stone;

    public bool IsWalkable(int x, int y) =>
        InBounds(x, y) && _tiles[x, y] != TileType.Water;
}
