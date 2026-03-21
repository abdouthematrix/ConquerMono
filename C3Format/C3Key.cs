using System.Collections.Generic;
using System.IO;

namespace ConquerMono.C3Format
{
    // =========================================================================
    // C3Frame  –  one keyframe entry (16 bytes, matches C++ struct exactly)
    //
    //   int   nFrame    4 bytes  timeline position
    //   float fParam    4 bytes  alpha value
    //   bool  bParam    1 byte   draw flag
    //   [pad]           3 bytes  alignment
    //   int   nParam    4 bytes  texture atlas index
    // =========================================================================
    public class C3Frame
    {
        public int   NFrame { get; set; }
        public float FParam { get; set; }
        public bool  BParam { get; set; }
        public int   NParam { get; set; }

        public static C3Frame Read(BinaryReader br)
        {
            var f    = new C3Frame();
            f.NFrame = br.ReadInt32();
            f.FParam = br.ReadSingle();
            f.BParam = br.ReadBoolean();
            br.ReadBytes(3);               // 3-byte alignment pad
            f.NParam = br.ReadInt32();
            return f;
        }
    }

    // =========================================================================
    // C3Key  –  three per-mesh keyframe tracks
    // All Process* methods are exact ports of c3_key.cpp.
    // =========================================================================
    public class C3Key
    {
        public List<C3Frame> Alphas     { get; } = new List<C3Frame>();
        public List<C3Frame> Draws      { get; } = new List<C3Frame>();
        public List<C3Frame> ChangeTexs { get; } = new List<C3Frame>();

        // Key_ProcessAlpha: linear interpolation between bracketing keyframes
        public (bool found, float value) ProcessAlpha(int frame, int totalFrames)
        {
            int s = -1, e = -1;
            for (int n = 0; n < Alphas.Count; n++)
            {
                if (Alphas[n].NFrame <= frame) { if (s == -1 || n > s) s = n; }
                if (Alphas[n].NFrame >  frame) { if (e == -1 || n < e) e = n; }
            }
            if (s == -1 && e > -1) return (true, Alphas[e].FParam);
            if (s > -1 && e == -1) return (true, Alphas[s].FParam);
            if (s > -1 && e > -1)
            {
                float t = (float)(frame - Alphas[s].NFrame)
                        / (float)(Alphas[e].NFrame - Alphas[s].NFrame);
                return (true, Alphas[s].FParam + t * (Alphas[e].FParam - Alphas[s].FParam));
            }
            return (false, 0f);
        }

        // Key_ProcessDraw: exact-frame lookup, no interpolation
        public (bool found, bool visible) ProcessDraw(int frame)
        {
            foreach (var d in Draws)
                if (d.NFrame == frame) return (true, d.BParam);
            return (false, false);
        }

        // Key_ProcessChangeTex: exact-frame lookup, no interpolation
        public (bool found, int texIndex) ProcessChangeTex(int frame)
        {
            foreach (var c in ChangeTexs)
                if (c.NFrame == frame) return (true, c.NParam);
            return (false, 0);
        }
    }
}
