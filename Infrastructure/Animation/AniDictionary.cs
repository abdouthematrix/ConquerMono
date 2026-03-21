namespace ConquerMono.Infrastructure.Animation;

/// <summary>
/// Thread-safe cache of loaded .ani index files.
/// Implements <see cref="IAniDictionary"/> so it can be injected into drawing components.
/// </summary>
public sealed class AniDictionary : IAniDictionary
{
    private readonly Dictionary<string, AnimationIndex> _cache =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly string    _conquerDir;
    private readonly AniParser _parser = new();
    private readonly object    _lock   = new();

    public AniDictionary(string conquerDirectory)
    {
        if (!Directory.Exists(conquerDirectory))
            throw new DirectoryNotFoundException($"Conquer directory not found: {conquerDirectory}");
        _conquerDir = conquerDirectory;
    }

    // ── IAniDictionary ────────────────────────────────────────────────────────

    public void Add(string aniPath)
    {
        if (string.IsNullOrWhiteSpace(aniPath)) return;
        lock (_lock)
        {
            if (_cache.ContainsKey(aniPath)) return;

            var full = Path.Combine(_conquerDir, aniPath);
            if (!File.Exists(full))
            {
                _cache[aniPath] = new AnimationIndex();
                return;
            }
            try   { _cache[aniPath] = _parser.ParseFile(full); }
            catch { _cache[aniPath] = new AnimationIndex(); }
        }
    }

    public IReadOnlyList<string> this[string aniPath, string animationName]
    {
        get
        {
            if (string.IsNullOrWhiteSpace(aniPath) || string.IsNullOrWhiteSpace(animationName))
                return Array.Empty<string>();
            if (!_cache.ContainsKey(aniPath)) Add(aniPath);
            return _cache.TryGetValue(aniPath, out var idx)
                ? idx.GetFrames(animationName).AsReadOnly()
                : Array.Empty<string>();
        }
    }

    public string? GetFrame(string aniPath, string animationName, int frameIndex = 0)
    {
        if (string.IsNullOrWhiteSpace(aniPath)) return null;
        if (!_cache.ContainsKey(aniPath)) Add(aniPath);
        return _cache.TryGetValue(aniPath, out var idx)
            ? idx.GetFrame(animationName, frameIndex)
            : null;
    }

    public bool IsLoaded(string aniPath) => _cache.ContainsKey(aniPath);

    // ── Helpers ───────────────────────────────────────────────────────────────

    public void AddRange(IEnumerable<string> paths) { foreach (var p in paths) Add(p); }
    public void Clear()   { lock (_lock) _cache.Clear(); }
    public int  CachedCount => _cache.Count;
}

// ── Extension methods ─────────────────────────────────────────────────────────

public static class AniDictionaryExtensions
{
    public static bool TryGetFrames(
        this IAniDictionary d, string aniPath, string animName,
        out IReadOnlyList<string> frames)
    {
        frames = d[aniPath, animName];
        return frames.Count > 0;
    }
}
