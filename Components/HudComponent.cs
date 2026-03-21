namespace ConquerMono.Components;

/// <summary>
/// HUD overlay — HP/MP/EXP bars, minimap thumbnail, coordinate readout, layer
/// toggle hints, and a controls reference.  Everything is drawn with a 1×1
/// white pixel tinted via SpriteBatch; no font asset is required.
/// </summary>
public sealed class HudComponent : DrawableGameComponent
{
    private readonly ConquerGame _game;
    private Texture2D _px      = null!;   // 1×1 white
    private Texture2D _minimap = null!;
    private string    _lastMinimapMapPath = string.Empty;

    // ── Pixel digit bitmaps (5w × 7h, bit 4 = leftmost pixel) ────────────────
    private static readonly byte[][] Digits =
    [
        [0b11110,0b10010,0b10010,0b10010,0b10010,0b10010,0b11110], // 0
        [0b01100,0b00100,0b00100,0b00100,0b00100,0b00100,0b01110], // 1
        [0b11110,0b00010,0b00010,0b11110,0b10000,0b10000,0b11110], // 2
        [0b11110,0b00010,0b00010,0b11110,0b00010,0b00010,0b11110], // 3
        [0b10010,0b10010,0b10010,0b11110,0b00010,0b00010,0b00010], // 4
        [0b11110,0b10000,0b10000,0b11110,0b00010,0b00010,0b11110], // 5
        [0b11110,0b10000,0b10000,0b11110,0b10010,0b10010,0b11110], // 6
        [0b11110,0b00010,0b00010,0b00110,0b00100,0b00100,0b00100], // 7
        [0b11110,0b10010,0b10010,0b11110,0b10010,0b10010,0b11110], // 8
        [0b11110,0b10010,0b10010,0b11110,0b00010,0b00010,0b11110], // 9
    ];

    public HudComponent(ConquerGame game) : base(game) { _game = game; DrawOrder = 20; }

    protected override void LoadContent()
    {
        _px = new Texture2D(GraphicsDevice, 1, 1);
        _px.SetData(new[] { Color.White });
        RebuildMinimap();
    }

    public override void Update(GameTime gt)
    {
        var input = _game.Input;
        if (input == null) return;

        // ── Camera controls ───────────────────────────────────────────────────
        var cam = _game.MapViewer?.Camera;
        if (cam != null)
        {
            // Zoom: scroll wheel
            if (input.ScrollDelta != 0)
                cam.ZoomAround(1f + input.ScrollDelta / 1200f, input.MousePosition);

            // Pan: right-mouse drag
            cam.IsPanning = input.RightHeld;
            if (input.RightHeld)
                cam.PanByPixels(input.MouseDelta);

            // Zoom shortcuts: +/-
            if (input.IsPressed(Keys.OemPlus)  || input.IsPressed(Keys.Add))      cam.Zoom *= 1.2f;
            if (input.IsPressed(Keys.OemMinus) || input.IsPressed(Keys.Subtract)) cam.Zoom /= 1.2f;

            // View shortcuts
            if (input.IsPressed(Keys.Home) || input.IsPressed(Keys.H)) cam.ResetView();
            if (input.IsPressed(Keys.F))                                cam.FitToWindow();
        }

        // ── Layer toggles ─────────────────────────────────────────────────────
        var mv = _game.MapViewer;
        if (mv != null)
        {
            if (input.IsPressed(Keys.D1)) mv.SetLayerEnabled(DrawingAspect.Backdrop,          !mv.IsLayerEnabled(DrawingAspect.Backdrop));
            if (input.IsPressed(Keys.D2)) mv.SetLayerEnabled(DrawingAspect.Puzzle,             !mv.IsLayerEnabled(DrawingAspect.Puzzle));
            if (input.IsPressed(Keys.D3)) mv.SetLayerEnabled(DrawingAspect.Scene,              !mv.IsLayerEnabled(DrawingAspect.Scene));
            if (input.IsPressed(Keys.D4)) mv.SetLayerEnabled(DrawingAspect.TerrainObject,      !mv.IsLayerEnabled(DrawingAspect.TerrainObject));
            if (input.IsPressed(Keys.D5)) mv.SetLayerEnabled(DrawingAspect.Portals,            !mv.IsLayerEnabled(DrawingAspect.Portals));
            if (input.IsPressed(Keys.D6)) mv.SetLayerEnabled(DrawingAspect.MapCell,            !mv.IsLayerEnabled(DrawingAspect.MapCell));
            if (input.IsPressed(Keys.D7)) mv.SetLayerEnabled(DrawingAspect.PuzzleGrid,         !mv.IsLayerEnabled(DrawingAspect.PuzzleGrid));
            if (input.IsPressed(Keys.D8)) mv.SetLayerEnabled(DrawingAspect.TerrainObjectGrid,  !mv.IsLayerEnabled(DrawingAspect.TerrainObjectGrid));
            if (input.IsPressed(Keys.D9)) mv.SetLayerEnabled(DrawingAspect.SceneGrid,          !mv.IsLayerEnabled(DrawingAspect.SceneGrid));
        }

        // Rebuild minimap whenever a new map is loaded
        var currentPath = _game.Settings.LastMapPath ?? string.Empty;
        if (mv != null && mv.IsMapLoaded && currentPath != _lastMinimapMapPath)
        {
            _lastMinimapMapPath = currentPath;
            RebuildMinimap();
        }
    }

