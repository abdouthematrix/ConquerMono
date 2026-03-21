using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace ConquerMono.C3Format
{
    // =========================================================================
    // C3SMotion  –  simple matrix-per-frame animation track for shapes
    //
    // Binary: dwFrames (DWORD) + dwFrames × 4×4 Matrix
    // Chunk tag: "SMOT"
    // =========================================================================
    public class C3SMotion
    {
        public Matrix[] Frames       { get; set; }
        public int      CurrentFrame { get; set; }
        public Matrix   LocalMatrix  { get; set; } = Matrix.Identity;

        public int FrameCount => Frames?.Length ?? 0;

        public static C3SMotion Load(BinaryReader br)
        {
            var m = new C3SMotion();
            int count = (int)br.ReadUInt32();
            m.Frames = new Matrix[count];
            for (int i = 0; i < count; i++)
                m.Frames[i] = C3Motion.ReadMatrix(br);
            return m;
        }

        public void NextFrame(int step = 1)
        {
            if (FrameCount > 0) CurrentFrame = (CurrentFrame + step) % FrameCount;
        }

        public void SetFrame(int frame) { CurrentFrame = frame; }
        public void ClearMatrix()       { LocalMatrix = Matrix.Identity; }
        public void Multiply(Matrix m)  { LocalMatrix = LocalMatrix * m; }

        /// Current world matrix for this frame (FrameMatrix × LocalMatrix)
        public Matrix GetWorldMatrix(bool applyLocal = true)
        {
            if (Frames == null || Frames.Length == 0) return Matrix.Identity;
            Matrix fm = Frames[Math.Clamp(CurrentFrame, 0, Frames.Length-1)];
            return applyLocal ? fm * LocalMatrix : fm;
        }
    }

    // =========================================================================
    // C3Line  –  one spline / control-point line inside a shape
    // =========================================================================
    public class C3Line
    {
        public Vector3[] Points { get; set; }
    }

    // =========================================================================
    // ShapeOutVertex  –  one output vertex for the ribbon/blade strip
    // =========================================================================
    public struct ShapeOutVertex
    {
        public Vector3 Position;
        public Color   Color;
        public Vector2 UV;
    }

    // =========================================================================
    // C3Shape  –  animated ribbon/blade effect (sword trail, etc.)
    //
    // Chunk tag: "SHAP"
    //
    // How it works:
    //   Each frame, Shape_Draw is called with the current world-space endpoints
    //   of the blade (vec[0] and vec[1]).  The shape maintains a ring-buffer of
    //   dwSegment quad-segments.  New segments are interpolated (dwSmooth=10
    //   sub-steps) from the previous position.  UVs fade from 1→0 along the trail.
    //
    //   Shape_DrawAlpha uses the same geometry but also copies the backbuffer to a
    //   refraction texture (D3D8 CopyRects).  That path is NOT ported here because
    //   MonoGame/DX11 requires a RenderTarget copy instead.  A TODO stub is left.
    //
    // Blend states:
    //   Shape_Prepare: AlphaBlend (SrcAlpha / InvSrcAlpha)
    //   z-write OFF (shape renders after scene)
    // =========================================================================
    public class C3Shape : IDisposable
    {
        public string   Name    { get; set; } = string.Empty;
        public string   TexName { get; set; } = string.Empty;
        public int      TexIndex { get; set; } = -1;

        public C3Line[]   Lines  { get; set; }
        public C3SMotion  Motion { get; set; }

        // Ring-buffer of rendered segments (dwSegment × 6 vertices per segment)
        private ShapeOutVertex[] _vb;
        private int   _segCount;     // total ring-buffer capacity
        private int   _segCur;       // write cursor in ring buffer
        private bool  _isFirst = true;
        private const int SMOOTH = 10;

        // Previous frame's two endpoints (for interpolation)
        private Vector3 _lastA = Vector3.Zero;
        private Vector3 _lastB = Vector3.Zero;

        // ------------------------------------------------------------------
        // Shape_Load  (c3_shape.cpp)
        // ------------------------------------------------------------------
        public static C3Shape Load(BinaryReader br, bool loadTexture = true)
        {
            var s = new C3Shape();

            uint nameLen = br.ReadUInt32();
            s.Name = Encoding.ASCII.GetString(br.ReadBytes((int)nameLen)).TrimEnd('\0');

            // Lines (spline control-point sets)
            uint lineCount = br.ReadUInt32();
            s.Lines = new C3Line[lineCount];
            for (int n = 0; n < (int)lineCount; n++)
            {
                uint vecCount = br.ReadUInt32();
                var line = new C3Line { Points = new Vector3[vecCount] };
                for (int v = 0; v < (int)vecCount; v++)
                    line.Points[v] = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
                s.Lines[n] = line;
            }

            // Texture
            uint texLen = br.ReadUInt32();
            s.TexName = Encoding.ASCII.GetString(br.ReadBytes((int)texLen)).TrimEnd('\0');
            if (loadTexture)
                s.TexIndex = C3Texture.Texture_Load(s.TexName);

            // Segment count (before dwSmooth expansion)
            uint seg = br.ReadUInt32();
            s.SetSegment((int)seg);

            return s;
        }

        // ------------------------------------------------------------------
        // Shape_SetSegment  (c3_shape.cpp line 339)
        // dwSmooth is hardcoded to 10 in the original.
        // Expands the segment count and allocates the ring buffer.
        // ------------------------------------------------------------------
        public void SetSegment(int seg)
        {
            _segCount = seg * (SMOOTH + 1);
            _segCur   = 0;
            _vb       = new ShapeOutVertex[_segCount * 6];
            for (int i = 0; i < _vb.Length; i++) _vb[i] = default;
        }

        // ------------------------------------------------------------------
        // Shape_Draw  (c3_shape.cpp line 358)
        //
        // Called once per frame.  bLocal controls whether the SMotion local
        // matrix is folded into the frame matrix.
        //
        // Writes new geometry into the ring buffer and draws the full trail.
        // ------------------------------------------------------------------
        public void Update(bool bLocal = false)
        {
            if (Motion == null || Lines == null || Lines.Length == 0) return;

            // Get current world-space blade endpoints
            Matrix mm = Motion.GetWorldMatrix(!bLocal);

            Vector3 vecA = Vector3.Transform(Lines[0].Points[0], mm);
            Vector3 vecB = Vector3.Transform(Lines[0].Points.Length > 1
                ? Lines[0].Points[1] : Lines[0].Points[0], mm);

            if (_isFirst)
            {
                // First frame: zero the buffer, record endpoints
                Array.Clear(_vb, 0, _vb.Length);
                _isFirst = false;
            }
            else
            {
                float len = Vector3.Distance(vecA, vecB);

                // Write SMOOTH interpolated sub-segments then the final segment
                Vector3 prevA = _lastA, prevB = _lastB;

                for (int nn = 0; nn < SMOOTH; nn++)
                {
                    float t = (nn + 1f) / (SMOOTH + 1f);
                    Vector3 sA = Vector3.Lerp(_lastA, vecA, t);
                    Vector3 sB = Vector3.Lerp(_lastB, vecB, t);

                    // Preserve blade width
                    float lnow = Vector3.Distance(sA, sB);
                    if (lnow > 0.0001f)
                        sA = Vector3.Lerp(sB, sA, len / lnow);

                    WriteSegment(sA, sB, prevA, prevB);
                    prevA = sA; prevB = sB;
                }
                WriteSegment(vecA, vecB, prevA, prevB);

                // Recompute UV fade along the full trail
                UpdateUVs();
            }

            _lastA = vecA;
            _lastB = vecB;
        }

        // ------------------------------------------------------------------
        // Draw: render the current trail ring buffer
        // blend state matches Shape_Prepare: AlphaBlend, z-write OFF
        // ------------------------------------------------------------------
        public void Draw(GraphicsDevice gd, BasicEffect effect,
                         Matrix view, Matrix projection, bool bLocal = false)
        {
            if (_vb == null || _segCount == 0) return;

            var tex = C3Texture.Get(TexIndex)?.Texture;

            Matrix world = bLocal
                ? (Motion?.LocalMatrix ?? Matrix.Identity)
                : Matrix.Identity;

            gd.BlendState        = BlendState.AlphaBlend;
            gd.DepthStencilState = DepthStencilState.DepthRead;  // z-write OFF
            gd.RasterizerState   = RasterizerState.CullNone;
            gd.SamplerStates[0]  = SamplerState.LinearWrap;

            effect.View              = view;
            effect.Projection        = projection;
            effect.World             = world;
            effect.TextureEnabled    = tex != null;
            effect.Texture           = tex;
            effect.VertexColorEnabled = true;
            effect.LightingEnabled   = false;

            // Convert ShapeOutVertex → VertexPositionColorTexture for draw call
            int total = _segCount * 6;
            var gpu   = new VertexPositionColorTexture[total];
            for (int i = 0; i < total; i++)
                gpu[i] = new VertexPositionColorTexture(
                    _vb[i].Position, _vb[i].Color, _vb[i].UV);

            foreach (var pass in effect.CurrentTechnique.Passes)
            {
                pass.Apply();
                gd.DrawUserPrimitives(PrimitiveType.TriangleList, gpu, 0, _segCount * 2);
            }
        }

        // Shape_NextFrame / Shape_SetFrame / Shape_ClearMatrix / Shape_Muliply
        public void NextFrame(int step=1) => Motion?.NextFrame(step);
        public void SetFrame(int frame)   => Motion?.SetFrame(frame);
        public void ClearMatrix()         => Motion?.ClearMatrix();
        public void Multiply(Matrix m)    => Motion?.Multiply(m);

        // ------------------------------------------------------------------
        // Private helpers
        // ------------------------------------------------------------------

        private void WriteSegment(Vector3 a, Vector3 b,
                                  Vector3 prevA, Vector3 prevB)
        {
            int cur = _segCur * 6;
            // Two triangles forming a quad: A-B-prevB  prevA-A-prevB
            // (matches original CCW triangle layout in Shape_Draw)
            _vb[cur+0] = new ShapeOutVertex { Position=a,     Color=Color.White };
            _vb[cur+1] = new ShapeOutVertex { Position=b,     Color=Color.White };
            _vb[cur+2] = new ShapeOutVertex { Position=prevB, Color=Color.White };
            _vb[cur+3] = new ShapeOutVertex { Position=prevA, Color=Color.White };
            _vb[cur+4] = new ShapeOutVertex { Position=prevB, Color=Color.White };
            _vb[cur+5] = new ShapeOutVertex { Position=a,     Color=Color.White };

            _segCur = (_segCur + 1) % _segCount;
        }

        private void UpdateUVs()
        {
            // UV fade: newest segment u=1, oldest u=0  (matching original uvstep logic)
            float uvStep = 0.9f / _segCount;
            float u      = (float)_segCount * uvStep + 0.05f;

            // Walk backwards from _segCur-1 to _segCur (ring-buffer order newest→oldest)
            for (int n = _segCur - 1; n >= 0; n--)
            {
                SetSegmentUV(n, u, uvStep);
                u -= uvStep;
            }
            for (int n = _segCount - 1; n > _segCur; n--)
            {
                SetSegmentUV(n, u, uvStep);
                u -= uvStep;
            }
        }

        private void SetSegmentUV(int seg, float u, float step)
        {
            int b = seg * 6;
            _vb[b+0].UV = new Vector2(u,      0);
            _vb[b+1].UV = new Vector2(u,      1);
            _vb[b+5].UV = new Vector2(u,      0);
            u -= step;
            _vb[b+2].UV = new Vector2(u, 1);
            _vb[b+3].UV = new Vector2(u, 0);
            _vb[b+4].UV = new Vector2(u, 1);
        }

        public void Dispose()
        {
            if (TexIndex != -1) { C3Texture.Texture_Unload(TexIndex); TexIndex = -1; }
        }
    }
}
