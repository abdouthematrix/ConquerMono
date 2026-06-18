namespace ConquerMono.Conquer.IO.Animation;

// ── AnimationIndex ────────────────────────────────────────────────────────────

/// <summary>
/// Loaded .ani file — maps section names (e.g. "Puzzle0") to lists of texture paths.
/// </summary>
public sealed class AnimationIndex
{
    public Dictionary<string, List<string>> Animations { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public List<string> GetFrames(string name)
        => Animations.TryGetValue(name, out var f) ? f : new List<string>();

    public string? GetFrame(string name, int index = 0)
    {
        var frames = GetFrames(name);
        return (index >= 0 && index < frames.Count) ? frames[index] : null;
    }

    public bool HasAnimation(string name) => Animations.ContainsKey(name);
    public int  AnimationCount            => Animations.Count;
}

// ── AniParser ─────────────────────────────────────────────────────────────────

/// <summary>
/// Parses INI-style .ani files used by Conquer Online:
/// <code>
/// [Puzzle0]
/// FrameAmount=1
/// Frame0=data/map/puzzle/arena/arena000.dds
/// </code>
/// </summary>
public sealed class AniParser
{
    private static readonly Regex _section     = new(@"^\[(.+)\]$",             RegexOptions.Compiled);
    private static readonly Regex _frame       = new(@"^Frame(\d+)=(.+)$",      RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex _frameAmount = new(@"^FrameAmount=(\d+)$",    RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public AnimationIndex Parse(Stream stream)
    {
        var index          = new AnimationIndex();
        string? curSection = null;
        List<string>? curFrames = null;

        using var reader = new StreamReader(stream, Encoding.ASCII);
        while (!reader.EndOfStream)
        {
            var line = reader.ReadLine()?.Trim();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith(';') || line.StartsWith('#'))
                continue;

            var sec = _section.Match(line);
            if (sec.Success)
            {
                if (curSection != null && curFrames?.Count > 0)
                    index.Animations[curSection] = curFrames;
                curSection = sec.Groups[1].Value;
                curFrames  = new List<string>();
                continue;
            }

            if (curFrames == null) continue;

            if (_frameAmount.IsMatch(line)) continue; // just pre-size hint, skip

            var fm = _frame.Match(line);
            if (fm.Success)
            {
                int  idx  = int.Parse(fm.Groups[1].Value);
                string fp = fm.Groups[2].Value.Trim();
                while (curFrames.Count <= idx) curFrames.Add(string.Empty);
                curFrames[idx] = fp;
            }
        }

        if (curSection != null && curFrames?.Count > 0)
            index.Animations[curSection] = curFrames;

        return index;
    }

    public AnimationIndex ParseFile(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Animation file not found: {filePath}", filePath);
        using var s = File.OpenRead(filePath);
        return Parse(s);
    }
}
