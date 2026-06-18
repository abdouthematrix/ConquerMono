namespace ConquerMono.Map.Rendering.Drawing;

public sealed class BackdropDrawingComponent : PuzzleDrawingComponent
{
    private const int H_DIV = 3;
    private const int V_DIV = 8;

    private readonly Puzzle _main;
    private readonly Matrix _scale;

    public BackdropDrawingComponent(
        Puzzle backdrop, Puzzle main, IAniDictionary ani, TextureCache cache)
        : base(backdrop, ani, cache)
    {
        _main  = main;
        _scale = Matrix.CreateScale((float)_main.Width  / _puzzle.Width,
                                    (float)_main.Height / _puzzle.Height, 1f);
    }

    public override void UpdateScreen(Rectangle sr)
    {
        int ox = sr.X, oy = sr.Y;

        if (_puzzle.HorizontalRate.HasValue && _puzzle.HorizontalRate.Value != 0)
        {
            int div = _puzzle.HorizontalRate.Value / H_DIV;
            if (div != 0) ox /= div;
        }
        if (_puzzle.VerticalRate.HasValue && _puzzle.VerticalRate.Value != 0)
        {
            int div = _puzzle.VerticalRate.Value / V_DIV;
            if (div != 0) oy /= div;
        }

        float sx = (float)_puzzle.Width  / _main.Width;
        float sy = (float)_puzzle.Height / _main.Height;

        var bdr = new Rectangle(
            (int)(ox * sx), (int)(oy * sy),
            (int)(sr.Width  * sx), (int)(sr.Height * sy));

        base.UpdateScreen(bdr);
    }

    public override void Draw(SpriteBatch sb, Matrix transform)
    {
        if (!Enabled) return;
        base.Draw(sb, _scale * transform);
    }
}
