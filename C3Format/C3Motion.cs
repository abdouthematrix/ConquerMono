using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.Xna.Framework;

namespace ConquerMono.C3Format
{
    // =========================================================================
    // C3KeyFrame  –  one snapshot: timeline position + per-bone matrices
    // =========================================================================
    public class C3KeyFrame
    {
        public int         Pos         { get; set; }
        public List<Matrix> BoneMatrices { get; set; } = new List<Matrix>();
    }

    // =========================================================================
    // C3Motion  –  animation track for one PHY mesh
    //
    // Four on-disk keyframe formats (Motion_Load, c3_phy.cpp):
    //   KKEY  full 4x4 matrix, pos=DWORD
    //   ZKEY  quaternion + translation, pos=WORD
    //   XKEY  3x4 matrix (col4 implicit 0,0,0,1), pos=WORD
    //   Legacy no tag, bone-major dense layout
    // =========================================================================
    public class C3Motion
    {
        public int BoneCount    { get; set; }
        public int FrameCount   { get; set; }
        public int CurrentFrame { get; set; }
        public int MorphCount   { get; set; }

        public List<C3KeyFrame> KeyFrames  { get; } = new List<C3KeyFrame>();
        public List<Matrix>     BoneMatrix { get; } = new List<Matrix>(); // per-bone local (Identity after ClearMatrix)
        public List<float>      Morphs     { get; } = new List<float>();

        // ------------------------------------------------------------------
        // Factory: read one MOTI block (stream positioned after ChunkHeader)
        // ------------------------------------------------------------------
        public static C3Motion Load(BinaryReader br)
        {
            var m = new C3Motion();
            m.BoneCount  = (int)br.ReadUInt32();
            m.FrameCount = (int)br.ReadUInt32();

            for (int b = 0; b < m.BoneCount; b++)
                m.BoneMatrix.Add(Matrix.Identity);

            string kf = Encoding.ASCII.GetString(br.ReadBytes(4));
            if      (kf == "KKEY") m.ReadKKEY(br);
            else if (kf == "ZKEY") m.ReadZKEY(br);
            else if (kf == "XKEY") m.ReadXKEY(br);
            else                   m.ReadLegacy(br);

            m.MorphCount = (int)br.ReadUInt32();
            int total = m.MorphCount * m.FrameCount;
            for (int i = 0; i < total; i++) m.Morphs.Add(br.ReadSingle());

            return m;
        }

        // KKEY: full 4x4 per bone, pos=DWORD
        private void ReadKKEY(BinaryReader br)
        {
            int count = (int)br.ReadUInt32();
            for (int kk = 0; kk < count; kk++)
            {
                var kf = new C3KeyFrame { Pos = (int)br.ReadUInt32() };
                for (int b = 0; b < BoneCount; b++) kf.BoneMatrices.Add(ReadMatrix(br));
                KeyFrames.Add(kf);
            }
        }

        // ZKEY: quaternion(4f) + translation(3f) per bone, pos=WORD
        private void ReadZKEY(BinaryReader br)
        {
            int count = (int)br.ReadUInt32();
            for (int kk = 0; kk < count; kk++)
            {
                var kf = new C3KeyFrame { Pos = br.ReadUInt16() };
                for (int b = 0; b < BoneCount; b++)
                {
                    float qx=br.ReadSingle(), qy=br.ReadSingle(), qz=br.ReadSingle(), qw=br.ReadSingle();
                    float tx=br.ReadSingle(), ty=br.ReadSingle(), tz=br.ReadSingle();
                    var mat  = MatrixFromQuaternion(qx, qy, qz, qw);
                    mat.M41  = tx; mat.M42 = ty; mat.M43 = tz; mat.M44 = 1f;
                    kf.BoneMatrices.Add(mat);
                }
                KeyFrames.Add(kf);
            }
        }

        // XKEY: 3x4 matrix (12 floats) per bone, pos=WORD
        private void ReadXKEY(BinaryReader br)
        {
            int count = (int)br.ReadUInt32();
            for (int kk = 0; kk < count; kk++)
            {
                var kf = new C3KeyFrame { Pos = br.ReadUInt16() };
                for (int b = 0; b < BoneCount; b++)
                {
                    float m11=br.ReadSingle(), m12=br.ReadSingle(), m13=br.ReadSingle();
                    float m21=br.ReadSingle(), m22=br.ReadSingle(), m23=br.ReadSingle();
                    float m31=br.ReadSingle(), m32=br.ReadSingle(), m33=br.ReadSingle();
                    float m41=br.ReadSingle(), m42=br.ReadSingle(), m43=br.ReadSingle();
                    kf.BoneMatrices.Add(new Matrix(
                        m11,m12,m13,0f, m21,m22,m23,0f, m31,m32,m33,0f, m41,m42,m43,1f));
                }
                KeyFrames.Add(kf);
            }
        }

