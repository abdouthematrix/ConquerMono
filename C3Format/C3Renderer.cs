using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ConquerMono.C3Format;

namespace ConquerMono
{
    // =========================================================================
    // PhyRenderData  –  GPU resources for one C3Phy mesh
    // =========================================================================
    public class PhyRenderData : IDisposable
    {
        public DynamicVertexBuffer VertexBuffer { get; private set; }
        public IndexBuffer IndexBuffer { get; private set; }
        public Texture2D Texture { get; set; }
        public C3Phy Phy { get; set; }   // settable: swapped by ReplacePhy

        public PhyRenderData(GraphicsDevice gd, C3Phy phy)
        {
            Phy = phy;
            Rebuild(gd);
        }

        /// <summary>
        /// Rebuild GPU buffers from current Phy.
        /// Called once on creation and again whenever the PHY is replaced.
        /// </summary>
        public void Rebuild(GraphicsDevice gd)
        {
            VertexBuffer?.Dispose();
            IndexBuffer?.Dispose();

            if (Phy.TotalVertexCount == 0 || Phy.TotalIndexCount == 0)
            {
                // Placeholder – no geometry yet; leave buffers null
                VertexBuffer = null;
                IndexBuffer = null;
                return;
            }

            VertexBuffer = new DynamicVertexBuffer(gd,
                VertexPositionColorTexture.VertexDeclaration,
                Phy.TotalVertexCount, BufferUsage.WriteOnly);

            IndexBuffer = new IndexBuffer(gd, IndexElementSize.SixteenBits,
                Phy.TotalIndexCount, BufferUsage.WriteOnly);
            IndexBuffer.SetData(Phy.IndexBuffer.ToArray());

            UploadVertices();
        }

        public void UploadVertices()
        {
            if (VertexBuffer == null) return;
            var v = Phy.BuildGpuVertices();
            if (v.Length > 0) VertexBuffer.SetData(v, 0, v.Length, SetDataOptions.Discard);
        }

        public void Dispose()
        {
            VertexBuffer?.Dispose();
            IndexBuffer?.Dispose();
        }
    }

    // =========================================================================
    // C3Renderer
    //
    // Draws ALL chunk types: PHY, SCEN, PTCL, SHAP.
    //
    // PHY REPLACEMENT:
    //   Subscribes to C3Model.PhyReplaced.  When a slot is replaced, the
    //   corresponding PhyRenderData is rebuilt (new VB/IB) and its texture
    //   is resolved from the new PHY's TexIndex or an explicit path.
    //
    // DRAW ORDER (matches original *_Prepare / *_Draw call order in the game):
    //   1. SCEN  – opaque static scene, z-write ON
    //   2. PHY   – skinned meshes, two-pass (opaque + alpha)
    //   3. PTCL  – particles, z-write OFF
    //   4. SHAP  – ribbon trails, z-write OFF
    // =========================================================================
    public class C3Renderer : IDisposable
    {
        private readonly GraphicsDevice _gd;
        private readonly BasicEffect _effect;
        private C3Model _model;
        private readonly List<PhyRenderData> _phyData = new();

        // Explicit texture override per PHY slot (set before LoadModel or via SetPhyTexture)
        private string _globalTexturePath;

        private double _frameTimer;
        private double _secondsPerFrame = 1.0 / 15.0;
        public bool IsPlaying { get; set; } = true;
        public float Fps
        {
            get => (float)(1.0 / _secondsPerFrame);
            set => _secondsPerFrame = value > 0 ? 1.0 / value : 1.0 / 15.0;
        }

        public Matrix World { get; set; } = Matrix.Identity;
        public C3Model Model => _model;

        /// <summary>
        /// Only PHY meshes whose name is in this set are drawn.
        /// Mirrors C3Model.VisibleMesh from the original engine.
        /// Default: {"v_body"} — call <see cref="AddVisibleMesh"/> to show
        /// additional parts (weapons, hair, etc.) after <see cref="ReplacePhy"/>.
        /// </summary>
        public HashSet<string> VisibleMeshNames { get; } =
            new(StringComparer.OrdinalIgnoreCase) { "v_body" };

        /// <summary>Add one or more mesh names to the visible set.</summary>
        public void AddVisibleMesh(params string[] names)
        {
            foreach (var n in names) VisibleMeshNames.Add(n);
        }

        /// <summary>Remove a mesh name from the visible set.</summary>
        public void RemoveVisibleMesh(string name) => VisibleMeshNames.Remove(name);

