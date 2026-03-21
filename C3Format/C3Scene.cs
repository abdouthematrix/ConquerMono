using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace ConquerMono.C3Format
{
    // =========================================================================
    // SceneVertex  –  one vertex in a static scene mesh
    //
    // Original FVF (SCENE_VERTEX): position(3f) + normal(3f) + 2xUV(2f each)
    // We store both UV sets for lightmap support.
    // =========================================================================
    public struct SceneVertex
    {
        public Vector3 Position;
        public Vector3 Normal;
        public Vector2 UV0;    // diffuse texture coords
        public Vector2 UV1;    // lightmap coords

        public static readonly VertexDeclaration VertexDeclaration = new VertexDeclaration(
            new VertexElement(0,  VertexElementFormat.Vector3, VertexElementUsage.Position,           0),
            new VertexElement(12, VertexElementFormat.Vector3, VertexElementUsage.Normal,             0),
            new VertexElement(24, VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate,  0),
            new VertexElement(32, VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate,  1));

        public const int SizeInBytes = 40;
    }

    // =========================================================================
    // C3Scene  –  static scene mesh with optional lightmap and animation frames
    //
    // Chunk tag: "SCEN"
    // Features:
    //   Diffuse texture  (nTex)
    //   Lightmap texture (nlTex, optional)
    //   Frame array: per-frame world matrices (dwFrameCount × Matrix)
    //
    // Scene_Prepare render states:
    //   D3DCULL_CW → CullCounterClockwise in MonoGame
    //   Z-write ON, alpha-blend OFF by default (opaque scene geo)
    //   Alpha-blend ON if diffuse texture has alpha channel
    //
    // Lightmap stage (Scene_Draw):
    //   If nlTex > -1: second texture stage uses MODULATE2X (brightening)
    //   This is approximated in MonoGame by blending two BasicEffect draw calls.
    // =========================================================================
    public class C3Scene : IDisposable
    {
        public string  Name       { get; set; } = string.Empty;
        public string  TexName    { get; set; } = string.Empty;
        public string  LightTexName { get; set; }  // null = no lightmap
        public int     TexIndex   { get; set; } = -1;
        public int     LightTexIndex { get; set; } = -1;

        public SceneVertex[] Vertices   { get; set; }
        public ushort[]      Indices    { get; set; }
        public Matrix[]      Frames     { get; set; }  // per-frame world matrices

        public int   CurrentFrame { get; set; }
        public Matrix ExtraMatrix { get; set; } = Matrix.Identity;  // Scene_Muliply accumulator

        // GPU resources
        private VertexBuffer _vb;
        private IndexBuffer  _ib;
        private Texture2D    _tex;
        private Texture2D    _lightTex;

        // ------------------------------------------------------------------
        // Load: find dwIndex-th "SCEN" chunk in file  (Scene_Load)
        // ------------------------------------------------------------------
        public static C3Scene Load(string filePath, uint dwIndex = 0,
                                   bool loadTextures = true, GraphicsDevice gd = null)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException(filePath);

            using var fs = File.OpenRead(filePath);
            using var br = new BinaryReader(fs, Encoding.ASCII, leaveOpen: true);

            uint found    = 0;
            long fileSize = fs.Length;

            while (fs.Position < fileSize)
            {
                if (fileSize - fs.Position < 8) break;
                var  chunk    = ChunkHeader.Read(br);
                long chunkEnd = fs.Position + chunk.ChunkSize;

                if (chunk.Tag == "SCEN")
                {
                    if (found < dwIndex)
                    { found++; fs.Seek(chunk.ChunkSize, SeekOrigin.Current); continue; }

                    var scene = new C3Scene();

                    // Name
                    uint nameLen = br.ReadUInt32();
                    scene.Name = Encoding.ASCII.GetString(br.ReadBytes((int)nameLen)).TrimEnd('\0');

                    // Vertex buffer
                    uint vecCount = br.ReadUInt32();
                    scene.Vertices = new SceneVertex[vecCount];
                    for (int i = 0; i < (int)vecCount; i++)
                    {
                        scene.Vertices[i].Position = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
                        scene.Vertices[i].Normal   = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
                        scene.Vertices[i].UV0      = new Vector2(br.ReadSingle(), br.ReadSingle());
                        scene.Vertices[i].UV1      = new Vector2(br.ReadSingle(), br.ReadSingle());
                    }

                    // Index buffer
                    uint triCount = br.ReadUInt32();
                    scene.Indices = new ushort[triCount * 3];
                    for (int i = 0; i < triCount * 3; i++)
                        scene.Indices[i] = br.ReadUInt16();

                    // Diffuse texture name
                    uint texLen = br.ReadUInt32();
                    scene.TexName = Encoding.ASCII.GetString(br.ReadBytes((int)texLen)).TrimEnd('\0');
                    if (loadTextures && gd != null)
                        scene.TexIndex = C3Texture.Texture_Load(scene.TexName);

                    // Lightmap texture name (0 length = none)
                    uint lmLen = br.ReadUInt32();
                    if (lmLen > 0)
                    {
                        scene.LightTexName = Encoding.ASCII.GetString(br.ReadBytes((int)lmLen)).TrimEnd('\0');
                        if (loadTextures && gd != null)
                            scene.LightTexIndex = C3Texture.Texture_Load(scene.LightTexName);
                    }

                    // Frame matrices
                    uint frameCount = br.ReadUInt32();
                    scene.Frames = new Matrix[frameCount];
                    for (int i = 0; i < (int)frameCount; i++)
                        scene.Frames[i] = C3Motion.ReadMatrix(br);

                    // Upload to GPU if device provided
                    if (gd != null) scene.UploadGPU(gd);

                    return scene;
                }
                else
                    fs.Seek(chunk.ChunkSize, SeekOrigin.Current);

                if (fs.Position > chunkEnd) fs.Seek(chunkEnd, SeekOrigin.Begin);
            }

            throw new InvalidDataException($"SCEN chunk {dwIndex} not found in '{filePath}'.");
        }

        // ------------------------------------------------------------------
        // Upload vertex and index data to GPU buffers
        // ------------------------------------------------------------------
        public void UploadGPU(GraphicsDevice gd)
        {
            _vb?.Dispose();
            _ib?.Dispose();

            if (Vertices == null || Vertices.Length == 0) return;

            _vb = new VertexBuffer(gd, SceneVertex.VertexDeclaration,
                Vertices.Length, BufferUsage.WriteOnly);
            _vb.SetData(Vertices);

            _ib = new IndexBuffer(gd, IndexElementSize.SixteenBits,
                Indices.Length, BufferUsage.WriteOnly);
            _ib.SetData(Indices);

            // Cache texture references
            _tex      = C3Texture.Get(TexIndex)?.Texture;
            _lightTex = C3Texture.Get(LightTexIndex)?.Texture;
        }

        // ------------------------------------------------------------------
        // Scene_NextFrame
        // ------------------------------------------------------------------
        public void NextFrame(int step = 1)
        {
            if (Frames != null && Frames.Length > 0)
                CurrentFrame = (CurrentFrame + step) % Frames.Length;
        }

        // ------------------------------------------------------------------
        // Scene_Muliply: accumulate extra world transform
        // ------------------------------------------------------------------
        public void Multiply(Matrix matrix)
        {
            ExtraMatrix = ExtraMatrix * matrix;
        }

        // ------------------------------------------------------------------
        // Draw (Scene_Draw equivalent)
        //
        // Render states (Scene_Prepare):
        //   D3DCULL_CW  → CullCounterClockwise in MonoGame
        //   Z-write ON, alpha-blend only when texture has alpha
        //   Lightmap: MODULATE2X on stage 1 (approximated as second BasicEffect pass)
        //
        // After drawing, Scene_Draw resets matrix to Identity (matching original).
        // ------------------------------------------------------------------
        public void Draw(GraphicsDevice gd, BasicEffect effect,
                         Matrix view, Matrix projection)
        {
            if (_vb == null || Indices == null || Indices.Length == 0) return;

            // World matrix = current frame matrix × extra accumulator
            Matrix frameMatrix = (Frames != null && Frames.Length > 0)
                ? Frames[CurrentFrame] : Matrix.Identity;
            Matrix world = frameMatrix * ExtraMatrix;

            bool hasAlpha = _tex != null &&
                (_tex.Format == SurfaceFormat.Dxt3 || _tex.Format == SurfaceFormat.Dxt5);

            gd.DepthStencilState = DepthStencilState.Default;   // z-write ON
            gd.RasterizerState   = RasterizerState.CullCounterClockwise; // D3DCULL_CW
            gd.BlendState        = hasAlpha ? BlendState.AlphaBlend : BlendState.Opaque;
            gd.SamplerStates[0]  = SamplerState.LinearWrap;

            effect.View            = view;
            effect.Projection      = projection;
            effect.World           = world;
            effect.TextureEnabled  = _tex != null;
            effect.Texture         = _tex;
            effect.LightingEnabled = false;
            effect.VertexColorEnabled = false;

            gd.SetVertexBuffer(_vb);
            gd.Indices = _ib;

            foreach (var pass in effect.CurrentTechnique.Passes)
            {
                pass.Apply();
                gd.DrawIndexedPrimitives(PrimitiveType.TriangleList,
                    baseVertex: 0, startIndex: 0,
                    primitiveCount: Indices.Length / 3);
            }

            // Lightmap pass: MODULATE2X approximated as additive-ish second pass
            if (_lightTex != null)
            {
                gd.BlendState   = BlendState.Additive;
                effect.Texture  = _lightTex;
                effect.TextureEnabled = true;
                foreach (var pass in effect.CurrentTechnique.Passes)
                { pass.Apply(); gd.DrawIndexedPrimitives(PrimitiveType.TriangleList,
                    baseVertex:0, startIndex:0, primitiveCount:Indices.Length/3); }
                gd.BlendState = BlendState.Opaque;
            }

            // Scene_Draw resets matrix to Identity after each draw call
            ExtraMatrix = Matrix.Identity;
        }

        public void Dispose()
        {
            _vb?.Dispose();
            _ib?.Dispose();
            if (TexIndex      != -1) { C3Texture.Texture_Unload(TexIndex);      TexIndex      = -1; }
            if (LightTexIndex != -1) { C3Texture.Texture_Unload(LightTexIndex); LightTexIndex = -1; }
        }
    }
}
