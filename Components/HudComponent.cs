using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ConquerMono.World;

namespace ConquerMono.Components;

/// <summary>
/// DrawableGameComponent that renders all HUD elements:
///  • HP / MP bars  (bottom-left panel)
///  • Minimap       (top-right corner)
///  • Coordinates   (top-left, rendered as a pixel-digit display)
///  • Controls hint (bottom-right)
///
/// Everything is drawn with a single SpriteBatch Begin/End using the 1×1
/// white pixel trick — no external font or texture assets required.
/// </summary>
public sealed class HudComponent : DrawableGameComponent
{
    private readonly ConquerGame _game;

    // ── Shared resources ──────────────────────────────────────────────────────
    private Texture2D _pixel = null!;   // 1×1 white, tinted by SpriteBatch colour arg

    // ── Minimap cache ─────────────────────────────────────────────────────────
    private Texture2D _minimapTex = null!;
    private int _cachedMapW, _cachedMapH;

    // ── Pixel-font digit bitmaps (5 wide × 7 tall, packed as bit-rows) ────────
    // Each digit is 7 bytes; bit 4 = leftmost pixel of a 5-pixel row.
    private static readonly byte[][] DigitBits =
    [
        [0b11110, 0b10010, 0b10010, 0b10010, 0b10010, 0b10010, 0b11110], // 0
        [0b01100, 0b00100, 0b00100, 0b00100, 0b00100, 0b00100, 0b01110], // 1
        [0b11110, 0b00010, 0b00010, 0b11110, 0b10000, 0b10000, 0b11110], // 2
        [0b11110, 0b00010, 0b00010, 0b11110, 0b00010, 0b00010, 0b11110], // 3
        [0b10010, 0b10010, 0b10010, 0b11110, 0b00010, 0b00010, 0b00010], // 4
        [0b11110, 0b10000, 0b10000, 0b11110, 0b00010, 0b00010, 0b11110], // 5
        [0b11110, 0b10000, 0b10000, 0b11110, 0b10010, 0b10010, 0b11110], // 6
        [0b11110, 0b00010, 0b00010, 0b00110, 0b00100, 0b00100, 0b00100], // 7
        [0b11110, 0b10010, 0b10010, 0b11110, 0b10010, 0b10010, 0b11110], // 8
        [0b11110, 0b10010, 0b10010, 0b11110, 0b00010, 0b00010, 0b11110], // 9
    ];

    public HudComponent(ConquerGame game) : base(game) => _game = game;

    // ─────────────────────────────────────────────────────────────────────────
    protected override void LoadContent()
    {
        _pixel = new Texture2D(GraphicsDevice, 1, 1);
        _pixel.SetData(new[] { Color.White });

        BuildMinimapTexture();
    }

    // ─────────────────────────────────────────────────────────────────────────
    public override void Draw(GameTime gameTime)
    {
        var sb     = _game.SpriteBatch;
        var player = _game.Player;
        int sw     = GraphicsDevice.Viewport.Width;
        int sh     = GraphicsDevice.Viewport.Height;

        sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend,
                 SamplerState.PointClamp, null, null);

        DrawCharacterPanel(sb, player, sh);
        DrawMinimap(sb, player, sw);
        DrawCoordinates(sb, player);
        DrawControlsHint(sb, sw, sh);

