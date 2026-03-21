using System.Collections.Generic;
using ConquerMono.World;
using ConquerMono.C3Format;

namespace ConquerMono.Components;

/// <summary>
/// Renders the player using a real C3 skeletal model (with per-state motion
/// switching) or falls back to the procedural coloured-box humanoid.
///
/// Motion switching
/// ────────────────
///   State       Key trigger       C3 motion source
///   Idle        (no input)        PlayerIdleMotionPath
///   Walking     WASD / click      PlayerWalkMotionPath
///   Running     Shift+WASD/click  PlayerRunMotionPath
///   Jumping     Space             PlayerJumpMotionPath
///
/// If a motion path is empty the last-loaded motion stays active.
/// The World matrix applied to C3Renderer is:
///   Scale × FacingRotation(Y) × Translate(cell.X, bob, cell.Y)
/// </summary>
public sealed class PlayerComponent : DrawableGameComponent
{
    private readonly ConquerGame _game;

    // ── C3 renderer ───────────────────────────────────────────────────────────
    private C3Renderer? _c3;
    private MovementState _lastState = (MovementState)(-1); // force first switch

    // ── Pre-loaded motion paths ───────────────────────────────────────────────
    private string? _idlePath;
    private string? _walkPath;
    private string? _runPath;
    private string? _jumpPath;

    // ── Procedural fallback resources ─────────────────────────────────────────
    private BasicEffect? _effect;
    private VertexBuffer? _vb;
    private IndexBuffer? _ib;
    private int _triCount;

    // ── Shared shadow ─────────────────────────────────────────────────────────
    private Texture2D _shadow = null!;

    // ── C3 world-up correction (matches Game1.cs WorldRotation) ──────────────
    private static readonly Matrix C3WorldRotation =
        Matrix.CreateRotationX(MathHelper.ToRadians(90f)) *
        Matrix.CreateRotationY(MathHelper.ToRadians(180f));

    public PlayerComponent(ConquerGame game) : base(game)
    {
        _game = game;
        DrawOrder = 10;
        UpdateOrder = 10;
    }

    // ── LoadContent ───────────────────────────────────────────────────────────
    protected override void LoadContent()
    {
        var s = _game.Settings;

        // Cache motion paths (null when empty or file missing)
        _idlePath = Existing(s.PlayerIdleMotionPath);
        _walkPath = Existing(s.PlayerWalkMotionPath);
        _runPath = Existing(s.PlayerRunMotionPath);
        _jumpPath = Existing(s.PlayerJumpMotionPath);

        // Try to load the C3 model
        if (!string.IsNullOrEmpty(s.PlayerModelPath) &&
            System.IO.File.Exists(s.PlayerModelPath))
        {
            try
            {
                C3Texture.Initialize(GraphicsDevice);
                _c3 = new C3Renderer(GraphicsDevice) { Fps = 15f };

                string? texPath = Existing(s.PlayerTexturePath);
                _c3.LoadModel(s.PlayerModelPath,
                              texturePath: texPath,
                              worldRotation: C3WorldRotation);

                // Load initial motion (idle preferred, then walk, then model's own)
                string? initMotion = _idlePath ?? _walkPath;
                if (initMotion != null)
                    _c3.ChangeMotion(initMotion, C3WorldRotation);

                _lastState = MovementState.Idle;
                Debug.WriteLine("[PlayerComponent] C3 model loaded.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PlayerComponent] C3 load failed: {ex.Message}");
                _c3?.Dispose();
                _c3 = null;
            }
        }

        if (_c3 == null)
        {
            _effect = new BasicEffect(GraphicsDevice) { VertexColorEnabled = true };
            BuildMesh();
        }

        BuildShadow();
    }