        // -----------------------------------------------------------------------
        public C3Renderer(GraphicsDevice gd)
        {
            _gd = gd;
            _effect = new BasicEffect(gd)
            {
                LightingEnabled = false,
                VertexColorEnabled = true,
                TextureEnabled = true,
            };
        }

        // -----------------------------------------------------------------------
        // LoadModel
        // -----------------------------------------------------------------------
        public void LoadModel(string c3FilePath, string texturePath = null,
                              Matrix? worldRotation = null)
        {
            // Unsubscribe from old model before releasing
            if (_model != null) _model.PhyReplaced -= OnPhyReplaced;

            foreach (var rd in _phyData) rd.Dispose();
            _phyData.Clear();

            _globalTexturePath = texturePath;
            _model = C3Model.Load(c3FilePath, loadTextures: true, gd: _gd);

            // Subscribe to PHY replacement notifications
            _model.PhyReplaced += OnPhyReplaced;

            if (worldRotation.HasValue)
                foreach (var phy in _model.Phys)
                    if (phy.Motion != null)
                    {
                        phy.Motion.ClearMatrix();
                        phy.Motion.Multiply(-1, worldRotation.Value);
                    }

            _model.Calculate();

            string dir = Path.GetDirectoryName(c3FilePath);
            string name = Path.GetFileNameWithoutExtension(c3FilePath);

            foreach (var phy in _model.Phys)
            {
                var rd = new PhyRenderData(_gd, phy);
                rd.Texture = ResolvePhyTexture(phy, texturePath, dir, name);
                _phyData.Add(rd);
            }
        }

        // -----------------------------------------------------------------------
        // ReplacePhy  –  hot-swap a named placeholder with real geometry
        //
        // Delegates to C3Model.ReplacePhy which raises PhyReplaced → OnPhyReplaced
        // rebuilds the GPU buffers automatically.
        // -----------------------------------------------------------------------
        public bool ReplacePhy(string targetName, string sourcePath,
                               string sourceMeshName = "v_body", string texturePath = null, bool useSourceMotion = false)
        {
            if (_model == null) return false;
            return _model.ReplacePhy(targetName, sourcePath, sourceMeshName, texturePath, useSourceMotion);
        }

        // -----------------------------------------------------------------------
        // OnPhyReplaced  –  called by C3Model.PhyReplaced event
        // Rebuilds GPU buffers and re-resolves texture for the affected slot.
        // -----------------------------------------------------------------------
        private void OnPhyReplaced(int slot)
        {
            if (slot < 0 || slot >= _phyData.Count) return;

            var rd = _phyData[slot];
            var phy = _model.Phys[slot];

            rd.Phy = phy;
            rd.Rebuild(_gd);

            // Re-resolve texture: explicit override → TexIndex set by ReplacePhy → auto-search
            rd.Texture = null;

            if (phy.TexIndex != -1)
            {
                rd.Texture = C3Texture.Get(phy.TexIndex)?.Texture;
            }

            if (rd.Texture == null)
            {
                string dir = Path.GetDirectoryName(_model.SourcePath);
                string name = Path.GetFileNameWithoutExtension(phy.TexName);
                string found = FindTexture(dir, name);
                if (found != null) rd.Texture = LoadTexture(found);
            }
        }

        // -----------------------------------------------------------------------
        // SetPhyTexture  –  override the texture for a named PHY at any time
        // -----------------------------------------------------------------------
        public void SetPhyTexture(string phyName, string texturePath)
        {
            int slot = _model?.Phys.FindIndex(p =>
                string.Equals(p.Name, phyName, StringComparison.OrdinalIgnoreCase)) ?? -1;
            if (slot == -1 || slot >= _phyData.Count) return;
            _phyData[slot].Texture = LoadTexture(texturePath);
        }

        // -----------------------------------------------------------------------
        // ChangeMotion
        // -----------------------------------------------------------------------
        public void ChangeMotion(string motionFilePath, Matrix? worldRotation = null)
        {
            if (_model == null) return;
            _frameTimer = 0;
            _model.ChangeMotion(motionFilePath, worldRotation ?? Matrix.Identity);
            _model.Calculate();
            foreach (var rd in _phyData) if (rd.Phy.Draw) rd.UploadVertices();
        }

