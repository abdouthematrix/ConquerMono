namespace ConquerMono.Core.Components;

/// <summary>
/// <see cref="DrawableGameComponent"/> that delegates all map rendering to
/// <see cref="Conquer.Services.MapViewerService"/>.
///
/// DrawOrder = 0 → drawn before the player and HUD.
/// </summary>
public sealed class MapRenderComponent : DrawableGameComponent
{
    private readonly ConquerGame _game;

    public MapRenderComponent(ConquerGame game) : base(game)
    {
        _game       = game;
        DrawOrder   = 0;
        UpdateOrder = 20;
    }

    public override void Update(GameTime gt)
    {
        _game.MapViewer?.Update();
    }

    public override void Draw(GameTime gt)
    {
        if (_game.MapViewer == null || _game.SpriteBatch == null) return;
        _game.MapViewer.Draw(_game.SpriteBatch);
    }
}
