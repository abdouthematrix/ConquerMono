using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace ConquerMono.C3Format
{
    // =========================================================================
    // PtclFrame  –  one animation frame of particle data
    // =========================================================================
    public class PtclFrame
    {
        public Vector3[] Positions;  // world-space particle centres
        public float[]   Ages;       // 0-1 normalised age (controls atlas tile)
        public float[]   Sizes;      // half-size in world units (billboard radius)
        public Matrix    FrameMatrix;// per-frame transform (applied before view)

        public int Count => Positions?.Length ?? 0;
    }

    // =========================================================================
    // C3Ptcl  –  pre-baked particle system
    //
    // Chunk tag: "PTCL"
    //
    // Particle quads are billboards in VIEW space:
    //   1. Transform position by (FrameMatrix × LocalMatrix × ViewMatrix)
    //   2. Build axis-aligned quad around the transformed position
    //   3. Render with world = InverseView (so quads face the camera)
    //
    // UV atlas: dwRow×dwRow grid; tile selected by floor(age × dwRow²)
    //
    // Blend modes are caller-supplied (nAsb/nAdb) matching Ptcl_Draw signature.
    // =========================================================================
    public class C3Ptcl : IDisposable
    {
        public string  Name    { get; set; } = string.Empty;
        public string  TexName { get; set; } = string.Empty;
        public int     TexIndex { get; set; } = -1;
        public int     TexRow   { get; set; } = 1;    // atlas grid dimension
        public int     MaxCount { get; set; }          // max particles per frame

        public PtclFrame[] Frames { get; set; }
        public int         CurrentFrame { get; set; }
        public Matrix      LocalMatrix  { get; set; } = Matrix.Identity;

        // CPU-side quad buffers (rebuilt every draw, matching original)
        private VertexPositionColorTexture[] _vb;
        private short[]                      _ib;

        // ------------------------------------------------------------------
        // Load one PTCL block  (Ptcl_Load, c3_ptcl.cpp)
        // stream must be positioned at the block start (after ChunkHeader consumed
        // by the calling C3Model / scene loader)
        // ------------------------------------------------------------------
        public static C3Ptcl Load(BinaryReader br, bool loadTexture = true)
        {
            var p = new C3Ptcl();

            // Name
            uint nameLen = br.ReadUInt32();
            p.Name = Encoding.ASCII.GetString(br.ReadBytes((int)nameLen)).TrimEnd('\0');

            // Texture name
            uint texLen = br.ReadUInt32();
            p.TexName = Encoding.ASCII.GetString(br.ReadBytes((int)texLen)).TrimEnd('\0');
            if (loadTexture)
                p.TexIndex = C3Texture.Texture_Load(p.TexName);

            p.TexRow   = (int)br.ReadUInt32();
            p.MaxCount = (int)br.ReadUInt32();

            // Allocate CPU quad buffers
            p._vb = new VertexPositionColorTexture[p.MaxCount * 4];
            p._ib = new short[p.MaxCount * 6];

            // Frames
            uint frameCount = br.ReadUInt32();
            p.Frames = new PtclFrame[frameCount];

            for (int n = 0; n < (int)frameCount; n++)
            {
                var frame = new PtclFrame();
                uint count = br.ReadUInt32();

                if (count > 0)
                {
                    frame.Positions = new Vector3[count];
                    for (int i = 0; i < (int)count; i++)
                        frame.Positions[i] = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());

                    frame.Ages = new float[count];
                    for (int i = 0; i < (int)count; i++) frame.Ages[i]  = br.ReadSingle();

                    frame.Sizes = new float[count];
                    for (int i = 0; i < (int)count; i++) frame.Sizes[i] = br.ReadSingle();

                    frame.FrameMatrix = C3Motion.ReadMatrix(br);
                }
                p.Frames[n] = frame;
            }

            return p;
        }

        // ------------------------------------------------------------------
        // Ptcl_Draw equivalent
        //
        // Builds view-aligned quads for the current frame, then draws them.
        //
        // blend: MonoGame BlendState (pass BlendState.Additive for fire/glow,
        //        BlendState.AlphaBlend for smoke etc.)
        //
        // world/view/projection: camera matrices from the renderer.
        //
        // IMPORTANT: The original engine sets D3DTS_WORLD = InverseView so that
        // the already-view-space quads render in world space.  We replicate this
        // by passing world = Matrix.Invert(view) to BasicEffect.
        // ------------------------------------------------------------------
        public void Draw(GraphicsDevice gd, BasicEffect effect,
                         Matrix view, Matrix projection,
                         BlendState blend = null)
        {
            if (Frames == null || Frames.Length == 0) return;

            var frame = Frames[CurrentFrame];
            if (frame.Count == 0) return;

            var tex = C3Texture.Get(TexIndex)?.Texture;

            int   segCount = TexRow * TexRow;
            float segSize  = 1f / TexRow;

            // Combined transform: FrameMatrix × LocalMatrix × View
            Matrix xform = frame.FrameMatrix * LocalMatrix * view;

            for (int n = 0; n < frame.Count; n++)
            {
                // Atlas tile from age
                int  tileIdx = (int)(frame.Ages[n] * segCount);
                tileIdx = Math.Clamp(tileIdx, 0, segCount - 1);
                float u = (tileIdx % TexRow) * segSize;
                float v = (tileIdx / TexRow) * segSize;

                // Transform position to view space
                Vector3 vpos = Vector3.Transform(frame.Positions[n], xform);
                float   s    = frame.Sizes[n];

                // Build axis-aligned quad (bottom-left, bottom-right, top-left, top-right)
                // Matching original vertex layout in Ptcl_Draw
                _vb[n*4+0] = new VertexPositionColorTexture(
                    new Vector3(vpos.X-s, vpos.Y-s, vpos.Z), Color.White,
                    new Vector2(u, v+segSize));
                _vb[n*4+1] = new VertexPositionColorTexture(
                    new Vector3(vpos.X+s, vpos.Y-s, vpos.Z), Color.White,
                    new Vector2(u+segSize, v+segSize));
                _vb[n*4+2] = new VertexPositionColorTexture(
                    new Vector3(vpos.X-s, vpos.Y+s, vpos.Z), Color.White,
                    new Vector2(u, v));
                _vb[n*4+3] = new VertexPositionColorTexture(
                    new Vector3(vpos.X+s, vpos.Y+s, vpos.Z), Color.White,
                    new Vector2(u+segSize, v));

                // Two-triangle quad (CCW winding)
                _ib[n*6+0]=(short)(n*4);   _ib[n*6+1]=(short)(n*4+1);
                _ib[n*6+2]=(short)(n*4+2); _ib[n*6+3]=(short)(n*4+2);
                _ib[n*6+4]=(short)(n*4+1); _ib[n*6+5]=(short)(n*4+3);
            }

            // World = InverseView (quads were built in view space)
            Matrix invView = Matrix.Invert(view);

            gd.BlendState        = blend ?? BlendState.AlphaBlend;
            gd.DepthStencilState = DepthStencilState.DepthRead;  // z-test ON, z-write OFF (Ptcl_Prepare)
            gd.RasterizerState   = RasterizerState.CullNone;
            gd.SamplerStates[0]  = SamplerState.LinearWrap;

            effect.View              = Matrix.Identity;   // already in view space
            effect.Projection        = projection;
            effect.World             = invView;
            effect.TextureEnabled    = tex != null;
            effect.Texture           = tex;
            effect.VertexColorEnabled = true;
            effect.LightingEnabled   = false;

            foreach (var pass in effect.CurrentTechnique.Passes)
            {
                pass.Apply();
                gd.DrawUserIndexedPrimitives(
                    PrimitiveType.TriangleList,
                    _vb, 0, frame.Count * 4,
                    _ib, 0, frame.Count * 2);
            }
        }

        // ------------------------------------------------------------------
        // Ptcl_NextFrame / Ptcl_SetFrame
        // ------------------------------------------------------------------
        public void NextFrame(int step = 1)
        {
            if (Frames != null && Frames.Length > 0)
                CurrentFrame = (CurrentFrame + step) % Frames.Length;
        }

        public void SetFrame(int frame)
        {
            if (Frames != null && Frames.Length > 0)
                CurrentFrame = frame % Frames.Length;
        }

        // Ptcl_Muliply: accumulate local matrix
        public void Multiply(Matrix matrix) { LocalMatrix = LocalMatrix * matrix; }

        // Ptcl_ClearMatrix
        public void ClearMatrix() { LocalMatrix = Matrix.Identity; }

        public void Dispose()
        {
            if (TexIndex != -1) { C3Texture.Texture_Unload(TexIndex); TexIndex = -1; }
        }
    }
}
