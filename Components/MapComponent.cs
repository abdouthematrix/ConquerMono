using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ConquerMono.Core;
using ConquerMono.World;

namespace ConquerMono.Components;

/// <summary>
/// DrawableGameComponent responsible for rendering the isometric tile map.
///
/// All tile textures are generated at load time from colour definitions —
/// no external assets are required.
///
/// Rendering strategy
/// ──────────────────
///  • Only tiles within a viewport-radius of the camera are visited.
///  • Tiles are iterated in painter's-algorithm order (ascending tx + ty)
///    so that closer tiles naturally overdraw farther ones.
///  • A single SpriteBatch.Begin / End wraps the entire tile pass.
/// </summary>
public sealed class MapComponent : DrawableGameComponent
{
    // ── Constants ─────────────────────────────────────────────────────────────
    private const int VisibleRadius = 18;   // half-width of the visible tile window

    // ── Dependencies ──────────────────────────────────────────────────────────
    private readonly ConquerGame _game;

    // ── Resources ─────────────────────────────────────────────────────────────
    private Dictionary<TileType, Texture2D> _textures = null!;
    private Texture2D _gridLine = null!;

    public MapComponent(ConquerGame game) : base(game) => _game = game;

    // ─────────────────────────────────────────────────────────────────────────
    protected override void LoadContent()
    {
        _textures = new()
        {
            //                           top face              left face             right face
            [TileType.Grass] = MakeTile(new Color(82, 163, 65),  new Color(52, 110, 38), new Color(43, 92, 30)),
            [TileType.Road]  = MakeTile(new Color(180,162,108),  new Color(130,112, 72), new Color(108, 93, 58)),
            [TileType.Water] = MakeTile(new Color( 58,128,210),  new Color( 38, 92,175), new Color( 28, 72,155)),
            [TileType.Stone] = MakeTile(new Color(148,140,130),  new Color( 98, 92, 83), new Color( 78, 73, 65)),
            [TileType.Sand]  = MakeTile(new Color(218,198,132),  new Color(175,155, 98), new Color(158,138, 78)),
        };

        // 1×1 white pixel used for grid overlay
        _gridLine = new Texture2D(GraphicsDevice, 1, 1);
        _gridLine.SetData(new[] { Color.White });
    }

    // ─────────────────────────────────────────────────────────────────────────
    public override void Draw(GameTime gameTime)
    {
        var cam = _game.Camera;
        var map = _game.Map;
        var sb  = _game.SpriteBatch;

        sb.Begin(SpriteSortMode.Deferred,
                 BlendState.AlphaBlend,
                 SamplerState.PointClamp,
                 null, null);

        int cx = (int)cam.Position.X;
        int cy = (int)cam.Position.Y;

        int x0 = Math.Max(0, cx - VisibleRadius);
        int x1 = Math.Min(map.Width  - 1, cx + VisibleRadius);
        int y0 = Math.Max(0, cy - VisibleRadius);
        int y1 = Math.Min(map.Height - 1, cy + VisibleRadius);

        // ── Painter's algorithm: iterate diagonals (constant tx+ty sums) ──────
        for (int sum = x0 + y0; sum <= x1 + y1; sum++)
        {
            for (int tx = x0; tx <= x1; tx++)
            {
                int ty = sum - tx;
                if (ty < y0 || ty > y1) continue;

                TileType tile = map.GetTile(tx, ty);
                Texture2D tex = _textures[tile];

                Vector2 pos = cam.TileToScreen(tx, ty);

                // Texture origin is its top-left; shift so diamond top aligns
                sb.Draw(tex,
                    new Vector2(pos.X - IsometricCamera.TileHalfW,
                                pos.Y - IsometricCamera.TileHalfH),
                    Color.White);
            }
        }

        sb.End();
    }

    // ── Texture factory ───────────────────────────────────────────────────────
    /// <summary>
    /// Builds a 64×32 isometric diamond texture split into three visible faces:
    ///   top (upper triangle), left face (lower-left), right face (lower-right).
    /// Pixels outside the diamond are transparent.
    /// </summary>
    private Texture2D MakeTile(Color topColor, Color leftColor, Color rightColor)
    {
        int w = IsometricCamera.TileWidth;
        int h = IsometricCamera.TileHeight;
        var pixels = new Color[w * h];

        float hw = w * 0.5f;   // 32
        float hh = h * 0.5f;   // 16

        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            float nx = (x - hw) / hw;   // −1 … +1 horizontally
            float ny = (y - hh) / hh;   // −1 … +1 vertically

            // Diamond test: |nx| + |ny| ≤ 1
            float dist = MathF.Abs(nx) + MathF.Abs(ny);
            if (dist > 1.0f)
            {
                pixels[y * w + x] = Color.Transparent;
                continue;
            }

            // Edge highlight — brighter near the diamond boundary makes tiles "pop"
            float edge  = 1f - dist;
            float light = 0.78f + edge * 0.22f;

            Color baseColor;
            if (y < hh)
            {
                // Top face
                baseColor = topColor;
            }
            else
            {
                // Lower half split left / right
                baseColor = x < hw ? leftColor : rightColor;
                light *= 0.88f;   // side faces slightly darker
            }

            pixels[y * w + x] = ScaleColor(baseColor, light);
        }

        var tex = new Texture2D(GraphicsDevice, w, h);
        tex.SetData(pixels);
        return tex;
    }

    private static Color ScaleColor(Color c, float f) => new(
        (int)(c.R * f),
        (int)(c.G * f),
        (int)(c.B * f),
        255);
}
