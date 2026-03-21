using System;
using System.IO;
using Microsoft.Xna.Framework.Graphics;

namespace ConquerMono.C3Format
{
    /// <summary>
    /// TGA loader – supports type 2 (uncompressed) and type 10 (RLE).
    /// 24-bit BGR and 32-bit BGRA input → RGBA for MonoGame SurfaceFormat.Color.
    /// Automatically flips vertically when origin is bottom-left (TGA default).
    /// </summary>
    public static class TGALoader
    {
        public static Texture2D Load(GraphicsDevice device, string filePath)
        {
            using var fs = File.OpenRead(filePath);
            using var br = new BinaryReader(fs);
            return Load(device, br);
        }

        public static Texture2D Load(GraphicsDevice device, BinaryReader br)
        {
            // Header (18 bytes)
            byte idLen     = br.ReadByte();
            byte colorMap  = br.ReadByte();
            byte imgType   = br.ReadByte();
            br.ReadBytes(5);                // color map spec (unused)
            br.ReadUInt16();                // originX
            br.ReadUInt16();                // originY
            ushort width   = br.ReadUInt16();
            ushort height  = br.ReadUInt16();
            byte   bpp     = br.ReadByte();
            byte   desc    = br.ReadByte();

            if (idLen > 0) br.ReadBytes(idLen);

            if (imgType != 2 && imgType != 10)
                throw new NotSupportedException($"TGA type {imgType} unsupported (need 2 or 10).");
            if (bpp != 24 && bpp != 32)
                throw new NotSupportedException($"TGA {bpp}bpp unsupported (need 24 or 32).");

            int pixels  = width * height;
            int bytesPP = bpp / 8;

            byte[] raw = imgType == 2
                ? br.ReadBytes(pixels * bytesPP)
                : ReadRLE(br, pixels, bytesPP);

            // BGR(A) → RGBA
            var rgba = new byte[pixels * 4];
            for (int i = 0; i < pixels; i++)
            {
                int s=i*bytesPP, d=i*4;
                rgba[d+0] = raw[s+2];   // R ← B[2] (TGA stores BGR)
                rgba[d+1] = raw[s+1];   // G
                rgba[d+2] = raw[s+0];   // B ← R[0]
                rgba[d+3] = bytesPP==4 ? raw[s+3] : (byte)255;
            }

            // Vertical flip when origin is bottom-left (desc bit5=0)
            if ((desc & 0x20) == 0) FlipV(rgba, width, height);

            var tex = new Texture2D(device, width, height, false, SurfaceFormat.Color);
            tex.SetData(rgba);
            return tex;
        }

        private static byte[] ReadRLE(BinaryReader br, int pixels, int bpp)
        {
            var buf = new byte[pixels * bpp];
            int pos = 0;
            while (pos < buf.Length)
            {
                byte pkt   = br.ReadByte();
                int  count = (pkt & 0x7F) + 1;
                if ((pkt & 0x80) != 0)
                {
                    byte[] px = br.ReadBytes(bpp);
                    for (int i = 0; i < count && pos < buf.Length; i++)
                    { Array.Copy(px, 0, buf, pos, bpp); pos += bpp; }
                }
                else
                {
                    byte[] raw = br.ReadBytes(count * bpp);
                    int copy   = Math.Min(raw.Length, buf.Length - pos);
                    Array.Copy(raw, 0, buf, pos, copy);
                    pos += count * bpp;
                }
            }
            return buf;
        }

        private static void FlipV(byte[] data, int width, int height)
        {
            int row  = width * 4;
            var tmp  = new byte[row];
            for (int y = 0; y < height / 2; y++)
            {
                int top = y * row, bot = (height-1-y) * row;
                Array.Copy(data, top, tmp,  0,   row);
                Array.Copy(data, bot, data, top, row);
                Array.Copy(tmp,  0,   data, bot, row);
            }
        }
    }
}
