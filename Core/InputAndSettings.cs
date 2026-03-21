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

// ── GameSettings ──────────────────────────────────────────────────────────────

/// <summary>
/// Runtime settings loaded from a JSON file stored in %AppData%/ConquerMono.
/// Call <see cref="Load"/> once at startup; call <see cref="Save"/> after mutations.
/// </summary>
public sealed class GameSettings
{
    // ── Persisted fields ──────────────────────────────────────────────────────
    public string ConquerDirectory { get; set; } = string.Empty;
    public string GameMapFilePath { get; set; } = string.Empty;
    public int DefaultMapId { get; set; } = 1006;
    public float DefaultZoom { get; set; } = 0.5f;
    public string LastMapPath { get; set; } = string.Empty;

    // ── Player 3-D model (C3 format) ──────────────────────────────────────
    /// <summary>Absolute path to the player's .c3 model file. Empty = use procedural mesh.</summary>
    public string PlayerModelPath { get; set; } = string.Empty;
    /// <summary>Explicit texture override. Empty = use texture embedded in the .c3 file.</summary>
    public string PlayerTexturePath { get; set; } = string.Empty;
    /// <summary>Uniform scale applied to the player's World matrix. Default 1.0.</summary>
    public float PlayerModelScale { get; set; } = 1.0f;

    // ── Per-state motion files (each is a separate .c3 or empty = skip) ────
    public string PlayerIdleMotionPath { get; set; } = string.Empty;
    public string PlayerWalkMotionPath { get; set; } = string.Empty;
    public string PlayerRunMotionPath { get; set; } = string.Empty;
    public string PlayerJumpMotionPath { get; set; } = string.Empty;

    // ── Movement tuning ────────────────────────────────────────────────────
    /// <summary>Walk speed in cells per second.</summary>
    public float PlayerWalkSpeed { get; set; } = 5f;
    /// <summary>Run speed in cells per second (hold Shift).</summary>
    public float PlayerRunSpeed { get; set; } = 10f;

    // ── Convenience ────────────────────────────────────────────────────────
    /// <summary>
    /// Set all player motion paths at once from a model directory using standard
    /// Conquer Online action IDs (e.g. idle=100, walk=110, run=120, jump=130).
    /// </summary>
    public void SetPlayerMotionsFromDirectory(string dir,
        int idleAction = C3Format.C3Actions.Standby,
        int walkAction = C3Format.C3Actions.WalkL,
        int runAction = C3Format.C3Actions.RunL,
        int jumpAction = C3Format.C3Actions.Jump)
    {
        PlayerIdleMotionPath = C3Format.C3Actions.ToPath(dir, idleAction);
        PlayerWalkMotionPath = C3Format.C3Actions.ToPath(dir, walkAction);
        PlayerRunMotionPath = C3Format.C3Actions.ToPath(dir, runAction);
        PlayerJumpMotionPath = C3Format.C3Actions.ToPath(dir, jumpAction);
    }

    // ── File I/O ──────────────────────────────────────────────────────────────

    private static string SettingsPath
    {
        get
        {
            var folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ConquerMono");
            Directory.CreateDirectory(folder);
            return Path.Combine(folder, "settings.json");
        }
    }

    public static GameSettings Load()
    {
        try
        {
            var path = SettingsPath;
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                return System.Text.Json.JsonSerializer.Deserialize<GameSettings>(json)
                       ?? CreateDefault();
            }
        }
        catch { /* fall through */ }
        return CreateDefault();
    }

    public void Save()
    {
        try
        {
            var opts = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(SettingsPath, System.Text.Json.JsonSerializer.Serialize(this, opts));
        }
        catch (Exception ex) { Debug.WriteLine($"[Settings] Save failed: {ex.Message}"); }
    }

    private static GameSettings CreateDefault() => new()
    {
        ConquerDirectory = string.Empty,
        GameMapFilePath = string.Empty,
    };

    public bool IsValid() =>
        !string.IsNullOrEmpty(ConquerDirectory) &&
        Directory.Exists(ConquerDirectory) &&
        !string.IsNullOrEmpty(GameMapFilePath) &&
        File.Exists(GameMapFilePath);
}