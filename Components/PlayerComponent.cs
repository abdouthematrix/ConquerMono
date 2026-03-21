namespace ConquerMono.Components;

using ConquerMono.World;

/// <summary>
/// Renders the player as a 3-D humanoid mesh composited over the 2-D tile map.
///
/// Pipeline per frame:
///  1. SpriteBatch blob shadow under the player's screen position.
///  2. Clear depth buffer only (preserves tile colours in the colour buffer).
///  3. BasicEffect indexed box mesh placed at (cell.X, 0, cell.Y) in world space.
///
/// The <see cref="GameCamera"/> View/Projection matrices are calibrated so that
/// a mesh unit at world (x, 0, z) maps to the same viewport pixel as
/// <see cref="Rendering.Coordinates.IsometricCoordinateSystem.MapToScreen"/> for cell (x, z).
/// </summary>
public sealed class PlayerComponent : DrawableGameComponent
{
    private readonly ConquerGame _game;

    private BasicEffect _effect = null!;
    private VertexBuffer _vb = null!;
    private IndexBuffer _ib = null!;
    private int _triCount;
    private Texture2D _shadow = null!;

    public PlayerComponent(ConquerGame game) : base(game)
    {
        _game = game;
        DrawOrder = 10;
        UpdateOrder = 10;
    }

    // ── LoadContent ───────────────────────────────────────────────────────────
    protected override void LoadContent()
    {
        _effect = new BasicEffect(GraphicsDevice) { VertexColorEnabled = true };
        BuildMesh();
        BuildShadow();
    }

    // ── Update ────────────────────────────────────────────────────────────────
    public override void Update(GameTime gt)
    {
        var input = _game.Input;
        var player = _game.Player;
        if (input == null || player == null) return;

        // ── Left-click → set walk target in cell space ────────────────────────
        var viewer = _game.MapViewer;
        if (input.LeftClick && viewer != null && !viewer.Camera.IsPanning)
        {
            // Convert viewport pixel → cell using the camera's inverse transform
            var cellTarget = viewer.Camera.ViewportToCell(input.MousePosition);
            player.SetTarget(cellTarget);
        }

        // ── WASD / Arrow key input (cancels click-to-move) ────────────────────
        var dir = Vector2.Zero;
        if (input.IsHeld(Keys.W) || input.IsHeld(Keys.Up)) dir.Y -= 1;
        if (input.IsHeld(Keys.S) || input.IsHeld(Keys.Down)) dir.Y += 1;
        if (input.IsHeld(Keys.A) || input.IsHeld(Keys.Left)) dir.X -= 1;
        if (input.IsHeld(Keys.D) || input.IsHeld(Keys.Right)) dir.X += 1;

        player.Update(dir, (float)gt.ElapsedGameTime.TotalSeconds);

        // ── Centre camera on player ───────────────────────────────────────────
        if (viewer?.Camera is GameCamera cam && viewer.CoordinateSystem is { } cs)
        {
            if (!cam.IsPanning)
            {
                var puzzlePx = cs.MapToScreen(player.CellPosition);
                cam.Follow(puzzlePx);
            }
            cam.TrackCell(player.CellPosition);
        }
    }

    // ── Draw ──────────────────────────────────────────────────────────────────
    public override void Draw(GameTime gt)
    {
        var player = _game.Player;
        var viewer = _game.MapViewer;
        var sb = _game.SpriteBatch;
        if (player == null || viewer == null || sb == null || _game.SpriteBatch == null) return;
        if (!viewer.IsMapLoaded) return;

        var cam = viewer.Camera;
        var cs = viewer.CoordinateSystem;
        if (cs == null) return;

        // ── 1. Shadow ─────────────────────────────────────────────────────────
        var puzzlePx = cs.MapToScreen(player.CellPosition);
        var vp = GraphicsDevice.Viewport;

        // Convert puzzle-space pixel → viewport pixel via the camera transform
        var screenPos = new Vector2(
            (puzzlePx.X - cam.DrawWindow.X) * cam.Zoom,
            (puzzlePx.Y - cam.DrawWindow.Y) * cam.Zoom);

        sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend);
        sb.Draw(_shadow,
            new Rectangle((int)screenPos.X - 28, (int)screenPos.Y - 10, 56, 20),
            new Color(0, 0, 0, 90));
        sb.End();

        // ── 2. 3-D mesh ───────────────────────────────────────────────────────
        GraphicsDevice.BlendState = BlendState.AlphaBlend;
        GraphicsDevice.DepthStencilState = DepthStencilState.Default;
        GraphicsDevice.RasterizerState = RasterizerState.CullCounterClockwise;
        GraphicsDevice.SamplerStates[0] = SamplerState.LinearWrap;
        GraphicsDevice.Clear(ClearOptions.DepthBuffer, Color.Transparent, 1f, 0);

        float bob = player.IsMoving ? MathF.Sin(player.WalkPhase) * 0.04f : 0f;

