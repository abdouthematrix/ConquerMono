namespace ConquerMono.Conquer.Graphics;
// ── TextureCache ──────────────────────────────────────────────────────────────

/// <summary>
/// Scoped texture loader that uses <see cref="C3Texture"/> as its backing store.
/// <para>
/// Each instance tracks the C3Texture slot indices it owns so that
/// <see cref="Clear"/> / <see cref="Dispose"/> correctly decrements
/// <see cref="C3TextureEntry.DupCount"/> for every texture this cache loaded.
/// Textures that were already present in the global registry (loaded by other
/// caches or code) are shared via ref-counting and are never double-freed.
/// </para>
/// </summary>
public sealed class TextureCache : IDisposable
{
    // path → C3Texture slot index for every texture owned by this instance
    private readonly Dictionary<string, int> _pathToSlot =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly IPackageReader _reader;
    private readonly GraphicsDevice _device;

    public TextureCache(IPackageReader reader, GraphicsDevice device)
    {
        _reader = reader;
        _device = device;
    }

    public Texture2D? GetOrLoad(string path)
    {
        // 1. Already tracked specifically by this instance
        if (_pathToSlot.TryGetValue(path, out int cached))
            return C3Texture.Get(cached)?.Texture;

        // 2. Delegate the rest to C3Texture (Checks global registry, then loads from stream if needed)
        using var stream = _reader.LoadFile(path);

        int slot = C3Texture.Texture_Load(path, stream, duplicate: true);

        if (slot >= 0)
        {
            // Track ownership for this specific cache so it can clean up later
            _pathToSlot[path] = slot;
            return C3Texture.Get(slot)?.Texture;
        }

        // Slot table full or stream was invalid
        return null;
    }    

    /// <summary>
    /// Decrements the ref-count of every texture owned by this cache.
    /// Textures whose count reaches zero are disposed from the global registry.
    /// </summary>
    public void Clear()
    {
        foreach (var slot in _pathToSlot.Values)
            C3Texture.Texture_Unload(slot);
        _pathToSlot.Clear();
    }

    public void Dispose() => Clear();
}