    // ── Update ────────────────────────────────────────────────────────────────
    public override void Update(GameTime gt)
    {
        var input = _game.Input;
        var player = _game.Player;
        if (input == null || player == null) return;

        var s = _game.Settings;
        bool shift = input.IsHeld(Keys.LeftShift) || input.IsHeld(Keys.RightShift);
        bool ctrl = input.IsHeld(Keys.LeftControl) || input.IsHeld(Keys.RightControl);
        var viewer = _game.MapViewer;

        // ── Jump: Ctrl+Left-click  OR  Space ──────────────────────────────────
        bool jumpTriggered = (input.LeftClick && ctrl)
                          || input.IsPressed(Keys.Space);
        if (jumpTriggered && player.State != MovementState.Jumping)
        {
            // Ctrl+click: set destination so player jumps toward the clicked tile
            Vector2? jumpTarget = null;
            if (input.LeftClick && ctrl && viewer != null && !viewer.Camera.IsPanning)
            {
                jumpTarget = viewer.Camera.ViewportToCell(input.MousePosition);
                player.SetTarget(jumpTarget.Value);
            }

            float baseDur = EstimateMotionDuration(_jumpPath, fps: _c3?.Fps ?? 15f);
            // Pass target so Jump() can scale duration and arc with distance
            player.Jump(baseDur, jumpTarget);
        }
        // ── Left-click (no Ctrl) → click-to-move ──────────────────────────────
        else if (input.LeftClick && viewer != null && !viewer.Camera.IsPanning && !ctrl)
            player.SetTarget(viewer.Camera.ViewportToCell(input.MousePosition),
                             run: shift);

        // ── WASD / Arrows ──────────────────────────────────────────────────────
        var dir = Vector2.Zero;
        if (input.IsHeld(Keys.W) || input.IsHeld(Keys.Up)) dir.Y -= 1;
        if (input.IsHeld(Keys.S) || input.IsHeld(Keys.Down)) dir.Y += 1;
        if (input.IsHeld(Keys.A) || input.IsHeld(Keys.Left)) dir.X -= 1;
        if (input.IsHeld(Keys.D) || input.IsHeld(Keys.Right)) dir.X += 1;

        player.Update(dir, shift,
                      (float)gt.ElapsedGameTime.TotalSeconds,
                      s.PlayerWalkSpeed, s.PlayerRunSpeed);

        // ── Motion switching ──────────────────────────────────────────────────
        if (_c3 != null && player.State != _lastState)
        {
            SwitchMotion(player.State);
            _lastState = player.State;
        }

        _c3?.Update(gt);

        // ── Camera follow ─────────────────────────────────────────────────────
        if (viewer?.Camera is GameCamera cam && viewer.CoordinateSystem is { } cs)
        {
            if (!cam.IsPanning)
                cam.Follow(cs.MapToScreen(player.CellPosition));
            cam.TrackCell(player.CellPosition);
        }
    }

    // ── Draw ──────────────────────────────────────────────────────────────────
    public override void Draw(GameTime gt)
    {
        var player = _game.Player;
        var viewer = _game.MapViewer;
        var sb = _game.SpriteBatch;
        if (player == null || viewer == null || sb == null) return;
        if (!viewer.IsMapLoaded) return;

        var cam = viewer.Camera;
        var cs = viewer.CoordinateSystem;
        if (cs == null) return;

        // ── 1. Blob shadow ────────────────────────────────────────────────────
        var puzzlePx = cs.MapToScreen(player.CellPosition);
        var screenPos = new Vector2(
            (puzzlePx.X - cam.DrawWindow.X) * cam.Zoom,
            (puzzlePx.Y - cam.DrawWindow.Y) * cam.Zoom);

        sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend);
        sb.Draw(_shadow,
            new Rectangle((int)screenPos.X - 28, (int)screenPos.Y - 10, 56, 20),
            new Color(0, 0, 0, 90));
        sb.End();

        // ── 2. Restore 3-D state, clear depth ────────────────────────────────
        GraphicsDevice.BlendState = BlendState.AlphaBlend;
        GraphicsDevice.DepthStencilState = DepthStencilState.Default;
        GraphicsDevice.RasterizerState = RasterizerState.CullCounterClockwise;
        GraphicsDevice.SamplerStates[0] = SamplerState.LinearWrap;
        GraphicsDevice.Clear(ClearOptions.DepthBuffer, Color.Transparent, 1f, 0);

