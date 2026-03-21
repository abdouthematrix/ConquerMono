using System;
using System.IO;
using Microsoft.Xna.Framework.Graphics;

namespace ConquerMono.C3Format
{
    /// <summary>
    /// DDS texture loader for DXT1/DXT3/DXT5 compressed and 32/24-bit uncompressed.
    ///
    /// TEXTURE BUG FIX – BGRA swizzle:
    ///   D3D8/D3D9 stores A8R8G8B8 uncompressed DDS in memory as B,G,R,A (byte order).
    ///   MonoGame SurfaceFormat.Color expects R,G,B,A.
    ///   Without this swap, red and blue are exchanged producing wrong colours.
    ///   Alpha (byte 3) is in the same position in both layouts → transparency unaffected.
    ///   DXT1/3/5 are block-compressed; same layout everywhere; no swizzle needed.
    /// </summary>
    public static class DDSLoader
    {
        private const uint DDS_MAGIC        = 0x20534444;
        private const uint DDSD_MIPMAPCOUNT = 0x00020000;
        private const uint DDPF_FOURCC      = 0x00000004;
        private const uint DDPF_RGB         = 0x00000040;
        private const uint FOURCC_DX10      = 0x30315844;
        private const uint FOURCC_DXT1      = 0x31545844;
        private const uint FOURCC_DXT2      = 0x32545844;
        private const uint FOURCC_DXT3      = 0x33545844;
        private const uint FOURCC_DXT4      = 0x34545844;
        private const uint FOURCC_DXT5      = 0x35545844;

        public static Texture2D Load(GraphicsDevice device, string filePath)
        {
            using var fs = File.OpenRead(filePath);
            using var br = new BinaryReader(fs);
            return Load(device, br);
        }

        public static Texture2D Load(GraphicsDevice device, BinaryReader br)
        {
            if (br.ReadUInt32() != DDS_MAGIC)
                throw new InvalidDataException("Not a DDS file.");

            // DDS_HEADER (124 bytes)
            br.ReadUInt32();             // dwSize = 124
            uint flags   = br.ReadUInt32();
            uint height  = br.ReadUInt32();
            uint width   = br.ReadUInt32();
            br.ReadUInt32();             // dwPitchOrLinearSize
            br.ReadUInt32();             // dwDepth
            uint mipCount = br.ReadUInt32();
            br.ReadBytes(44);            // dwReserved1[11]

            // DDS_PIXELFORMAT (32 bytes)
            br.ReadUInt32();             // dwSize = 32
            uint pfFlags = br.ReadUInt32();
            uint fourCC  = br.ReadUInt32();
            uint bitCnt  = br.ReadUInt32();
            uint rMask   = br.ReadUInt32();
            uint gMask   = br.ReadUInt32();
            uint bMask   = br.ReadUInt32();
            uint aMask   = br.ReadUInt32();

            br.ReadBytes(16);            // dwCaps[4]
            br.ReadUInt32();             // dwReserved2

            // DX10 extended header: skip (C3 assets predate DX10)
            if ((pfFlags & DDPF_FOURCC) != 0 && fourCC == FOURCC_DX10)
                br.ReadBytes(20);

            // Detect surface format
            SurfaceFormat fmt = DetectFormat(pfFlags, fourCC, bitCnt);

            bool hasMips  = (flags & DDSD_MIPMAPCOUNT) != 0 && mipCount > 1;
            int  levels   = hasMips ? (int)mipCount : 1;
            bool is24bit  = (pfFlags & DDPF_RGB) != 0 && bitCnt == 24;

            // BGRA swizzle: only for 32-bit uncompressed BGRA / BGRX on-disk layout
            // bMask=0x000000FF means Blue is at byte 0 → BGRA layout → needs swap
            bool swizzle  = (pfFlags & DDPF_RGB) != 0 && bitCnt == 32
                            && bMask == 0x000000FF && rMask == 0x00FF0000;

            var texture = new Texture2D(device, (int)width, (int)height, hasMips, fmt);

            int w = (int)width, h = (int)height;
            for (int lvl = 0; lvl < levels; lvl++)
            {
                int rawSize = is24bit ? w * h * 3 : CalcSize(w, h, fmt);
                byte[] raw  = br.ReadBytes(rawSize);
                if (raw.Length < rawSize) break;   // truncated

                byte[] data;
                if      (is24bit) data = Expand24to32(raw, w * h, bMask == 0x000000FF);
                else if (swizzle) data = SwizzleBGRA(raw);
                else              data = raw;

                texture.SetData(lvl, null, data, 0, data.Length);
                w = Math.Max(1, w / 2);
                h = Math.Max(1, h / 2);
            }

            return texture;
        }

        private static SurfaceFormat DetectFormat(uint pfFlags, uint fourCC, uint bitCnt)
        {
            if ((pfFlags & DDPF_FOURCC) != 0)
            {
                return fourCC switch
                {
                    FOURCC_DXT1 => SurfaceFormat.Dxt1,
                    FOURCC_DXT2 => SurfaceFormat.Dxt3,
                    FOURCC_DXT3 => SurfaceFormat.Dxt3,
                    FOURCC_DXT4 => SurfaceFormat.Dxt5,
                    FOURCC_DXT5 => SurfaceFormat.Dxt5,
                    _           => SurfaceFormat.Color,
                };
            }
            return SurfaceFormat.Color;
        }

        private static int CalcSize(int w, int h, SurfaceFormat fmt) => fmt switch
        {
            SurfaceFormat.Dxt1 or SurfaceFormat.Dxt1a
                => Math.Max(1,(w+3)/4) * Math.Max(1,(h+3)/4) * 8,
            SurfaceFormat.Dxt3 or SurfaceFormat.Dxt5
                => Math.Max(1,(w+3)/4) * Math.Max(1,(h+3)/4) * 16,
            _   => w * h * 4,
        };

        // Swap R↔B in every 4-byte BGRA pixel; A at byte 3 is untouched
        private static byte[] SwizzleBGRA(byte[] src)
        {
            var dst = (byte[])src.Clone();
            for (int i = 0; i + 3 < dst.Length; i += 4)
            { byte t = dst[i]; dst[i] = dst[i+2]; dst[i+2] = t; }
            return dst;
        }

        // Expand 24-bit BGR or RGB → 32-bit RGBA (alpha = 255)
        private static byte[] Expand24to32(byte[] src, int pixels, bool isBGR)
        {
            var dst = new byte[pixels * 4];
            for (int i = 0; i < pixels; i++)
            {
                int s=i*3, d=i*4;
                if (isBGR) { dst[d]=src[s+2]; dst[d+1]=src[s+1]; dst[d+2]=src[s+0]; }
                else       { dst[d]=src[s+0]; dst[d+1]=src[s+1]; dst[d+2]=src[s+2]; }
                dst[d+3] = 255;
            }
            return dst;
        }
    }
}