        // Legacy: no tag, bone-major dense storage
        private void ReadLegacy(BinaryReader br)
        {
            br.BaseStream.Seek(-4, SeekOrigin.Current);
            var frames = new List<C3KeyFrame>(FrameCount);
            for (int kk = 0; kk < FrameCount; kk++)
            {
                var kf = new C3KeyFrame { Pos = kk };
                for (int b = 0; b < BoneCount; b++) kf.BoneMatrices.Add(Matrix.Identity);
                frames.Add(kf);
                KeyFrames.Add(kf);
            }
            for (int b = 0; b < BoneCount; b++)
                for (int kk = 0; kk < FrameCount; kk++)
                    frames[kk].BoneMatrices[b] = ReadMatrix(br);
        }

        // ------------------------------------------------------------------
        // Motion_GetMatrix: interpolate bone matrix at CurrentFrame
        // ------------------------------------------------------------------
        public Matrix GetBoneMatrix(int boneIndex)
        {
            if (KeyFrames.Count == 0) return Matrix.Identity;
            int s = -1, e = -1;
            for (int n = 0; n < KeyFrames.Count; n++)
            {
                if (KeyFrames[n].Pos <= CurrentFrame) { if (s == -1 || n > s) s = n; }
                if (KeyFrames[n].Pos >  CurrentFrame) { if (e == -1 || n < e) e = n; }
            }
            if (s == -1) return KeyFrames[e].BoneMatrices[boneIndex];
            if (e == -1) return KeyFrames[s].BoneMatrices[boneIndex];
            float t = (float)(CurrentFrame - KeyFrames[s].Pos)
                    / (float)(KeyFrames[e].Pos - KeyFrames[s].Pos);
            return Matrix.Lerp(KeyFrames[s].BoneMatrices[boneIndex],
                               KeyFrames[e].BoneMatrices[boneIndex], t);
        }

        // Phy_NextFrame
        public void NextFrame(int step = 1)
        {
            if (FrameCount > 0) CurrentFrame = (CurrentFrame + step) % FrameCount;
        }

        // Phy_SetFrame
        public void SetFrame(int frame)
        {
            CurrentFrame = FrameCount > 0 ? frame % FrameCount : 0;
        }

        // Phy_ClearMatrix: reset all bone local matrices to Identity
        public void ClearMatrix()
        {
            for (int n = 0; n < BoneMatrix.Count; n++) BoneMatrix[n] = Matrix.Identity;
        }

        // Phy_Muliply: bone[n] = bone[n] * matrix  (boneIndex=-1 applies to all)
        public void Multiply(int boneIndex, Matrix matrix)
        {
            int start = boneIndex == -1 ? 0 : boneIndex;
            int end   = boneIndex == -1 ? BoneMatrix.Count : boneIndex + 1;
            for (int n = start; n < end; n++)
                BoneMatrix[n] = BoneMatrix[n] * matrix;
        }

        // ------------------------------------------------------------------
        // Binary helpers
        // ------------------------------------------------------------------

        /// Read 4x4 row-major matrix (64 bytes)
        public static Matrix ReadMatrix(BinaryReader br)
        {
            float[] v = new float[16];
            for (int i = 0; i < 16; i++) v[i] = br.ReadSingle();
            return new Matrix(
                v[0],  v[1],  v[2],  v[3],
                v[4],  v[5],  v[6],  v[7],
                v[8],  v[9],  v[10], v[11],
                v[12], v[13], v[14], v[15]);
        }

        /// Quaternion → rotation matrix (matches D3DXMatrixRotationQuaternion)
        public static Matrix MatrixFromQuaternion(float qx, float qy, float qz, float qw)
        {
            float xx=qx*qx, yy=qy*qy, zz=qz*qz;
            float xy=qx*qy, zw=qz*qw, zx=qz*qx, yw=qy*qw, yz=qy*qz, xw=qx*qw;
            return new Matrix(
                1f-2f*(yy+zz),  2f*(xy+zw),      2f*(zx-yw),      0f,
                2f*(xy-zw),     1f-2f*(zz+xx),   2f*(yz+xw),      0f,
                2f*(zx+yw),     2f*(yz-xw),       1f-2f*(yy+xx),  0f,
                0f,             0f,               0f,              1f);
        }
    }
}