        // JumpHeight is computed by PlayerEntity using the Role.cpp two-phase sine arc.
        // It is already in cell units; scale by PlayerModelScale to match world space.
        float bob = player.State switch
        {
            MovementState.Jumping => player.JumpHeight,
            MovementState.Walking or MovementState.Running
                                  => MathF.Sin(player.WalkPhase) * 0.04f,
            _ => 0f
        };

        float scale = _game.Settings.PlayerModelScale;
        const float BASE_OFFSET = -MathF.PI / 4f;
        var faceRot = Matrix.CreateRotationY(-(player.FacingAngle + BASE_OFFSET));
        var world = Matrix.CreateScale(scale)
                    * faceRot
                    * Matrix.CreateTranslation(player.CellPosition.X, bob,
                                               player.CellPosition.Y);

        // ── 3a. C3 model ──────────────────────────────────────────────────────
        if (_c3 != null)
        {
            _c3.World = world;
            _c3.Draw(cam.ViewMatrix, cam.ProjectionMatrix);
            return;
        }

        // ── 3b. Procedural fallback ───────────────────────────────────────────
        if (_effect == null || _vb == null || _ib == null) return;

        _effect.View = cam.ViewMatrix;
        _effect.Projection = cam.ProjectionMatrix;
        _effect.World = world;

        GraphicsDevice.SetVertexBuffer(_vb);
        GraphicsDevice.Indices = _ib;
        foreach (var pass in _effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            GraphicsDevice.DrawIndexedPrimitives(
                PrimitiveType.TriangleList, 0, 0, _triCount);
        }
    }

    // ── Motion helpers ────────────────────────────────────────────────────────

    private void SwitchMotion(MovementState newState)
    {
        string? path = newState switch
        {
            MovementState.Idle => _idlePath,
            MovementState.Walking => _walkPath,
            MovementState.Running => _runPath,
            MovementState.Jumping => _jumpPath,
            _ => null
        };

        if (path == null) return; // keep current motion when no file configured

        try
        {
            _c3!.ChangeMotion(path, C3WorldRotation);
            Debug.WriteLine($"[PlayerComponent] Motion → {newState}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[PlayerComponent] ChangeMotion failed ({newState}): {ex.Message}");
        }
    }

    /// <summary>
    /// Estimate the duration of a motion clip in seconds by loading its frame count.
    /// Returns the <paramref name="fallback"/> value if the path is null or loading fails.
    /// </summary>
    private static float EstimateMotionDuration(string? path, float fps, float fallback = 0.6f)
    {
        if (path == null) return fallback;
        try
        {
            var src = C3Model.Load(path, loadTextures: false);
            int frames = src.MaxFrameCount;
            return frames > 0 ? frames / fps : fallback;
        }
        catch { return fallback; }
    }

    // ── Procedural humanoid mesh ───────────────────────────────────────────────
    private void BuildMesh()
    {
        var verts = new List<VertexPositionColor>();
        var indices = new List<short>();

        float[] lum = { 1.00f, 0.55f, 0.82f, 0.72f, 0.68f, 0.92f };

        void Box(Vector3 mn, Vector3 mx, Color col)
        {
            Vector3[] c =
            [
                new(mn.X,mn.Y,mn.Z),new(mx.X,mn.Y,mn.Z),new(mx.X,mx.Y,mn.Z),new(mn.X,mx.Y,mn.Z),
                new(mn.X,mn.Y,mx.Z),new(mx.X,mn.Y,mx.Z),new(mx.X,mx.Y,mx.Z),new(mn.X,mx.Y,mx.Z),
            ];
            int[][] faces = [[3, 2, 1, 0], [4, 5, 6, 7], [0, 1, 5, 4], [7, 6, 2, 3], [4, 7, 3, 0], [1, 2, 6, 5]];
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

    private static string? Existing(string? path) =>
        !string.IsNullOrEmpty(path) && System.IO.File.Exists(path) ? path : null;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _c3?.Dispose();
            _effect?.Dispose();
            _vb?.Dispose();
            _ib?.Dispose();
            _shadow?.Dispose();
            C3Texture.Texture_UnloadAll();
        }
        base.Dispose(disposing);
    }
}