        // -----------------------------------------------------------------------
        // Update
        // -----------------------------------------------------------------------
        public void Update(GameTime gameTime)
        {
            if (_model == null) return;

            if (IsPlaying)
            {
                _frameTimer += gameTime.ElapsedGameTime.TotalSeconds;
                while (_frameTimer >= _secondsPerFrame)
                {
                    _model.AdvanceFrame(1);
                    _model.Calculate();
                    _model.UpdateShapes();
                    _frameTimer -= _secondsPerFrame;
                }
            }

            foreach (var rd in _phyData)
                if (rd.Phy.Draw && rd.VertexBuffer != null &&
                    VisibleMeshNames.Contains(rd.Phy.Name))
                    rd.UploadVertices();
        }

        // -----------------------------------------------------------------------
        // Draw  –  all chunk types in correct order
        // -----------------------------------------------------------------------
        public void Draw(Matrix view, Matrix projection)
        {
            if (_model == null) return;

            _gd.SamplerStates[0] = SamplerState.LinearWrap;

            DrawScene(view, projection);
            DrawPhy(view, projection);
            DrawPtcl(view, projection);
            DrawShape(view, projection);
        }

        // -----------------------------------------------------------------------
        // DrawScene  (Scene_Prepare / Scene_Draw)
        // D3DCULL_CW → CullCounterClockwise, z-write ON
        // Lightmap: second additive pass (MODULATE2X approximation)
        // -----------------------------------------------------------------------
        private void DrawScene(Matrix view, Matrix projection)
        {
            if (_model.Scenes.Count == 0) return;

            _effect.VertexColorEnabled = false;
            _effect.LightingEnabled = false;

            foreach (var scene in _model.Scenes)
                scene.Draw(_gd, _effect, view, projection);
        }

        // -----------------------------------------------------------------------
        // DrawPhy  (Phy_Prepare / Phy_DrawNormal / Phy_DrawAlpha)
        //
        // Pass 1 opaque : Alpha == 1.0, z-write ON, CullCounterClockwise
        // Pass 2 alpha  : Alpha < 1.0 OR AlphaTriCount > 0, z-write ON, CullNone
        //   Sub A: transparent normal tris   startIndex = 0
        //   Sub B: dedicated alpha tris      startIndex = NormalTriCount * 3
        // -----------------------------------------------------------------------
        private void DrawPhy(Matrix view, Matrix projection)
        {
            if (_phyData.Count == 0) return;

            _gd.DepthStencilState = DepthStencilState.Default;
            _effect.View = view;
            _effect.Projection = projection;
            _effect.World = World;
            _effect.VertexColorEnabled = true;
            _effect.LightingEnabled = false;

            // ── Pass 1: Opaque ───────────────────────────────────────────
            _gd.BlendState = BlendState.Opaque;

            foreach (var rd in _phyData)
            {
                var phy = rd.Phy;
                if (!VisibleMeshNames.Contains(phy.Name)) continue;
                if (!phy.Draw || phy.NormalTriCount == 0 || !phy.IsFullyOpaque) continue;
                if (rd.VertexBuffer == null) continue;   // placeholder – no geometry yet

                _gd.RasterizerState = phy.TwoSided
                    ? RasterizerState.CullNone
                    : RasterizerState.CullCounterClockwise;

                SetPhyEffect(rd);
                _gd.SetVertexBuffer(rd.VertexBuffer);
                _gd.Indices = rd.IndexBuffer;

                foreach (var pass in _effect.CurrentTechnique.Passes)
                {
                    pass.Apply();
                    _gd.DrawIndexedPrimitives(PrimitiveType.TriangleList,
                        baseVertex: 0, startIndex: 0,
                        primitiveCount: phy.NormalTriCount);
                }
            }

            // ── Pass 2: Alpha ────────────────────────────────────────────
            _gd.BlendState = BlendState.AlphaBlend;
            _gd.RasterizerState = RasterizerState.CullNone;

            foreach (var rd in _phyData)
            {
                var phy = rd.Phy;
                if (!VisibleMeshNames.Contains(phy.Name)) continue;
                if (!phy.Draw || rd.VertexBuffer == null) continue;

                bool transNormals = phy.NormalTriCount > 0 && !phy.IsFullyOpaque;
                bool hasAlphaTris = phy.AlphaTriCount > 0;
                if (!transNormals && !hasAlphaTris) continue;

                SetPhyEffect(rd);
                _gd.SetVertexBuffer(rd.VertexBuffer);
                _gd.Indices = rd.IndexBuffer;

                foreach (var pass in _effect.CurrentTechnique.Passes)
                {
                    pass.Apply();
                    if (transNormals)
                        _gd.DrawIndexedPrimitives(PrimitiveType.TriangleList,
                            baseVertex: 0, startIndex: 0,
                            primitiveCount: phy.NormalTriCount);
                    if (hasAlphaTris)
                        _gd.DrawIndexedPrimitives(PrimitiveType.TriangleList,
                            baseVertex: 0, startIndex: phy.AlphaIndexStart,
                            primitiveCount: phy.AlphaTriCount);
                }
            }
        }

