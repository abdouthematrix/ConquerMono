namespace ConquerMono.Infrastructure.FileSystem;

/// <summary>
/// Top-level <see cref="IPackageReader"/> that tries:
///  1. Absolute path on disk
///  2. &lt;conquerDir&gt;/&lt;fileName&gt; on disk
///  3. A loaded WDF sub-package whose key matches the first path segment
/// </summary>
public sealed class TqPackageReader : IPackageReader
{
    private readonly Dictionary<string, IPackageReader> _packs = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _conquerDir;

    public TqPackageReader(string conquerDirectory)
    {
        _conquerDir = conquerDirectory;
        AddPackage("c3.wdf");
        AddPackage("data.wdf");
    }

    public void AddPackage(string fileName)
    {
        var full = Path.Combine(_conquerDir, fileName);
        if (!File.Exists(full)) return;

        var name = Path.GetFileNameWithoutExtension(fileName).ToLowerInvariant();
        var ext  = Path.GetExtension(fileName).ToLowerInvariant();

        if (ext == ".wdf")
            _packs[name] = new WdfPackageReader(full);
    }

    public Stream LoadFile(string fileName)
    {
        // 1 — absolute path
        if (File.Exists(fileName))
            return ReadToMemory(fileName);

        // 2 — relative to conquer directory
        var full = Path.Combine(_conquerDir, fileName);
        if (File.Exists(full))
            return ReadToMemory(full);

        // 3 — first segment selects a WDF package
        var key = fileName.Split('/', '\\')[0].ToLowerInvariant();
        if (_packs.TryGetValue(key, out var pack))
            return pack.LoadFile(fileName);

        throw new FileNotFoundException($"File not found: {fileName}");
    }

    private static Stream ReadToMemory(string path)
    {
        if (Path.GetExtension(path).ToLowerInvariant() == ".7z")
        {
            using var archive = new ArchiveFile(path);
            var dmapEntry = archive.Entries.FirstOrDefault(e =>
                Path.GetExtension(e.FileName).ToLowerInvariant() == ".dmap");

            if (dmapEntry != null)
            {
                var ms = new MemoryStream();
                dmapEntry.Extract(ms);
                ms.Position = 0;
                return ms;
            }
        }

        using var fs  = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var buf = new byte[fs.Length];
        _ = fs.Read(buf, 0, buf.Length);
        return new MemoryStream(buf, writable: false);
    }

    public void Dispose()
    {
        foreach (var p in _packs.Values) p.Dispose();
        _packs.Clear();
    }
}
