using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ConquerMono.Core;

namespace ConquerMono.Components;

/// <summary>
/// DrawableGameComponent that renders the player as a 3D mesh.
///
/// Rendering pipeline
/// ──────────────────
///  1. SpriteBatch pass — draw a soft ellipse shadow on the tile surface.
///  2. Clear depth buffer only (keep tile colours).
///  3. 3D BasicEffect pass — draw the procedural humanoid mesh.
///
/// The View/Projection matrices from <see cref="IsometricCamera"/> are derived
/// so that the mesh's world position (tileX, 0, tileY) projects to the same
/// screen pixel as the tile drawn by <see cref="MapComponent"/> at (tileX, tileY).
///
/// Mesh construction
/// ─────────────────
/// The character is built from axis-aligned boxes (VertexPositionColor).
/// Each of the 6 faces receives a pre-baked luminance multiplier that fakes
/// directional lighting without requiring shader changes.
/// </summary>
public sealed class PlayerComponent : DrawableGameComponent
{
    // ── Dependencies ──────────────────────────────────────────────────────────
    private readonly ConquerGame _game;

    // ── 3D resources ──────────────────────────────────────────────────────────
    private BasicEffect   _effect = null!;
    private VertexBuffer  _vb     = null!;
    private IndexBuffer   _ib     = null!;
    private int           _triCount;

    // ── 2D shadow resource ────────────────────────────────────────────────────
    private Texture2D _shadowTex = null!;

    // ── Walk-bob state ────────────────────────────────────────────────────────
    private float _bobPhase;

    public PlayerComponent(ConquerGame game) : base(game) => _game = game;

    // ─────────────────────────────────────────────────────────────────────────
    protected override void LoadContent()
    {
        _effect = new BasicEffect(GraphicsDevice)
        {
            VertexColorEnabled = true,
            LightingEnabled    = false,
        };

        BuildMesh();
        BuildShadowTexture();
    }

    // ── Draw ──────────────────────────────────────────────────────────────────
    public override void Draw(GameTime gameTime)
    {
        var cam    = _game.Camera;
        var player = _game.Player;

        // ── Pass 1: 2D shadow (SpriteBatch) ──────────────────────────────────
        Vector2 screenPos = cam.TileToScreen(player.TilePosition);
        var sb = _game.SpriteBatch;
        sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend);
        sb.Draw(_shadowTex,
            new Rectangle(
                (int)screenPos.X - 28,
                (int)screenPos.Y - 10,
                56, 20),
            new Color(0, 0, 0, 90));
        sb.End();

        // ── Pass 2: 3D mesh ────────────────────────────────────────────────────
        // Restore 3D render states (SpriteBatch alters them)
        GraphicsDevice.BlendState        = BlendState.AlphaBlend;
        GraphicsDevice.DepthStencilState = DepthStencilState.Default;
        GraphicsDevice.RasterizerState   = RasterizerState.CullCounterClockwise;
        GraphicsDevice.SamplerStates[0]  = SamplerState.LinearWrap;

        // Clear depth only — tile colours remain in the colour buffer
        GraphicsDevice.Clear(ClearOptions.DepthBuffer, Color.Transparent, 1f, 0);

        // Walk-bob: tiny vertical oscillation when moving
        _bobPhase = player.IsMoving
            ? _bobPhase + (float)gameTime.ElapsedGameTime.TotalSeconds * MathF.Tau * 2.2f
            : 0f;
        float bob = player.IsMoving ? MathF.Sin(_bobPhase) * 0.04f : 0f;

        _effect.View       = cam.ViewMatrix;
        _effect.Projection = cam.ProjectionMatrix;
        _effect.World      = Matrix.CreateTranslation(
            player.TilePosition.X,
            bob,
            player.TilePosition.Y);

        GraphicsDevice.SetVertexBuffer(_vb);
        GraphicsDevice.Indices = _ib;

