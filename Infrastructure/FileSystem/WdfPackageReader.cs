namespace ConquerMono.Infrastructure.FileSystem;

/// <summary>Reads files packed in Conquer Online's WDF archive format.</summary>
internal sealed class WdfPackageReader : IPackageReader
{
    private struct PackedFile { public uint FileId, FileOffset, FileSize, Reserved; }

    private readonly Dictionary<uint, PackedFile> _files = new();
    private readonly FileStream _pack;

    public WdfPackageReader(string fileName)
    {
        _pack = new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var r = new BinaryReader(_pack, Encoding.ASCII, leaveOpen: true);

        r.ReadUInt32();                     // magic
        uint fileCount   = r.ReadUInt32();
        uint indexOffset = r.ReadUInt32();

        _pack.Seek(indexOffset, SeekOrigin.Begin);
        for (uint i = 0; i < fileCount; i++)
        {
            var f = new PackedFile
            {
                FileId     = r.ReadUInt32(),
                FileOffset = r.ReadUInt32(),
                FileSize   = r.ReadUInt32(),
                Reserved   = r.ReadUInt32()
            };
            _files.TryAdd(f.FileId, f);
        }
    }

    public void AddPackage(string fileName) => throw new NotSupportedException();

    public Stream LoadFile(string fileName)
    {
        uint hash = HashFilename(fileName);
        if (!_files.TryGetValue(hash, out var f))
            throw new FileNotFoundException($"File not found in WDF: {fileName}");

        lock (_pack)
        {
            _pack.Seek(f.FileOffset, SeekOrigin.Begin);
            var buf = new byte[f.FileSize];
            _ = _pack.Read(buf, 0, buf.Length);
            return new MemoryStream(buf, writable: false);
        }
    }

    public void Dispose()
    {
        _files.Clear();
        _pack.Dispose();
    }

    // ── WDF filename hash (original CO algorithm) ─────────────────────────────
    private static uint HashFilename(string filename)
    {
        uint a = 4110059816u, b = 0u, c = 0u, d = 933775118u, e = 2002301995u, f2 = 0u;
        uint[] arr = new uint[70];
        byte[] raw  = Encoding.ASCII.GetBytes(filename.ToLowerInvariant());
        int pad = raw.Length % 4 != 0 ? 4 - raw.Length % 4 : 0;
        byte[] padded = new byte[raw.Length + pad];
        raw.CopyTo(padded, 0);

        int n;
        using (var br = new BinaryReader(new MemoryStream(padded, writable: false)))
            for (n = 0; n < padded.Length / 4; n++) arr[n] = (uint)br.ReadInt32();

        arr[n++] = 2615624776u;
        arr[n++] = 1727278152u;

        for (int i = 0; i < n; i++)
        {
            f2  = 645597969u;
            a   = (a << 1) | (a >> 31);
            f2 ^= a;
            b   = arr[i];
            d  ^= b; e ^= b;
            uint t = f2 + e;
            t |= 0x2040801u; t &= 0xBFEF7FDFu;
            ulong m = (ulong)t * d;
            uint lo = (uint)m, hi = (uint)(m >> 32);
            if (hi != 0) lo++;
            m = lo; m += hi; lo = (uint)m;
            if ((uint)(m >> 32) != 0) lo++;
            t = f2 + d;
            t |= 0x804021u; t &= 0x7DFEFBFFu;
            d = lo;
            m = (ulong)e * t;
            lo = (uint)m; hi = (uint)(m >> 32);
            m = hi; m += hi; hi = (uint)m;
            if ((uint)(m >> 32) != 0) lo++;
            m = lo; m += hi; lo = (uint)m;
            if ((uint)(m >> 32) != 0) lo += 2;
            e = lo;
        }
        return d ^ e;
    }
}
