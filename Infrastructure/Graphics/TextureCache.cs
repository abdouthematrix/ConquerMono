namespace ConquerMono.Infrastructure.Graphics;

// ── TGAHelper ─────────────────────────────────────────────────────────────────

public static class TGAHelper
{
    public static Texture2D LoadFromStream(Stream stream, GraphicsDevice device) =>
        Load(device, stream);

    public static Texture2D Load(GraphicsDevice device, Stream fs)
    {
        using var r = new BinaryReader(fs);
        return Load(device, r);
    }

    public static Texture2D Load(GraphicsDevice device, BinaryReader r)
    {
        byte idLen   = r.ReadByte();
        byte cmType  = r.ReadByte();
        byte imgType = r.ReadByte();
        r.ReadBytes(5);                  // color-map spec
        r.ReadUInt16(); r.ReadUInt16();  // origin
        ushort w   = r.ReadUInt16();
        ushort h   = r.ReadUInt16();
        byte   bpp = r.ReadByte();
        byte   desc= r.ReadByte();
        if (idLen > 0) r.ReadBytes(idLen);

        int bpPixel = bpp / 8;
        var pixels  = new Color[w * h];
        int idx     = 0;

        if (imgType == 2) // uncompressed RGB/RGBA
        {
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = ReadPixel(r, bpPixel);
        }
        else if (imgType == 10) // RLE
        {
            while (idx < pixels.Length)
            {
                byte hdr  = r.ReadByte();
                int  cnt  = (hdr & 0x7F) + 1;
                if ((hdr & 0x80) != 0)
                {
                    var p = ReadPixel(r, bpPixel);
                    for (int i = 0; i < cnt && idx < pixels.Length; i++) pixels[idx++] = p;
                }
                else
                {
                    for (int i = 0; i < cnt && idx < pixels.Length; i++) pixels[idx++] = ReadPixel(r, bpPixel);
                }
            }
        }
        else throw new NotSupportedException($"TGA type {imgType} not supported");

        if ((desc & 0x20) == 0) FlipV(pixels, w, h);

        var tex = new Texture2D(device, w, h);
        tex.SetData(pixels);
        return tex;
    }

    private static Color ReadPixel(BinaryReader r, int bpp)
    {
        byte b = r.ReadByte(), g = r.ReadByte(), rc = r.ReadByte();
        byte a = bpp == 4 ? r.ReadByte() : (byte)255;
        return new Color(rc, g, b, a);
    }

    private static void FlipV(Color[] pixels, int w, int h)
    {
        var tmp = new Color[w];
        for (int y = 0; y < h / 2; y++)
        {
            int top = y * w, bot = (h - 1 - y) * w;
            Array.Copy(pixels, top, tmp, 0, w);
            Array.Copy(pixels, bot, pixels, top, w);
            Array.Copy(tmp,    0,   pixels, bot, w);
        }
    }
}

// ── TextureCache ──────────────────────────────────────────────────────────────

/// <summary>Caches loaded textures to avoid repeated disk I/O and GPU upload.</summary>
public sealed class TextureCache : IDisposable
{
    private readonly Dictionary<string, Texture2D> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly IPackageReader _reader;
    private readonly GraphicsDevice _device;

    public TextureCache(IPackageReader reader, GraphicsDevice device)
    {
        _reader = reader;
        _device = device;
    }

    public Texture2D GetOrLoad(string path)
    {
        if (_cache.TryGetValue(path, out var cached)) return cached;

        // .msk files are just renamed .dds
        if (Path.GetExtension(path).Equals(".msk", StringComparison.OrdinalIgnoreCase))
            path = Path.ChangeExtension(path, ".dds");

        using var stream = _reader.LoadFile(path);
        var ext = Path.GetExtension(path).ToLowerInvariant();

        Texture2D tex = ext switch
        {
            ".dds" => DDSHelper.LoadFromStream(stream, _device),
            ".tga" => TGAHelper.LoadFromStream(stream, _device),
            _      => Texture2D.FromStream(_device, stream)
        };

        _cache[path] = tex;
        return tex;
    }

    public void Clear()
    {
        foreach (var t in _cache.Values) t?.Dispose();
        _cache.Clear();
    }

    public void Dispose() => Clear();
}
