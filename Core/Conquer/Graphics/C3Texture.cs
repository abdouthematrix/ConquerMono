namespace ConquerMono.Conquer.Graphics;

// ── C3TextureEntry ────────────────────────────────────────────────────────────

/// <summary>Equivalent to original C++ C3Texture structure.</summary>
public sealed class C3TextureEntry
{
    public int ID = -1;

    /// <summary>Duplicate/shared reference count.</summary>
    public int DupCount = 0;

    /// <summary>Texture file name / path key.</summary>
    public string Name = string.Empty;

    /// <summary>Texture object.</summary>
    public Texture2D? Texture;

    /// <summary>Texture format.</summary>
    public SurfaceFormat Format;

    public int Width;
    public int Height;
}

// ── C3Texture ─────────────────────────────────────────────────────────────────

/// <summary>
/// MonoGame port of original DX8 C3Texture system.
/// Global slot-based texture registry with reference-counted sharing.
/// </summary>
public static class C3Texture
{
    public const int TEX_MAX = 10240;

    public static int TextureCount = 0;

    public static readonly C3TextureEntry?[] Textures = new C3TextureEntry[TEX_MAX];

    private static readonly object _lock = new();
    private static GraphicsDevice? _graphicsDevice;

    public static void Initialize(GraphicsDevice graphicsDevice)
    {
        _graphicsDevice = graphicsDevice;
    }

    // ── Texture_Clear ─────────────────────────────────────────────────────────

    /// <summary>Equivalent to Texture_Clear().</summary>
    public static void Texture_Clear(C3TextureEntry tex)
    {
        tex.ID = -1;
        tex.DupCount = 0;
        tex.Name = string.Empty;
        tex.Texture = null;
        tex.Width = 0;
        tex.Height = 0;
        tex.Format = SurfaceFormat.Color;
    }

    // ── Texture_Load ──────────────────────────────────────────────────────────

    /// <summary>
    /// Equivalent to original Texture_Load().
    /// <para>
    /// When <paramref name="duplicate"/> is <c>true</c> and a slot with the same
    /// <paramref name="name"/> already exists, its <see cref="C3TextureEntry.DupCount"/>
    /// is incremented and the existing index is returned – no new slot is allocated
    /// and <paramref name="texture"/> (if supplied) is NOT registered.
    /// Callers must dispose a supplied texture themselves when -1 is returned.
    /// </para>
    /// </summary>
    /// <returns>Slot index ≥ 0, or -1 on failure.</returns>
    public static int Texture_Load(string name, Texture2D? texture = null,
        bool duplicate = true)
    {
        if (string.IsNullOrWhiteSpace(name))
            return -1;

        if (_graphicsDevice == null)
            throw new InvalidOperationException("C3Texture.Initialize() not called.");

        lock (_lock)
        {
            // Duplicate/shared texture lookup
            if (duplicate)
            {
                int found = 0;
                for (int t = 0; t < TEX_MAX; t++)
                {
                    if (Textures[t] != null)
                    {
                        if (string.Equals(Textures[t]!.Name, name,
                                StringComparison.OrdinalIgnoreCase))
                        {
                            Textures[t]!.DupCount++;
                            return t;
                        }
                        if (++found == TextureCount) break;
                    }
                }
            }

            if (texture == null)
                return -1;

            var tex = new C3TextureEntry();
            Texture_Clear(tex);
            tex.Texture = texture;
            tex.Name = name;
            tex.DupCount = 1;
            tex.Width = texture.Width;
            tex.Height = texture.Height;
            tex.Format = texture.Format;

            for (int t = 0; t < TEX_MAX; t++)
            {
                if (Textures[t] == null)
                {
                    tex.ID = t;
                    Textures[t] = tex;
                    return t;
                }
            }

            texture.Dispose();
            return -1;
        }
    }

    // ── Texture_Load (Stream) ─────────────────────────────────────────────────

    /// <summary>
    /// Loads a texture directly from a stream. 
    /// If <paramref name="duplicate"/> is true and the texture is already registered, 
    /// the stream is ignored, the DupCount is incremented, and the existing slot is returned.
    /// </summary>
    public static int Texture_Load(string texturePath, Stream texstream, bool duplicate = true)
    {
        if (string.IsNullOrWhiteSpace(texturePath))
            return -1;

        if (_graphicsDevice == null)
            throw new InvalidOperationException("C3Texture.Initialize() not called.");

        // 1. Check if it already exists (skip stream decoding entirely if it does)
        if (duplicate)
        {
            // The existing overload returns the slot if found when passing a null texture
            int existingSlot = Texture_Load(texturePath, texture: null, duplicate: true);
            if (existingSlot >= 0)
            {
                return existingSlot;
            }
        }

        if (texstream == null)
            return -1;

        // 2. Decode the stream into a Texture2D
        Texture2D loadedTexture;
        try
        {
            var ext = Path.GetExtension(texturePath).ToLowerInvariant();

            // Normalise .msk → .dds
            if (ext == ".msk")
                ext = ".dds";

            loadedTexture = ext switch
            {
                ".dds" => DDSLoader.LoadFromStream(texstream, _graphicsDevice),
                ".tga" => TGALoader.LoadFromStream(texstream, _graphicsDevice),
                _ => Texture2D.FromStream(_graphicsDevice, texstream)
            };
        }
        catch
        {
            // Gracefully handle stream decoding errors (e.g., corrupt file)
            return -1;
        }

        // 3. Register the newly created texture
        // (Pass duplicate: false since we already verified it doesn't exist)
        return Texture_Load(texturePath, loadedTexture, duplicate: false);
    }
    // ── Texture_Unload ────────────────────────────────────────────────────────

    /// <summary>Decrement ref-count for the given slot; disposes when it reaches zero.</summary>
    public static void Texture_Unload(int texIndex)
    {
        if (texIndex < 0 || texIndex >= TEX_MAX || Textures[texIndex] == null) return;
        Texture_Unload(Get(texIndex));
    }

    /// <summary>Equivalent to Texture_Unload().</summary>
    public static void Texture_Unload(C3TextureEntry? tex)
    {
        if (tex == null) return;

        lock (_lock)
        {
            tex.DupCount--;
            if (tex.DupCount <= 0)
            {
                int id = tex.ID;
                tex.Texture?.Dispose();
                Texture_Clear(tex);
                if (id >= 0 && id < TEX_MAX)
                    Textures[id] = null;
            }
        }
    }

    // ── Texture_UnloadAll ─────────────────────────────────────────────────────

    /// <summary>Unload and dispose all textures. Equivalent to engine shutdown cleanup.</summary>
    public static void Texture_UnloadAll()
    {
        lock (_lock)
        {
            for (int t = 0; t < TEX_MAX; t++)
            {
                var tex = Textures[t];
                if (tex == null) continue;
                tex.Texture?.Dispose();
                Texture_Clear(tex);
                Textures[t] = null;
            }
        }
    }

    // ── Get ───────────────────────────────────────────────────────────────────

    public static C3TextureEntry? Get(int index)
    {
        if (index < 0 || index >= TEX_MAX) return null;
        lock (_lock) { return Textures[index]; }
    }
}