        // -----------------------------------------------------------------------
        // DrawPtcl  (Ptcl_Prepare / Ptcl_Draw)
        // D3DCULL_NONE, z-test ON, z-write OFF, alpha-blend
        // -----------------------------------------------------------------------
        private void DrawPtcl(Matrix view, Matrix projection)
        {
            if (_model.Ptcls.Count == 0) return;
            foreach (var ptcl in _model.Ptcls)
                ptcl.Draw(_gd, _effect, view, projection, BlendState.AlphaBlend);
        }

        // -----------------------------------------------------------------------
        // DrawShape  (Shape_Prepare / Shape_Draw)
        // D3DCULL_NONE, z-write OFF, AlphaBlend
        // -----------------------------------------------------------------------
        private void DrawShape(Matrix view, Matrix projection)
        {
            if (_model.Shapes.Count == 0) return;
            foreach (var shape in _model.Shapes)
                shape.Draw(_gd, _effect, view, projection);
        }

        // -----------------------------------------------------------------------
        // StepFrame
        // -----------------------------------------------------------------------
        public void StepFrame(int delta)
        {
            if (_model == null) return;
            _model.AdvanceFrame(delta);
            _model.Calculate();
            _model.UpdateShapes();
            foreach (var rd in _phyData)
                if (rd.Phy.Draw && rd.VertexBuffer != null &&
                    VisibleMeshNames.Contains(rd.Phy.Name))
                    rd.UploadVertices();
        }

        // -----------------------------------------------------------------------
        // Private helpers
        // -----------------------------------------------------------------------

        private void SetPhyEffect(PhyRenderData rd)
        {
            var phy = rd.Phy;
            bool hasTex = rd.Texture != null;
            _effect.TextureEnabled = hasTex;
            _effect.VertexColorEnabled = true;
            _effect.Texture = hasTex ? rd.Texture : null;
            _effect.DiffuseColor = new Vector3(phy.R, phy.G, phy.B);
            _effect.Alpha = phy.Alpha;
        }

        /// Resolve a texture for a PHY: explicit path → TexIndex in cache → auto-search
        private Texture2D ResolvePhyTexture(C3Phy phy, string explicitPath,
                                             string dir, string baseName)
        {
            if (explicitPath != null && File.Exists(explicitPath))
                return LoadTexture(explicitPath);

            if (phy.TexIndex != -1)
            {
                var cached = C3Texture.Get(phy.TexIndex)?.Texture;
                if (cached != null) return cached;
            }

            string found = FindTexture(dir, baseName)
                        ?? FindTexture(dir, Path.GetFileNameWithoutExtension(phy.TexName));
            return found != null ? LoadTexture(found) : null;
        }

        private static string FindTexture(string dir, string baseName)
        {
            if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(baseName)) return null;
            foreach (string ext in new[] { ".dds", ".tga", ".png", ".jpg", ".jpeg" })
            {
                string p = Path.Combine(dir, baseName + ext);
                if (File.Exists(p)) return p;
            }
            return null;
        }

        private Texture2D LoadTexture(string path)
        {
            try
            {
                return Path.GetExtension(path).ToLowerInvariant() switch
                {
                    ".dds" => DDSLoader.Load(_gd, path),
                    ".tga" => TGALoader.Load(_gd, path),
                    _ => LoadStream(path),
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[C3Renderer] '{path}': {ex.Message}");
                var t = new Texture2D(_gd, 1, 1);
                t.SetData(new[] { Color.Magenta });
                return t;
            }
        }

        private Texture2D LoadStream(string p)
        { using var s = File.OpenRead(p); return Texture2D.FromStream(_gd, s); }

        public void Dispose()
        {
            if (_model != null) _model.PhyReplaced -= OnPhyReplaced;
            foreach (var rd in _phyData) rd.Dispose();
            _effect?.Dispose();
        }
    }
}