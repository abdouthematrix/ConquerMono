using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace ConquerMono.C3Format
{
    public static class C3Constants
    {
        public const int BoneMax  = 2;
        public const int MorphMax = 4;
    }

    // =========================================================================
    // PhyVertex  –  raw vertex as stored in the .c3 file
    // =========================================================================
    public class PhyVertex
    {
        public Vector3[] Positions;   // [morphMax]
        public Vector2   TexCoord;
        public int[]     BoneIndex;   // [BoneMax]
        public float[]   BoneWeight;  // [BoneMax]

        public PhyVertex(int morphMax)
        {
            Positions  = new Vector3[morphMax];
            BoneIndex  = new int  [C3Constants.BoneMax];
            BoneWeight = new float[C3Constants.BoneMax];
        }
    }

    // =========================================================================
    // PhyOutVertex  –  CPU-skinned output (rebuilt every frame)
    // =========================================================================
    public class PhyOutVertex
    {
        public Vector3 Position = Vector3.Zero;
        public Vector2 TexCoord = Vector2.Zero;
    }

    // =========================================================================
    // C3Phy  –  one mesh inside a .c3 file
    //
    // VERTEX COLOR:
    //   The original engine stores vertex.color = WHITE in the output VB always.
    //   Material tint (fR,fG,fB,fA) is applied via D3D SetMaterial, not vertices.
    //   We replicate this: BuildGpuVertices() outputs WHITE vertex colour,
    //   and C3Renderer sets effect.DiffuseColor = (R,G,B) / effect.Alpha = Alpha.
    //
    // INDEX BUFFER LAYOUT:
    //   [0 .. NormalTriCount*3-1]       normal triangle indices
    //   [NormalTriCount*3 .. end]        alpha  triangle indices
    //   Alpha indices are ABSOLUTE vertex positions (NormalVertexCount + n)
    // =========================================================================
    public class C3Phy
    {
        public string Name     { get; set; } = string.Empty;
        public string TexName  { get; set; } = string.Empty;
        public int    TexIndex { get; set; } = -1;

        public int NormalVertexCount { get; set; }
        public int AlphaVertexCount  { get; set; }
        public int NormalTriCount    { get; set; }
        public int AlphaTriCount     { get; set; }
        public int BlendCount        { get; set; }

        public List<PhyVertex>    SourceVertices { get; } = new List<PhyVertex>();
        public List<PhyOutVertex> OutputVertices { get; } = new List<PhyOutVertex>();
        public List<ushort>       IndexBuffer    { get; } = new List<ushort>();

        public Vector3  BBoxMin    { get; set; }
        public Vector3  BBoxMax    { get; set; }
        public Matrix   InitMatrix { get; set; } = Matrix.Identity;
        public C3Motion Motion     { get; set; }
        public C3Key    Key        { get; set; } = new C3Key();

        // Material tint – exposed so C3Renderer can forward to effect params
        public float Alpha { get; set; } = 1f;
        public float R     { get; set; } = 1f;
        public float G     { get; set; } = 1f;
        public float B     { get; set; } = 1f;
        public bool  Draw  { get; set; } = true;

        public int     TexRow  { get; set; } = 1;
        public Vector2 UVStep  { get; set; } = Vector2.Zero;
        private Vector2 _accumUV = Vector2.Zero;   // UV scroll accumulator (persists like original output VB)

        public bool TwoSided     { get; set; } = false;
        public bool IsFullyOpaque => Alpha == 1f;   // matches original fA == 1.0f check

        // ------------------------------------------------------------------
        // Phy_Load  (c3_phy.cpp line 450)
        // ------------------------------------------------------------------
        public static C3Phy Load(BinaryReader br, string chunkTag)
        {
            var phy = new C3Phy();

            uint nameLen = br.ReadUInt32();
            phy.Name = Encoding.ASCII.GetString(br.ReadBytes((int)nameLen)).TrimEnd('\0');

            phy.BlendCount        = (int)br.ReadUInt32();
            phy.NormalVertexCount = (int)br.ReadUInt32();
            phy.AlphaVertexCount  = (int)br.ReadUInt32();

            int totalVerts = phy.NormalVertexCount + phy.AlphaVertexCount;
            int morphMax   = (chunkTag == "PHY3" || chunkTag == "PHY4") ? 1 : C3Constants.MorphMax;

            for (int i = 0; i < totalVerts; i++)
            {
                var v = new PhyVertex(morphMax);
                for (int m = 0; m < morphMax; m++)
                    v.Positions[m] = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());

                v.TexCoord = new Vector2(br.ReadSingle(), br.ReadSingle());
                br.ReadBytes(4);   // DWORD vertex diffuse colour (always white in original, skipped)

                for (int b = 0; b < C3Constants.BoneMax; b++) v.BoneIndex[b]  = (int)br.ReadUInt32();
                for (int b = 0; b < C3Constants.BoneMax; b++) v.BoneWeight[b] = br.ReadSingle();

                if (chunkTag == "PHY3") br.ReadBytes(12);  // vertex normal (unused)

                phy.SourceVertices.Add(v);
                phy.OutputVertices.Add(new PhyOutVertex
                {
                    Position = v.Positions[0],
                    TexCoord = v.TexCoord,
                });
            }

            phy.NormalTriCount = (int)br.ReadUInt32();
            phy.AlphaTriCount  = (int)br.ReadUInt32();
            int totalIdx       = (phy.NormalTriCount + phy.AlphaTriCount) * 3;
            for (int i = 0; i < totalIdx; i++) phy.IndexBuffer.Add(br.ReadUInt16());

            uint texLen   = br.ReadUInt32();
            byte[] txb    = br.ReadBytes((int)texLen);
            try   { phy.TexName = Encoding.GetEncoding("GBK").GetString(txb).TrimEnd('\0'); }
            catch { phy.TexName = Encoding.ASCII.GetString(txb).TrimEnd('\0'); }

            phy.BBoxMin    = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
            phy.BBoxMax    = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
            phy.InitMatrix = C3Motion.ReadMatrix(br);
            phy.TexRow     = (int)br.ReadUInt32();

            int ac = (int)br.ReadUInt32(); for (int i = 0; i < ac; i++) phy.Key.Alphas.Add(C3Frame.Read(br));
            int dc = (int)br.ReadUInt32(); for (int i = 0; i < dc; i++) phy.Key.Draws.Add(C3Frame.Read(br));
            int cc = (int)br.ReadUInt32(); for (int i = 0; i < cc; i++) phy.Key.ChangeTexs.Add(C3Frame.Read(br));

            // STEP tag
            byte[] f1 = br.ReadBytes(4);
            if (Encoding.ASCII.GetString(f1) == "STEP")
                phy.UVStep = new Vector2(br.ReadSingle(), br.ReadSingle());
            else
                br.BaseStream.Seek(-4, SeekOrigin.Current);

            // 2SID tag
            byte[] f2 = br.ReadBytes(4);
            if (Encoding.ASCII.GetString(f2) == "2SID") phy.TwoSided = true;
            else br.BaseStream.Seek(-4, SeekOrigin.Current);

            return phy;
        }

        // ------------------------------------------------------------------
        // Phy_Calculate  (c3_phy.cpp line 1050)
        //
        // 1. Key track queries update Alpha, Draw, tex
        // 2. bone[b] = InitMatrix * GetBoneMatrix(b) * BoneMatrix[b]
        // 3. Position: first positive-weight bone only (original break)
        // 4. UV scroll: output_uv += uvstep each frame (accumulates like original output VB)
        //    ChangeTex: uv = source_uv + atlas_offset (reads fresh from source)
        // ------------------------------------------------------------------
        public void Calculate()
        {
            if (Motion == null) return;

            var (aFound, alpha) = Key.ProcessAlpha(Motion.CurrentFrame, Motion.FrameCount);
            if (aFound) Alpha = alpha;

            var (dFound, visible) = Key.ProcessDraw(Motion.CurrentFrame);
            if (dFound) Draw = visible;

            var (tFound, tex) = Key.ProcessChangeTex(Motion.CurrentFrame);
            if (!tFound) tex = -1;

            if (!Draw) return;

            // bone[b] = InitMatrix * keyframeMat * localMat
            var bone = new Matrix[Motion.BoneCount];
            for (int b = 0; b < Motion.BoneCount; b++)
                bone[b] = InitMatrix * Motion.GetBoneMatrix(b) * Motion.BoneMatrix[b];

            // UV scroll accumulate (mirrors: vertex[v].u += uvstep.x each frame)
            _accumUV += UVStep;

            float seg = TexRow > 0 ? 1f / TexRow : 1f;

            for (int v = 0; v < SourceVertices.Count; v++)
            {
                var sv = SourceVertices[v];

                // Position: first positive-weight bone (original break logic, c3_phy.cpp line 1168)
                Vector3 pos = Vector3.Zero;
                for (int l = 0; l < C3Constants.BoneMax; l++)
                {
                    if (sv.BoneWeight[l] > 0f)
                    {
                        pos = Vector3.Transform(sv.Positions[0], bone[sv.BoneIndex[l]]);
                        break;
                    }
                }
                OutputVertices[v].Position = pos;

                // UV (c3_phy.cpp lines 1177-1185)
                if (tex > -1)
                    OutputVertices[v].TexCoord = new Vector2(
                        sv.TexCoord.X + (tex % TexRow) * seg,
                        sv.TexCoord.Y + (tex / TexRow) * seg);
                else
                    OutputVertices[v].TexCoord = sv.TexCoord + _accumUV;
            }
        }

        // ------------------------------------------------------------------
        // Build GPU vertex array for upload.
        // Vertex colour is always WHITE – tint is applied by C3Renderer via
        // effect.DiffuseColor / effect.Alpha, matching the original D3D material pipeline.
        // ------------------------------------------------------------------
        public VertexPositionColorTexture[] BuildGpuVertices()
        {
            var verts = new VertexPositionColorTexture[OutputVertices.Count];
            for (int i = 0; i < OutputVertices.Count; i++)
                verts[i] = new VertexPositionColorTexture(
                    OutputVertices[i].Position,
                    Color.White,
                    OutputVertices[i].TexCoord);
            return verts;
        }

        public int TotalVertexCount => NormalVertexCount + AlphaVertexCount;
        public int TotalIndexCount  => (NormalTriCount + AlphaTriCount) * 3;
        public int AlphaIndexStart  => NormalTriCount * 3;
    }
}