    public override void Draw(GameTime gt)
    {
        var sb = _game.SpriteBatch;
        if (sb == null) return;

        int sw = GraphicsDevice.Viewport.Width;
        int sh = GraphicsDevice.Viewport.Height;

        sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);

        DrawCharPanel(sb, sh);
        DrawMinimap(sb, sw);
        DrawCoords(sb);
        DrawLayerHints(sb, sw, sh);

        sb.End();
    }

    // ── Character panel ───────────────────────────────────────────────────────
    private void DrawCharPanel(SpriteBatch sb, int sh)
    {
        var p   = _game.Player;
        if (p == null) return;
        const int PX = 12, H = 120;
        int py = sh - H - 12;

        Fill(sb, PX, py, 214, H, new Color(10, 8, 24, 210));
        Border(sb, PX, py, 214, H, new Color(180, 140, 40));

        // Level badge
        Fill(sb, PX + 4, py + 4, 36, 18, new Color(180, 140, 40));
        DrawInt(sb, p.Level, PX + 7, py + 7, Color.Black, 2);

        // HP
        Bar(sb, PX + 4, py + 28, 206, 20,
            (float)p.Health / p.MaxHealth,
            new Color(200, 35, 35), new Color(60, 10, 10), new Color(230, 80, 80));
        DrawInt(sb, p.Health, PX + 8, py + 32, Color.White, 2);

        // MP
        Bar(sb, PX + 4, py + 54, 206, 20,
            (float)p.Mana / p.MaxMana,
            new Color(40, 80, 210), new Color(10, 20, 80), new Color(90, 140, 255));
        DrawInt(sb, p.Mana, PX + 8, py + 58, Color.White, 2);

        // EXP
        Bar(sb, PX + 4, py + 80, 206, 10, 0.63f,
            new Color(180, 140, 30), new Color(60, 40, 5), new Color(255, 220, 60));

        // Corner gems
        foreach (var (cx, cy) in new[]
            { (PX+2,py+2),(PX+210,py+2),(PX+2,py+H-6),(PX+210,py+H-6) })
            Fill(sb, cx, cy, 4, 4, new Color(200, 160, 40));
    }

    // ── Minimap ───────────────────────────────────────────────────────────────
    private void DrawMinimap(SpriteBatch sb, int sw)
    {
        const int MM = 160, MAR = 12;
        int mx = sw - MM - MAR, my = MAR;

        Fill(sb, mx - 4, my - 4, MM + 8, MM + 8, new Color(10, 8, 24, 220));
        Border(sb, mx - 4, my - 4, MM + 8, MM + 8, new Color(180, 140, 40));
        sb.Draw(_minimap, new Rectangle(mx, my, MM, MM), Color.White);

        var mv = _game.MapViewer;
        var p  = _game.Player;
        if (mv == null || p == null || !mv.IsMapLoaded) return;

        // Player dot — position in cell space normalised to map bounds
        var mapData = _game.CurrentMapData;
        int mapW = mapData?.Bounds.Width  ?? 1;
        int mapH = mapData?.Bounds.Height ?? 1;
        int px2 = mx + (int)(p.CellPosition.X / mapW * MM);
        int py2 = my + (int)(p.CellPosition.Y / mapH * MM);
        Fill(sb, px2 - 3, py2 - 3, 7, 7, Color.Yellow);
        Fill(sb, px2 - 1, py2 - 1, 3, 3, Color.White);
    }

    // ── Coordinate readout ────────────────────────────────────────────────────
    private void DrawCoords(SpriteBatch sb)
    {
        var p = _game.Player;
        if (p == null) return;
        Fill(sb, 12, 12, 140, 24, new Color(10, 8, 24, 200));
        Border(sb, 12, 12, 140, 24, new Color(180, 140, 40));
        Fill(sb, 16, 20, 6, 2, new Color(180, 140, 40));
        DrawInt(sb, (int)p.CellPosition.X, 26, 15, new Color(220, 200, 100), 2);
        Fill(sb, 82, 20, 6, 2, new Color(100, 160, 220));
        DrawInt(sb, (int)p.CellPosition.Y, 92, 15, new Color(120, 180, 255), 2);
    }

    // ── Layer hints ───────────────────────────────────────────────────────────
    private void DrawLayerHints(SpriteBatch sb, int sw, int sh)
    {
        const int W = 190, H = 150;
        int hx = sw - W - 12, hy = sh - H - 12;
        Fill(sb, hx, hy, W, H, new Color(10, 8, 24, 180));
        Border(sb, hx, hy, W, H, new Color(80, 70, 50));

        var mv = _game.MapViewer;
        var keys = new[]
        {
            ("1 Backdrop",   DrawingAspect.Backdrop),
            ("2 Puzzle",     DrawingAspect.Puzzle),
            ("3 Scene",      DrawingAspect.Scene),
            ("4 Terrain",    DrawingAspect.TerrainObject),
            ("5 Portals",    DrawingAspect.Portals),
            ("6 Cell debug", DrawingAspect.MapCell),
            ("7 PuzzleGrid", DrawingAspect.PuzzleGrid),
            ("8 ObjGrid",    DrawingAspect.TerrainObjectGrid),
            ("9 SceneGrid",  DrawingAspect.SceneGrid),
        };

        for (int i = 0; i < keys.Length; i++)
        {
            bool on = mv?.IsLayerEnabled(keys[i].Item2) ?? false;
            var  c  = on ? new Color(80, 220, 80) : new Color(150, 80, 80);
            Fill(sb, hx + 6, hy + 8 + i * 15, 8, 8, c);
            // Draw key label as a coloured stripe
            Fill(sb, hx + 18, hy + 10 + i * 15, 90, 4, new Color(160, 150, 100, 180));
        }

        // WASD / arrows reminder
        Fill(sb, hx + 120, hy + 8,  40, 10, new Color(100, 100, 80, 120));
        Fill(sb, hx + 120, hy + 20, 40, 10, new Color(100, 100, 80, 120));
    }

    // ── Minimap texture builder ───────────────────────────────────────────────
    private void RebuildMinimap()
    {
        var mv = _game.MapViewer;
        if (mv == null || !mv.IsMapLoaded) { MakeBlankMinimap(); return; }

        // Access internal MapData via ConquerGame
        var mapData = _game.CurrentMapData;
        if (mapData == null) { MakeBlankMinimap(); return; }

        int w = mapData.Bounds.Width, h = mapData.Bounds.Height;
        var px = new Color[w * h];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            px[y * w + x] = mapData.Cells[x, y].Access switch
            {
                MapCellAccessType.Walkable => new Color(50, 120, 40),
                MapCellAccessType.Blocked  => new Color(80, 30, 30),
                MapCellAccessType.Portal   => new Color(255, 220, 0),
                MapCellAccessType.Scene    => new Color(38, 82, 185),
                MapCellAccessType.Terrain  => new Color(38, 185, 185),
                MapCellAccessType.Effect   => new Color(210, 120, 20),
                MapCellAccessType.Sound    => new Color(140, 20, 200),
                _                          => Color.DimGray,
            };
        }
        _minimap?.Dispose();
        _minimap = new Texture2D(GraphicsDevice, w, h);
        _minimap.SetData(px);
    }

    private void MakeBlankMinimap()
    {
        _minimap?.Dispose();
        _minimap = new Texture2D(GraphicsDevice, 1, 1);
        _minimap.SetData(new[] { Color.DimGray });
    }

    // ── Primitive helpers ─────────────────────────────────────────────────────
    private void Fill(SpriteBatch sb, int x, int y, int w, int h, Color c) =>
        sb.Draw(_px, new Rectangle(x, y, w, h), c);

    private void Border(SpriteBatch sb, int x, int y, int w, int h, Color c)
    {
        Fill(sb, x,       y,       w, 2, c);
        Fill(sb, x,       y+h-2,   w, 2, c);
        Fill(sb, x,       y,       2, h, c);
        Fill(sb, x+w-2,   y,       2, h, c);
    }

    private void Bar(SpriteBatch sb, int x, int y, int w, int h,
                     float fill, Color fc, Color bg, Color gl)
    {
        Fill(sb, x, y, w, h, bg);
        int fw = (int)(w * Math.Clamp(fill, 0f, 1f));
        if (fw > 0) { Fill(sb, x, y, fw, h, fc); Fill(sb, x, y, fw, 2, gl); }
        Border(sb, x, y, w, h, new Color(80, 70, 50));
    }

    private void DrawInt(SpriteBatch sb, int v, int x, int y, Color col, int scale)
    {
        foreach (char ch in v.ToString())
        {
            if (ch is >= '0' and <= '9')
            {
                DrawDigit(sb, ch - '0', x, y, col, scale);
                x += (5 + 1) * scale;
            }
        }
    }

    private void DrawDigit(SpriteBatch sb, int d, int ox, int oy, Color col, int sc)
    {
        var rows = Digits[d];
        for (int row = 0; row < 7; row++)
        {
            byte mask = rows[row];
            for (int bit = 0; bit < 5; bit++)
                if ((mask & (1 << (4 - bit))) != 0)
                    Fill(sb, ox + bit * sc, oy + row * sc, sc, sc, col);
        }
    }
}
