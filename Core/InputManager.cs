namespace ConquerMono.Core;

// ── InputManager ──────────────────────────────────────────────────────────────

/// <summary>Per-frame keyboard + mouse state with edge detection.</summary>
public sealed class InputManager
{
    private KeyboardState _prevKey;
    private KeyboardState _currKey;
    private MouseState _prevMouse;
    private MouseState _currMouse;

    public void Update()
    {
        _prevKey = _currKey;
        _currKey = Keyboard.GetState();
        _prevMouse = _currMouse;
        _currMouse = Microsoft.Xna.Framework.Input.Mouse.GetState();
    }

    // Keyboard
    public bool IsHeld(Keys k) => _currKey.IsKeyDown(k);
    public bool IsPressed(Keys k) => _currKey.IsKeyDown(k) && _prevKey.IsKeyUp(k);
    public bool IsReleased(Keys k) => _currKey.IsKeyUp(k) && _prevKey.IsKeyDown(k);

    // Mouse
    public MouseState MouseSnapshot => _currMouse;
    public MouseState PreviousMouse => _prevMouse;

    public int ScrollDelta => _currMouse.ScrollWheelValue - _prevMouse.ScrollWheelValue;
    public bool LeftClick => _currMouse.LeftButton == ButtonState.Pressed
                            && _prevMouse.LeftButton == ButtonState.Released;
    public bool RightHeld => _currMouse.RightButton == ButtonState.Pressed;
    public bool MiddleClick => _currMouse.MiddleButton == ButtonState.Pressed
                            && _prevMouse.MiddleButton == ButtonState.Released;

    public Vector2 MouseDelta => new(
        _currMouse.X - _prevMouse.X,
        _currMouse.Y - _prevMouse.Y);

    public Vector2 MousePosition => new(_currMouse.X, _currMouse.Y);
}
