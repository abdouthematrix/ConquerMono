using Microsoft.Xna.Framework.Input;

namespace ConquerMono.Core;

/// <summary>Wraps keyboard state and exposes per-frame edge detection.</summary>
public sealed class InputManager
{
    private KeyboardState _prev;
    private KeyboardState _curr;

    public void Update()
    {
        _prev = _curr;
        _curr = Keyboard.GetState();
    }

    /// <summary>Key is currently held down.</summary>
    public bool IsHeld(Keys key) => _curr.IsKeyDown(key);

    /// <summary>Key was pressed this frame (rising edge).</summary>
    public bool IsPressed(Keys key) => _curr.IsKeyDown(key) && !_prev.IsKeyDown(key);

    /// <summary>Key was released this frame (falling edge).</summary>
    public bool IsReleased(Keys key) => !_curr.IsKeyDown(key) && _prev.IsKeyDown(key);
}
