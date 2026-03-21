using System;
using System.IO;
using Microsoft.Xna.Framework.Graphics;

namespace ConquerMono.C3Format
{
    // =========================================================================
    // C3TextureEntry  –  one slot in the global texture cache
    // =========================================================================
    public class C3TextureEntry
    {
        public int           ID       { get; set; } = -1;
        public int           RefCount { get; set; } =  0;
        public string        Name     { get; set; }
        public Texture2D     Texture  { get; set; }
        public SurfaceFormat Format   { get; set; }
        public int           Width    { get; set; }
        public int           Height   { get; set; }
    }

    // =========================================================================
    // C3Texture  –  static global texture cache (port of c3_texture.cpp)
    //
    // Ref-counted: Texture_Load increments RefCount, Texture_Unload decrements.
    // Texture is freed when RefCount reaches 0.
    // Thread-safe via lock.
    // =========================================================================
    public static class C3Texture
    {
        public const int TEX_MAX = 512;

        private static readonly C3TextureEntry[] _cache = new C3TextureEntry[TEX_MAX];
        private static readonly object           _lock  = new object();
        private static GraphicsDevice            _gd;

        public static void Initialize(GraphicsDevice gd) { _gd = gd; }

        // ------------------------------------------------------------------
        // Texture_Load  (c3_texture.cpp line 22)
        // Returns cache slot index or -1 on failure.
        // bDuplicate=true: reuse if same name already loaded (default on)
        // ------------------------------------------------------------------
        public static int Texture_Load(string name, bool bDuplicate = true)
        {
            if (string.IsNullOrEmpty(name)) return -1;
            if (_gd == null) throw new InvalidOperationException("C3Texture.Initialize() not called.");

            lock (_lock)
            {
                if (bDuplicate)
                {
                    for (int t = 0; t < TEX_MAX; t++)
                    {
                        var e = _cache[t];
                        if (e != null && string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase))
                        { e.RefCount++; return t; }
                    }
                }

                Texture2D tex;
                try { tex = LoadFromDisk(name); }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[C3Texture] Failed '{name}': {ex.Message}");
                    return -1;
                }

                for (int t = 0; t < TEX_MAX; t++)
                {
                    if (_cache[t] == null)
                    {
                        _cache[t] = new C3TextureEntry
                        {
                            ID       = t,
                            RefCount = 1,
                            Name     = name,
                            Texture  = tex,
                            Format   = tex.Format,
                            Width    = tex.Width,
                            Height   = tex.Height,
                        };
                        return t;
                    }
                }

                tex.Dispose();
                return -1;  // cache full
            }
        }

        // ------------------------------------------------------------------
        // Texture_Unload  (c3_texture.cpp line 142)
        // ------------------------------------------------------------------
        public static void Texture_Unload(int index)
        {
            if (index < 0 || index >= TEX_MAX) return;
            lock (_lock)
            {
                var e = _cache[index];
                if (e == null) return;
                if (--e.RefCount <= 0) { e.Texture?.Dispose(); _cache[index] = null; }
            }
        }

        public static void Texture_UnloadAll()
        {
            lock (_lock)
            {
                for (int t = 0; t < TEX_MAX; t++)
                { _cache[t]?.Texture?.Dispose(); _cache[t] = null; }
            }
        }

        public static C3TextureEntry Get(int index)
        {
            if (index < 0 || index >= TEX_MAX) return null;
            return _cache[index];
        }

        public static int  GetLoadedTextureCount()
        {
            int n=0; lock(_lock) for(int t=0;t<TEX_MAX;t++) if(_cache[t]!=null) n++;
            return n;
        }

        public static long GetTotalMemoryUsage()
        {
            long total=0;
            lock(_lock)
                for(int t=0;t<TEX_MAX;t++)
                {
                    var e=_cache[t];
                    if(e==null) continue;
                    total += e.Format switch {
                        SurfaceFormat.Dxt1 => Math.Max(1,(e.Width+3)/4)*Math.Max(1,(e.Height+3)/4)*8L,
                        SurfaceFormat.Dxt3 or SurfaceFormat.Dxt5
                            => Math.Max(1,(e.Width+3)/4)*Math.Max(1,(e.Height+3)/4)*16L,
                        _  => e.Width*e.Height*4L,
                    };
                }
            return total;
        }

        private static Texture2D LoadFromDisk(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            return ext switch
            {
                ".dds" => DDSLoader.Load(_gd, path),
                ".tga" => TGALoader.Load(_gd, path),
                _      => LoadStream(path),
            };
        }

        private static Texture2D LoadStream(string path)
        {
            using var s = File.OpenRead(path);
            return Texture2D.FromStream(_gd, s);
        }
    }
}
