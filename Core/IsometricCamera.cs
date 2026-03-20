using Microsoft.Xna.Framework;

namespace ConquerMono.Core;

/// <summary>
/// Unified camera for a 2.5D isometric scene.
///
/// Provides two coordinate systems that agree on screen position:
///   1. <see cref="ScreenOffset"/>   — pixel offset fed to SpriteBatch tile rendering.
///   2. <see cref="ViewMatrix"/> /
///      <see cref="ProjectionMatrix"/> — orthographic 3D matrices used to render
///                                      the player's 3D mesh at exactly the same
///                                      screen location as its tile footprint.
///
/// Derivation of the 3D camera angle
/// ───────────────────────────────────
/// We want a dimetric (2 : 1) projection so that:
///   moving 1 tile in world X  →  (+TileHalfW, +TileHalfH) pixels  = (+32, +16)
///   moving 1 tile in world Z  →  (-TileHalfW, +TileHalfH) pixels  = (-32, +16)
///
/// This gives a screen-Y / screen-X ratio of 0.5 per diagonal tile step.
/// Solving for the eye position (a, h, a) that satisfies this ratio yields:
///   h / a = √(2/3)  ≈  0.8165
///
/// The orthographic projection is scaled so that
///   1 world unit  ≡  TileHalfW × √2  pixels  ≈  45.25 px
/// (the horizontal screen stride of one tile unit at 45° azimuth).
/// </summary>
public sealed class IsometricCamera
{
    // ── Tile constants ────────────────────────────────────────────────────────
    public const  int   TileWidth  = 64;
    public const  int   TileHeight = 32;
    public const  int   TileHalfW  = TileWidth  / 2;   // 32
    public const  int   TileHalfH  = TileHeight / 2;   // 16

    /// <summary>Pixels per 3-D world unit, calibrated to match tile width.</summary>
    public static readonly float PixelsPerUnit = TileHalfW * MathF.Sqrt(2f);  // ≈ 45.25

    // ── State ─────────────────────────────────────────────────────────────────
    private readonly int _screenW, _screenH;

    public Vector2 Position        { get; private set; }
    public Vector2 ScreenOffset    { get; private set; }
    public Matrix  ViewMatrix      { get; private set; }
    public Matrix  ProjectionMatrix { get; private set; }

    // ─────────────────────────────────────────────────────────────────────────
    public IsometricCamera(int screenW, int screenH)
    {
        _screenW = screenW;
        _screenH = screenH;

        // Projection is constant (no zoom changes needed yet)
        ProjectionMatrix = Matrix.CreateOrthographic(
            screenW / PixelsPerUnit,
            screenH / PixelsPerUnit,
            -500f, 500f);
    }

    // ─────────────────────────────────────────────────────────────────────────
    /// <summary>
    /// Move the camera to centre on <paramref name="tilePos"/>.
    /// Recomputes ScreenOffset and the 3D View matrix.
    /// </summary>
    public void SetPosition(Vector2 tilePos)
    {
        Position = tilePos;

        // 2D offset: map tilePos to the screen centre
        Vector2 rawScreen = TileToScreen(tilePos, Vector2.Zero);
        ScreenOffset = new Vector2(
            _screenW * 0.5f - rawScreen.X,
            _screenH * 0.5f - rawScreen.Y);

        // 3D view: eye at (d, d√(2/3), d) relative to the world target
        const float d = 20f;
        float h = d * MathF.Sqrt(2f / 3f);           // ≈ 16.33
        var   worldTarget = new Vector3(tilePos.X, 0f, tilePos.Y);
        var   eye         = worldTarget + new Vector3(d, h, d);
        ViewMatrix = Matrix.CreateLookAt(eye, worldTarget, Vector3.Up);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Coordinate helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Convert tile coordinates to screen pixels using an explicit offset.</summary>
    public static Vector2 TileToScreen(Vector2 tile, Vector2 offset) => new(
        (tile.X - tile.Y) * TileHalfW + offset.X,
        (tile.X + tile.Y) * TileHalfH + offset.Y);

    /// <summary>Convert tile coordinates to screen pixels using the current camera offset.</summary>
    public Vector2 TileToScreen(Vector2 tile) => TileToScreen(tile, ScreenOffset);

    /// <summary>Integer overload for whole-tile rendering.</summary>
    public Vector2 TileToScreen(int tx, int ty) =>
        TileToScreen(new Vector2(tx, ty), ScreenOffset);
}
