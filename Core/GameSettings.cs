namespace ConquerMono.Core;
// ── GameSettings ──────────────────────────────────────────────────────────────

/// <summary>
/// Runtime settings loaded from a JSON file stored in %AppData%/ConquerMono.
/// Call <see cref="Load"/> once at startup; call <see cref="Save"/> after mutations.
/// </summary>
public sealed class GameSettings
{
    // ── Persisted fields ──────────────────────────────────────────────────────
    public string ConquerDirectory { get; set; } = string.Empty;
    public float DefaultZoom { get; set; }
    /// <summary>Uniform scale applied to the player's World matrix. Default 1.0.</summary>
    public float PlayerModelScale { get; set; }
    // ── Movement tuning ────────────────────────────────────────────────────
    /// <summary>Walk speed in cells per second.</summary>
    public float PlayerWalkSpeed { get; set; } 
    /// <summary>Run speed in cells per second (hold Shift).</summary>
    public float PlayerRunSpeed { get; set; }

    public int MapId { get; set; }

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
    };

    public bool IsValid() =>
        !string.IsNullOrEmpty(ConquerDirectory) &&
        Directory.Exists(ConquerDirectory) &&
        File.Exists(Path.Combine(ConquerDirectory, "ini", "gamemap.dat"));
}