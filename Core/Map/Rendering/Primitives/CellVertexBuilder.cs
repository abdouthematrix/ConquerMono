namespace ConquerMono.Map.Rendering.Primitives;

/// <summary>
/// Batches isometric cell outlines using a dynamic vertex buffer and
/// <see cref="PrimitiveType.LineList"/> rendering via BasicEffect.
/// </summary>
public sealed class CellVertexBuilder : IDisposable
{
    private const int VPR_CELL = 8; // 4 lines × 2 vertices

    private readonly Vector2[]    _pts;
    private readonly GraphicsDevice _gd;
    private readonly BasicEffect  _effect;

    private VertexPositionColor[]  _verts;
    private int                    _vtxCount;
    private DynamicVertexBuffer?   _vb;
    private int                    _vbCap;

    public int PrimitiveCount { get; private set; }

    public CellVertexBuilder(Vector2[] cellPoints, GraphicsDevice gd)
    {
        _pts   = cellPoints;
        _gd    = gd;
        _verts = new VertexPositionColor[1024];
        _effect = new BasicEffect(gd)
        {
            VertexColorEnabled = true,
            View       = Matrix.Identity,
            Projection = Matrix.CreateOrthographicOffCenter(
                0, gd.Viewport.Width, gd.Viewport.Height, 0, 0, 1)
        };
    }

    public void UpdateProjection(Viewport vp)
    {
        _effect.Projection = Matrix.CreateOrthographicOffCenter(
            0, vp.Width, vp.Height, 0, 0, 1);
    }

    public void Begin(int estimate = 0)
    {
        _vtxCount = 0; PrimitiveCount = 0;
        int need = estimate * VPR_CELL;
        if (_verts.Length < need)
            Array.Resize(ref _verts, Math.Max(need, _verts.Length * 2));
    }

    public void AddCell(Vector2 loc, Color col)
    {
        if (_vtxCount + VPR_CELL > _verts.Length)
            Array.Resize(ref _verts, _verts.Length * 2);

        AddLine(loc, _pts[0], _pts[1], col);
        AddLine(loc, _pts[1], _pts[2], col);
        AddLine(loc, _pts[2], _pts[3], col);
        AddLine(loc, _pts[3], _pts[0], col);
        PrimitiveCount += 4;
    }

    private void AddLine(Vector2 base_, Vector2 a, Vector2 b, Color c)
    {
        _verts[_vtxCount++] = new VertexPositionColor(new Vector3(base_.X + a.X, base_.Y + a.Y, 0), c);
        _verts[_vtxCount++] = new VertexPositionColor(new Vector3(base_.X + b.X, base_.Y + b.Y, 0), c);
    }

    public void End()
    {
        if (_vtxCount == 0) return;
        if (_vb == null || _vbCap < _vtxCount)
        {
            _vb?.Dispose();
            _vbCap = Math.Max(_vtxCount, _vbCap * 3 / 2 + 64);
            _vb = new DynamicVertexBuffer(_gd, typeof(VertexPositionColor), _vbCap, BufferUsage.WriteOnly);
        }
        _vb.SetData(_verts, 0, _vtxCount, SetDataOptions.Discard);
    }

    public void Draw(Matrix transform)
    {
        if (_vb == null || PrimitiveCount == 0) return;
        _effect.World = transform;
        _effect.CurrentTechnique.Passes[0].Apply();
        _gd.SetVertexBuffer(_vb);
        _gd.DrawPrimitives(PrimitiveType.LineList, 0, PrimitiveCount);
    }

    public void Dispose() { _vb?.Dispose(); _effect.Dispose(); }
}
