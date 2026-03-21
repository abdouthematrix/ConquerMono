using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace ConquerMono.C3Format
{
    /// <summary>
    /// 2D screen-space textured quad (port of c3_sprite.cpp).
    ///
    /// Vertex layout (TRIANGLESTRIP order, matches original):
    ///   [0] = top-left     (u=0, v=0)
    ///   [1] = bottom-left  (u=0, v=1)
    ///   [2] = top-right    (u=1, v=0)
    ///   [3] = bottom-right (u=1, v=1)
    ///
    /// Draw modes (Sprite_Draw dwShowWay):
    ///   0 – Normal:   AlphaBlend (SrcAlpha/InvSrcAlpha) or Opaque if no alpha
    ///   1 – Additive: One/One
    ///   2 – Multiply: SrcAlpha/SrcAlpha
    /// </summary>
    public class C3Sprite : IDisposable
    {
        private Vector2[] _pos   = new Vector2[4];
        private Vector2[] _uv    = new Vector2[4];
        private Color[]   _color = new Color[4];

        private int          _texIndex = -1;
        private Texture2D    _texture;
        private SurfaceFormat _format = SurfaceFormat.Color;

        private readonly GraphicsDevice _gd;

        // Static blend states (created once per GraphicsDevice)
        private static BlendState _blendAdditive;
        private static BlendState _blendMultiply;

        public C3Sprite(GraphicsDevice gd)
        {
            _gd = gd;
            Clear();
            EnsureBlendStates(gd);
        }

        // Sprite_Clear
        public void Clear()
        {
            for (int v = 0; v < 4; v++) { _pos[v] = Vector2.Zero; _color[v] = Color.White; }
            _uv[0] = new Vector2(0,0); _uv[1] = new Vector2(0,1);
            _uv[2] = new Vector2(1,0); _uv[3] = new Vector2(1,1);
        }

        // Sprite_Load via C3Texture cache
        public bool Load(string path, bool bDuplicate = true)
        {
            Unload();
            _texIndex = C3Texture.Texture_Load(path, bDuplicate);
            if (_texIndex == -1) return false;
            var e = C3Texture.Get(_texIndex);
            _texture = e.Texture; _format = e.Format;
            SetPosition(0, 0);
            return true;
        }

        // Set texture directly (bypasses cache)
        public void SetTexture(Texture2D tex)
        {
            Unload();
            _texture = tex;
            _format  = tex?.Format ?? SurfaceFormat.Color;
        }

        // Sprite_Unload
        public void Unload()
        {
            if (_texIndex != -1) { C3Texture.Texture_Unload(_texIndex); _texIndex = -1; }
            _texture = null;
        }

        // Sprite_Mirror: horizontal UV flip
        public void Mirror()
        {
            Swap(ref _uv[0], ref _uv[2]);
            Swap(ref _uv[1], ref _uv[3]);
        }

        // Simple position setter (full texture, auto size)
        public void SetPosition(int x, int y)
            => SetCoordinates(null, x, y, _texture?.Width ?? 0, _texture?.Height ?? 0);

        // Sprite_SetCoor: map source rect → destination rect at (x,y)
        // srcRect=null uses full texture; w=0/h=0 uses texture size
        public void SetCoordinates(Rectangle? src, int x, int y, int w=0, int h=0)
        {
            if (_texture == null) return;
            int tw=_texture.Width, th=_texture.Height;

            if (src == null)
            {
                _uv[0]=new Vector2(0,0); _uv[1]=new Vector2(0,1);
                _uv[2]=new Vector2(1,0); _uv[3]=new Vector2(1,1);
            }
            else
            {
                float u0=(float)src.Value.Left/tw,  u1=(float)src.Value.Right/tw;
                float v0=(float)src.Value.Top/th,   v1=(float)src.Value.Bottom/th;
                _uv[0]=new Vector2(u0,v0); _uv[1]=new Vector2(u0,v1);
                _uv[2]=new Vector2(u1,v0); _uv[3]=new Vector2(u1,v1);
            }

            int dw=w==0?tw:w, dh=h==0?th:h;
            _pos[0]=new Vector2(x,    y);     _pos[1]=new Vector2(x,    y+dh);
            _pos[2]=new Vector2(x+dw, y);     _pos[3]=new Vector2(x+dw, y+dh);
        }

        // Sprite_SetColor: uniform ARGB for all vertices
        public void SetColor(byte a, byte r, byte g, byte b)
        { var c=new Color(r,g,b,a); for(int v=0;v<4;v++) _color[v]=c; }

        // Sprite_SetVertexColor: per-vertex (TL, TR, BL, BR)
        public void SetVertexColor(Color tl, Color tr, Color bl, Color br)
        { _color[0]=tl; _color[1]=bl; _color[2]=tr; _color[3]=br; }

        // Sprite_Draw  (dwShowWay: 0=normal, 1=additive, 2=multiply)
        public void Draw(SpriteBatch sb, int dwShowWay = 0)
        {
            if (_texture == null) return;

            // Original checks: format has alpha channel OR vertex[0] alpha < 255
            bool hasAlpha = _format == SurfaceFormat.Dxt3 || _format == SurfaceFormat.Dxt5
                         || _color[0].A < 255;

            BlendState blend = dwShowWay switch
            {
                1 => _blendAdditive,
                2 => hasAlpha ? _blendMultiply : _blendAdditive,
                _ => hasAlpha ? BlendState.AlphaBlend : BlendState.Opaque,
            };

            var dest = new Rectangle((int)_pos[0].X, (int)_pos[0].Y,
                (int)(_pos[2].X - _pos[0].X), (int)(_pos[1].Y - _pos[0].Y));

            // Build source rect from UVs (null if full texture)
            int tw=_texture.Width, th=_texture.Height;
            int sl=(int)(_uv[0].X*tw), st=(int)(_uv[0].Y*th);
            int sr=(int)(_uv[2].X*tw), sb_=(int)(_uv[1].Y*th);
            Rectangle? srcRect = (sl==0&&st==0&&sr==tw&&sb_==th)
                ? (Rectangle?)null
                : new Rectangle(sl, st, sr-sl, sb_-st);

            sb.Begin(SpriteSortMode.Immediate, blend,
                     SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullNone);
            sb.Draw(_texture, dest, srcRect, _color[0]);
            sb.End();
        }

        public Texture2D Texture  => _texture;
        public Vector2   Position => _pos[0];

        public void Dispose() => Unload();

        private static void EnsureBlendStates(GraphicsDevice gd)
        {
            if (_blendAdditive != null) return;
            _blendAdditive = new BlendState
            {
                ColorSourceBlend=Blend.One, AlphaSourceBlend=Blend.One,
                ColorDestinationBlend=Blend.One, AlphaDestinationBlend=Blend.One,
            };
            _blendMultiply = new BlendState
            {
                ColorSourceBlend=Blend.SourceAlpha, AlphaSourceBlend=Blend.SourceAlpha,
                ColorDestinationBlend=Blend.SourceAlpha, AlphaDestinationBlend=Blend.SourceAlpha,
            };
        }

        private static void Swap<T>(ref T a, ref T b) { T t=a; a=b; b=t; }
    }
}
