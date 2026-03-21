namespace ConquerMono.Infrastructure.Graphics;

public static class DDSHelper
{
    private const uint DDS_MAGIC       = 0x20534444u; // "DDS "
    private const uint DDSD_MIPMAPCOUNT = 0x00020000u;
    private const uint DDPF_FOURCC     = 0x00000004u;
    private const uint DDPF_RGB        = 0x00000040u;

    public static int GetWidth(Stream stream)
    {
        stream.Seek(16, SeekOrigin.Begin);
        using var r = new BinaryReader(stream, Encoding.Default, leaveOpen: true);
        return r.ReadInt32();
    }

    public static Texture2D LoadFromStream(Stream stream, GraphicsDevice device) =>
        Load(device, stream);

    public static Texture2D Load(GraphicsDevice device, Stream fs)
    {
        using var r = new BinaryReader(fs);
        return Load(device, r);
    }

    public static Texture2D Load(GraphicsDevice device, BinaryReader r)
    {
        if (r.ReadUInt32() != DDS_MAGIC)
            throw new InvalidDataException("Not a DDS file");

        r.ReadUInt32(); // headerSize
        uint flags  = r.ReadUInt32();
        uint height = r.ReadUInt32();
        uint width  = r.ReadUInt32();
        r.ReadUInt32(); // pitchOrLinearSize
        r.ReadUInt32(); // depth
        uint mipCount = r.ReadUInt32();
        r.ReadBytes(44); // reserved1[11]

        // PixelFormat
        r.ReadUInt32(); // pfSize
        uint pfFlags = r.ReadUInt32();
        uint fourCC  = r.ReadUInt32();
        uint bpp     = r.ReadUInt32();
        uint rMask   = r.ReadUInt32();
        uint gMask   = r.ReadUInt32();
        uint bMask   = r.ReadUInt32();
        uint aMask   = r.ReadUInt32();
        r.ReadBytes(20); // caps (4×uint32)

        var fmt      = DetectFormat(pfFlags, fourCC, bpp, rMask, gMask, bMask, aMask);
        bool hasMips = (flags & DDSD_MIPMAPCOUNT) != 0 && mipCount > 1;
        int mips     = hasMips ? (int)mipCount : 1;

        var tex = new Texture2D(device, (int)width, (int)height, hasMips, fmt);
        int w = (int)width, h = (int)height;

        for (int m = 0; m < mips; m++)
        {
            int sz   = MipSize(w, h, fmt);
            var data = r.ReadBytes(sz);
            tex.SetData(m, null, data, 0, sz);
            w = Math.Max(1, w / 2);
            h = Math.Max(1, h / 2);
        }
        return tex;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static SurfaceFormat DetectFormat(uint pfFlags, uint fourCC,
        uint bpp, uint r, uint g, uint b, uint a)
    {
        if ((pfFlags & DDPF_FOURCC) != 0)
        {
            return FourCCToString(fourCC) switch
            {
                "DXT1" => SurfaceFormat.Dxt1,
                "DXT3" => SurfaceFormat.Dxt3,
                "DXT5" => SurfaceFormat.Dxt5,
                _      => throw new NotSupportedException($"Unsupported DDS FourCC: {FourCCToString(fourCC)}")
            };
        }
        return SurfaceFormat.Color;
    }

    private static int MipSize(int w, int h, SurfaceFormat fmt) => fmt switch
    {
        SurfaceFormat.Dxt1 or SurfaceFormat.Dxt1SRgb or SurfaceFormat.Dxt1a
            => Math.Max(1, (w + 3) / 4) * Math.Max(1, (h + 3) / 4) * 8,
        SurfaceFormat.Dxt3 or SurfaceFormat.Dxt3SRgb
        or SurfaceFormat.Dxt5 or SurfaceFormat.Dxt5SRgb
            => Math.Max(1, (w + 3) / 4) * Math.Max(1, (h + 3) / 4) * 16,
        _   => w * h * 4
    };

    private static string FourCCToString(uint v) => new(new[]
    {
        (char)(v & 0xFF), (char)((v >> 8) & 0xFF),
        (char)((v >> 16) & 0xFF), (char)((v >> 24) & 0xFF)
    });
}
