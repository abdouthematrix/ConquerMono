namespace ConquerMono.Conquer.IO.Animation;

/// <summary>
/// Thread-safe cache of parsed .ani index files.
/// </summary>
public interface IAniDictionary
{
    /// <summary>Parse and cache an .ani file (no-op if already cached).</summary>
    void Add(string aniPath);

    /// <summary>All frame paths for a named animation section.</summary>
    IReadOnlyList<string> this[string aniPath, string animationName] { get; }

    /// <summary>Single frame path, or null if not found.</summary>
    string? GetFrame(string aniPath, string animationName, int frameIndex = 0);

    /// <summary>True if the .ani file is already in the cache.</summary>
    bool IsLoaded(string aniPath);
}