        foreach (var pass in _effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            GraphicsDevice.DrawIndexedPrimitives(
                PrimitiveType.TriangleList,
                baseVertex: 0,
                startIndex: 0,
                primitiveCount: _triCount);
        }
    }

    // ── Mesh builder ──────────────────────────────────────────────────────────
    /// <summary>
    /// Builds a humanoid character from coloured boxes:
    ///   Y = 0   feet
    ///   Y = 1.9 top of helmet
    /// </summary>
    private void BuildMesh()
    {
        var verts   = new List<VertexPositionColor>();
        var indices = new List<short>();

        // Luminance per face (top, bottom, front-z, back+z, left-x, right+x)
        var lum = new float[] { 1.00f, 0.55f, 0.82f, 0.72f, 0.68f, 0.92f };

        void AddBox(Vector3 min, Vector3 max, Color col)
        {
            // 8 corners
            Vector3[] c =
            [
                new(min.X, min.Y, min.Z), // 0 LBF
                new(max.X, min.Y, min.Z), // 1 RBF
                new(max.X, max.Y, min.Z), // 2 RTF
                new(min.X, max.Y, min.Z), // 3 LTF
                new(min.X, min.Y, max.Z), // 4 LBK
                new(max.X, min.Y, max.Z), // 5 RBK
                new(max.X, max.Y, max.Z), // 6 RTK
                new(min.X, max.Y, max.Z), // 7 LTK
            ];

            // Face quad corner indices (CCW when viewed from outside)
            int[][] faces =
            [
                [3, 2, 1, 0], // top    (+Y)
                [4, 5, 6, 7], // bottom (−Y)
                [0, 1, 5, 4], // front  (−Z)
                [7, 6, 2, 3], // back   (+Z)
                [4, 7, 3, 0], // left   (−X)
                [1, 2, 6, 5], // right  (+X)
            ];

            for (int f = 0; f < 6; f++)
            {
                short v0 = (short)verts.Count;
                float l  = lum[f];
                Color fc = new((int)(col.R * l), (int)(col.G * l), (int)(col.B * l));

                foreach (int ci in faces[f])
                    verts.Add(new VertexPositionColor(c[ci], fc));

                // Two triangles per quad
                indices.AddRange([v0, (short)(v0+1), (short)(v0+2),
                                  v0, (short)(v0+2), (short)(v0+3)]);
            }
        }

        // ── Character parts ───────────────────────────────────────────────────
        // Y origin = feet.  All units are world-tiles (1 unit ≈ 1 tile width).

        // Left leg
        AddBox(new(-0.27f, 0.00f, -0.14f), new(-0.05f, 0.62f,  0.14f),
               new Color( 70,  55, 135));
        // Right leg
        AddBox(new( 0.05f, 0.00f, -0.14f), new( 0.27f, 0.62f,  0.14f),
               new Color( 70,  55, 135));
        // Torso / armour
        AddBox(new(-0.30f, 0.62f, -0.17f), new( 0.30f, 1.45f,  0.17f),
               new Color(185,  38,  38));
        // Shoulder pads
        AddBox(new(-0.38f, 1.20f, -0.15f), new(-0.28f, 1.45f,  0.15f),
               new Color(210, 170,  28));
        AddBox(new( 0.28f, 1.20f, -0.15f), new( 0.38f, 1.45f,  0.15f),
               new Color(210, 170,  28));
        // Left arm
        AddBox(new(-0.48f, 0.68f, -0.13f), new(-0.28f, 1.35f,  0.13f),
               new Color(160,  32,  32));
        // Right arm
        AddBox(new( 0.28f, 0.68f, -0.13f), new( 0.48f, 1.35f,  0.13f),
               new Color(160,  32,  32));
        // Neck
        AddBox(new(-0.10f, 1.45f, -0.10f), new( 0.10f, 1.56f,  0.10f),
               new Color(210, 170, 130));
        // Head (skin)
        AddBox(new(-0.21f, 1.56f, -0.19f), new( 0.21f, 1.95f,  0.19f),
               new Color(222, 178, 135));
        // Helmet
        AddBox(new(-0.23f, 1.88f, -0.21f), new( 0.23f, 2.05f,  0.21f),
               new Color(195, 155,  20));
        // Helmet crest
        AddBox(new(-0.05f, 2.04f, -0.04f), new( 0.05f, 2.22f,  0.04f),
               new Color(220,  40,  40));

        // ── Upload ────────────────────────────────────────────────────────────
        _vb = new VertexBuffer(GraphicsDevice,
                               VertexPositionColor.VertexDeclaration,
                               verts.Count,
                               BufferUsage.WriteOnly);
        _vb.SetData(verts.ToArray());

        _ib = new IndexBuffer(GraphicsDevice,
                              IndexElementSize.SixteenBits,
                              indices.Count,
                              BufferUsage.WriteOnly);
        _ib.SetData(indices.ToArray());

        _triCount = indices.Count / 3;
    }

    // ── Shadow texture ────────────────────────────────────────────────────────
    /// <summary>Soft radial-gradient ellipse used as a blob shadow on the tile.</summary>
    private void BuildShadowTexture()
    {
        const int W = 56, H = 20;
        var pixels = new Color[W * H];
        float cx = W * 0.5f, cy = H * 0.5f;

        for (int y = 0; y < H; y++)
        for (int x = 0; x < W; x++)
        {
            float dx = (x - cx) / cx;
            float dy = (y - cy) / cy;
            float d  = dx * dx + dy * dy;   // 0 at centre, 1 at edge of ellipse
            if (d < 1f)
            {
                int alpha = (int)((1f - d) * 180f);
                pixels[y * W + x] = new Color(0, 0, 0, alpha);
            }
        }

        _shadowTex = new Texture2D(GraphicsDevice, W, H);
        _shadowTex.SetData(pixels);
    }
}