        sb.End();
    }

    // ── Panels ────────────────────────────────────────────────────────────────
    private void DrawCharacterPanel(SpriteBatch sb, PlayerEntity p, int sh)
    {
        const int PX = 12, PY_OFFSET = 130;
        int py = sh - PY_OFFSET;

        // Background panel
        FillRect(sb, PX, py, 210, 118, new Color(10, 8, 24, 210));
        DrawBorder(sb, PX, py, 210, 118, new Color(180, 140, 40));

        // Level badge
        FillRect(sb, PX + 4, py + 4, 36, 18, new Color(180, 140, 40));
        DrawPixelInt(sb, p.Level, PX + 7, py + 7, Color.Black, 2);

        // Name strip (coloured bar used as label background)
        FillRect(sb, PX + 44, py + 4, 162, 18, new Color(40, 30, 70, 200));
        // Draw "HERO" letters as coloured dots — just a decorative bar here
        FillRect(sb, PX + 48, py + 8, 80, 4, new Color(220, 200, 100));

        // ── HP bar ───────────────────────────────────────────────────────────
        int barX = PX + 4, barY = py + 30, barW = 202, barH = 20;
        DrawBar(sb, barX, barY, barW, barH,
                (float)p.Health / p.MaxHealth,
                new Color(200, 35, 35), new Color(60, 10, 10), new Color(230, 80, 80));
        DrawPixelInt(sb, p.Health, barX + 4, barY + 4, Color.White, 2);

        // ── MP bar ───────────────────────────────────────────────────────────
        barY += 26;
        DrawBar(sb, barX, barY, barW, barH,
                (float)p.Mana / p.MaxMana,
                new Color(40, 80, 210), new Color(10, 20, 80), new Color(90, 140, 255));
        DrawPixelInt(sb, p.Mana, barX + 4, barY + 4, Color.White, 2);

        // ── EXP bar ──────────────────────────────────────────────────────────
        barY += 26;
        DrawBar(sb, barX, barY, barW, 10,
                0.63f,
                new Color(180, 140, 30), new Color(60, 40, 5), new Color(255, 220, 60));

        // ── Decorative corner gems ────────────────────────────────────────────
        FillRect(sb, PX + 2,       py + 2,       4, 4, new Color(200, 160, 40));
        FillRect(sb, PX + 210 - 6, py + 2,       4, 4, new Color(200, 160, 40));
        FillRect(sb, PX + 2,       py + 118 - 6, 4, 4, new Color(200, 160, 40));
        FillRect(sb, PX + 210 - 6, py + 118 - 6, 4, 4, new Color(200, 160, 40));
    }

    private void DrawMinimap(SpriteBatch sb, PlayerEntity player, int sw)
    {
        const int MM_W = 160, MM_H = 160, MARGIN = 12;
        int mx = sw - MM_W - MARGIN;
        int my = MARGIN;

        // Panel backdrop
        FillRect(sb, mx - 4, my - 4, MM_W + 8, MM_H + 8, new Color(10, 8, 24, 220));
        DrawBorder(sb, mx - 4, my - 4, MM_W + 8, MM_H + 8, new Color(180, 140, 40));

        // Tile texture
        sb.Draw(_minimapTex, new Rectangle(mx, my, MM_W, MM_H), Color.White);

        // Player dot
        var map = _game.Map;
        int px  = mx + (int)(player.TilePosition.X / map.Width  * MM_W);
        int py  = my + (int)(player.TilePosition.Y / map.Height * MM_H);
        FillRect(sb, px - 3, py - 3, 7, 7, new Color(255, 255,   0));
        FillRect(sb, px - 1, py - 1, 3, 3, Color.White);

        // Camera view rectangle (approximate)
        const int ViewR = 18;
        int vw = (int)(ViewR * 2f / map.Width  * MM_W);
        int vh = (int)(ViewR * 2f / map.Height * MM_H);
        DrawBorder(sb, px - vw / 2, py - vh / 2, vw, vh, new Color(255, 255, 100, 140));
    }

    private void DrawCoordinates(SpriteBatch sb, PlayerEntity p)
    {
        FillRect(sb, 12, 12, 128, 24, new Color(10, 8, 24, 200));
        DrawBorder(sb, 12, 12, 128, 24, new Color(180, 140, 40));

        int ix = (int)p.TilePosition.X;
        int iy = (int)p.TilePosition.Y;

        // "X:" label marker
        FillRect(sb, 16, 20, 6, 2, new Color(180, 140, 40));
        DrawPixelInt(sb, ix, 26, 15, new Color(220, 200, 100), 2);

        FillRect(sb, 76, 20, 6, 2, new Color(100, 160, 220));
        DrawPixelInt(sb, iy, 86, 15, new Color(120, 180, 255), 2);
    }

    private void DrawControlsHint(SpriteBatch sb, int sw, int sh)
    {
        const int W = 140, H = 44;
        int hx = sw - W - 12;
        int hy = sh - H - 12;

        FillRect(sb, hx, hy, W, H, new Color(10, 8, 24, 180));
        DrawBorder(sb, hx, hy, W, H, new Color(80, 70, 50));

        // Draw tiny arrow-key icons
        Color arrowCol = new(160, 150, 100);
        // Up arrow
        FillRect(sb, hx + 56, hy + 6,  12, 12, arrowCol);
        // Left
        FillRect(sb, hx + 40, hy + 20, 12, 12, arrowCol);
        // Down
        FillRect(sb, hx + 56, hy + 20, 12, 12, arrowCol);
        // Right
        FillRect(sb, hx + 72, hy + 20, 12, 12, arrowCol);
        // WASD hint
        FillRect(sb, hx + 14, hy + 8,  10, 10, new Color(100, 100, 100));
        FillRect(sb, hx + 4,  hy + 20, 10, 10, new Color(100, 100, 100));
        FillRect(sb, hx + 14, hy + 20, 10, 10, new Color(100, 100, 100));
        FillRect(sb, hx + 24, hy + 20, 10, 10, new Color(100, 100, 100));
    }

    // ── Primitive helpers ─────────────────────────────────────────────────────
    private void FillRect(SpriteBatch sb, int x, int y, int w, int h, Color c) =>
        sb.Draw(_pixel, new Rectangle(x, y, w, h), c);

    private void DrawBorder(SpriteBatch sb, int x, int y, int w, int h, Color c)
    {
        FillRect(sb, x,         y,         w, 2, c);
        FillRect(sb, x,         y + h - 2, w, 2, c);
        FillRect(sb, x,         y,         2, h, c);
        FillRect(sb, x + w - 2, y,         2, h, c);
    }

    private void DrawBar(SpriteBatch sb,
                         int x, int y, int w, int h,
                         float fill,
                         Color fillColor, Color bgColor, Color glint)
    {
        // Background
        FillRect(sb, x, y, w, h, bgColor);
        // Fill
        int fw = (int)(w * Math.Clamp(fill, 0f, 1f));
        if (fw > 0)
        {
            FillRect(sb, x, y, fw, h, fillColor);
            // 1-pixel glint at top
            FillRect(sb, x, y, fw, 2, glint);
        }
        // Frame
        DrawBorder(sb, x, y, w, h, new Color(80, 70, 50));
    }

    // ── Pixel-digit renderer ──────────────────────────────────────────────────
    /// <summary>Render an integer using the built-in 5×7 pixel font.</summary>
    private void DrawPixelInt(SpriteBatch sb, int value, int x, int y, Color col, int scale)
    {
        string s = value.ToString();
        int cx = x;
        foreach (char ch in s)
        {
            if (ch >= '0' && ch <= '9')
            {
                DrawDigit(sb, ch - '0', cx, y, col, scale);
                cx += (5 + 1) * scale;
            }
        }
    }

    private void DrawDigit(SpriteBatch sb, int d, int ox, int oy, Color colx, int scale)
    {
        byte[] rows = DigitBits[d];
        for (int row = 0; row < 7; row++)
        {
            byte mask = rows[row];
            for (int col = 0; col < 5; col++)
            {
                if ((mask & (1 << (4 - col))) != 0)
                {
                    FillRect(sb,
                             ox + col * scale,
                             oy + row * scale,
                             scale, scale, colx);
                }
            }
        }
    }

    // ── Minimap ───────────────────────────────────────────────────────────────
    private void BuildMinimapTexture()
    {
        var map = _game.Map;
        _cachedMapW = map.Width;
        _cachedMapH = map.Height;

        var pixels = new Color[map.Width * map.Height];
        for (int ty = 0; ty < map.Height; ty++)
        for (int tx = 0; tx < map.Width;  tx++)
        {
            pixels[ty * map.Width + tx] = map.GetTile(tx, ty) switch
            {
                TileType.Grass  => new Color( 50, 120,  40),
                TileType.Road   => new Color(145, 128,  80),
                TileType.Water  => new Color( 38,  82, 185),
                TileType.Stone  => new Color(108,  98,  88),
                TileType.Sand   => new Color(185, 162,  98),
                _               => Color.Gray,
            };
        }

        _minimapTex = new Texture2D(GraphicsDevice, map.Width, map.Height);
        _minimapTex.SetData(pixels);
    }
}
