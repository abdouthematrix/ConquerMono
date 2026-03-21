namespace ConquerMono.Rendering.Drawing;

/// <summary>
/// Base class for all 2-D drawing components.
/// Concrete subclasses implement <see cref="UpdateScreen"/> and <see cref="Draw"/>.
/// </summary>
public abstract class BaseDrawingComponent : IDrawingComponent
{
    public bool Enabled { get; set; } = true;

    public abstract void UpdateScreen(Rectangle screenRect);
    public abstract void Draw(SpriteBatch spriteBatch, Matrix transformMatrix);
}