        // Rotate the mesh around Y to face the direction of travel.
        // FacingAngle is in cell-space (atan2 of the XZ movement direction).
        // The dimetric camera looks from (+X,+Y,+Z), so a 45° base offset
        // aligns the mesh "forward" with the camera's screen-right direction.
        const float BASE_OFFSET = -MathF.PI / 4f; // align mesh forward with cell +X axis
        var rotation = Matrix.CreateRotationY(-(player.FacingAngle + BASE_OFFSET));

        _effect.View = cam.ViewMatrix;
        _effect.Projection = cam.ProjectionMatrix;
        _effect.World = rotation
                           * Matrix.CreateTranslation(
                                 player.CellPosition.X,
                                 bob,
                                 player.CellPosition.Y);

        GraphicsDevice.SetVertexBuffer(_vb);
        GraphicsDevice.Indices = _ib;
        foreach (var pass in _effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            GraphicsDevice.DrawIndexedPrimitives(
                PrimitiveType.TriangleList, 0, 0, _triCount);
        }
    }

    // ── Mesh builder ──────────────────────────────────────────────────────────
    private void BuildMesh()
    {
        var verts = new List<VertexPositionColor>();
        var indices = new List<short>();

        // Plain array — ReadOnlySpan<stackalloc> cannot be captured by a local function
        float[] lum = { 1.00f, 0.55f, 0.82f, 0.72f, 0.68f, 0.92f };

        void Box(Vector3 mn, Vector3 mx, Color col)
        {
            Vector3[] c =
            [
                new(mn.X,mn.Y,mn.Z),new(mx.X,mn.Y,mn.Z),new(mx.X,mx.Y,mn.Z),new(mn.X,mx.Y,mn.Z),
                new(mn.X,mn.Y,mx.Z),new(mx.X,mn.Y,mx.Z),new(mx.X,mx.Y,mx.Z),new(mn.X,mx.Y,mx.Z),
            ];
            int[][] faces =
            [
                [3,2,1,0],[4,5,6,7],[0,1,5,4],[7,6,2,3],[4,7,3,0],[1,2,6,5]
            ];
            for (int f = 0; f < 6; f++)
            {
                short v0 = (short)verts.Count;
                float l = lum[f];
                Color fc = new((int)(col.R * l), (int)(col.G * l), (int)(col.B * l));
                foreach (int ci in faces[f]) verts.Add(new VertexPositionColor(c[ci], fc));
                indices.AddRange([v0,(short)(v0+1),(short)(v0+2),
                                  v0,(short)(v0+2),(short)(v0+3)]);
            }
        }

        Box(new(-0.27f, 0.00f, -0.14f), new(-0.05f, 0.62f, 0.14f), new Color(70, 55, 135));
        Box(new(0.05f, 0.00f, -0.14f), new(0.27f, 0.62f, 0.14f), new Color(70, 55, 135));
        Box(new(-0.30f, 0.62f, -0.17f), new(0.30f, 1.45f, 0.17f), new Color(185, 38, 38));
        Box(new(-0.38f, 1.20f, -0.15f), new(-0.28f, 1.45f, 0.15f), new Color(210, 170, 28));
        Box(new(0.28f, 1.20f, -0.15f), new(0.38f, 1.45f, 0.15f), new Color(210, 170, 28));
        Box(new(-0.48f, 0.68f, -0.13f), new(-0.28f, 1.35f, 0.13f), new Color(160, 32, 32));
        Box(new(0.28f, 0.68f, -0.13f), new(0.48f, 1.35f, 0.13f), new Color(160, 32, 32));
        Box(new(-0.10f, 1.45f, -0.10f), new(0.10f, 1.56f, 0.10f), new Color(210, 170, 130));
        Box(new(-0.21f, 1.56f, -0.19f), new(0.21f, 1.95f, 0.19f), new Color(222, 178, 135));
        Box(new(-0.23f, 1.88f, -0.21f), new(0.23f, 2.05f, 0.21f), new Color(195, 155, 20));
        Box(new(-0.05f, 2.04f, -0.04f), new(0.05f, 2.22f, 0.04f), new Color(220, 40, 40));

        _vb = new VertexBuffer(GraphicsDevice, VertexPositionColor.VertexDeclaration,
                               verts.Count, BufferUsage.WriteOnly);
        _vb.SetData(verts.ToArray());

        _ib = new IndexBuffer(GraphicsDevice, IndexElementSize.SixteenBits,
                              indices.Count, BufferUsage.WriteOnly);
        _ib.SetData(indices.ToArray());
        _triCount = indices.Count / 3;
    }

    private void BuildShadow()
    {
        const int W = 56, H = 20;
        var px = new Color[W * H];
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float dx = (x - W * .5f) / (W * .5f);
                float dy = (y - H * .5f) / (H * .5f);
                float d = dx * dx + dy * dy;
                if (d < 1f) px[y * W + x] = new Color(0, 0, 0, (int)((1f - d) * 180f));
            }
        _shadow = new Texture2D(GraphicsDevice, W, H);
        _shadow.SetData(px);
    }
}