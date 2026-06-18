

namespace ConquerMono.Conquer.Services;

public interface IGameDataService
{
    IReadOnlyList<RolePart> RoleParts { get; }
    IReadOnlyDictionary<ulong, string> MeshMap { get; }
    IReadOnlyDictionary<ulong, string> TextureMap { get; }
    IReadOnlyDictionary<ulong, string> MotionMap { get; }
    IReadOnlyDictionary<ulong, string> WeaponMotionMap { get; }
    IReadOnlyDictionary<ulong, string> MountMotionMap { get; }
    IReadOnlyDictionary<ulong, string> CapeMotionMap { get; }
    IReadOnlyDictionary<ulong, string> MiscMotionMap { get; }
    IReadOnlyDictionary<ulong, string> ArmetMotionMap { get; }
    IReadOnlyDictionary<ulong, string> SpiritMotionMap { get; }
    IReadOnlyDictionary<ulong, string> HeadMotionMap { get; }
    IReadOnlyDictionary<ulong, string> PelvisMotionMap { get; }
    Task LoadAsync(string conquerPath);
    string? ResolveMesh(ulong id);
    string? ResolveTexture(ulong id);
    string? ResolveMotion(ulong motionId);
    string? ResolveWeaponMotion(ulong weaponId, int actionType);
    string? ResolveMountMotion(ulong mountId, int actionType);
    string? ResolveCapeMotion(ulong capeId, int actionType);
    string? ResolveMiscMotion(ulong miscId, int actionType);
    string? ResolveArmetMotion(ulong armetId, int actionType);
    string? ResolveSpiritMotion(ulong spiritId, int actionType);
    string? ResolveHeadMotion(ulong headId, int actionType);
    string? ResolvePelvisMotion(ulong pelvisId, int actionType);
    RolePart? FindRolePart(uint id, RolePartType type);
}

// ─────────────────────────────────────────────────────────────────────────────

public class GameDataService : IGameDataService
{
    // ── Backing stores ────────────────────────────────────────────────────────   
    private List<RolePart> _roleParts = new();

    private Dictionary<ulong, string> _mesh = new();
    private Dictionary<ulong, string> _tex = new();
    private Dictionary<ulong, string> _motion = new();
    private Dictionary<ulong, string> _weaponMotion = new();
    private Dictionary<ulong, string> _mountMotion = new();
    private Dictionary<ulong, string> _capeMotion = new();
    private Dictionary<ulong, string> _miscMotion = new();
    private Dictionary<ulong, string> _armetMotion = new();
    private Dictionary<ulong, string> _spiritMotion = new();
    private Dictionary<ulong, string> _headMotion = new();
    private Dictionary<ulong, string> _pelvisMotion = new();

    // ── Public properties ─────────────────────────────────────────────────────
    public IReadOnlyList<RolePart> RoleParts => _roleParts;

    public IReadOnlyDictionary<ulong, string> MeshMap => _mesh;
    public IReadOnlyDictionary<ulong, string> TextureMap => _tex;
    public IReadOnlyDictionary<ulong, string> MotionMap => _motion;
    public IReadOnlyDictionary<ulong, string> WeaponMotionMap => _weaponMotion;
    public IReadOnlyDictionary<ulong, string> MountMotionMap => _mountMotion;
    public IReadOnlyDictionary<ulong, string> CapeMotionMap => _capeMotion;
    public IReadOnlyDictionary<ulong, string> MiscMotionMap => _miscMotion;
    public IReadOnlyDictionary<ulong, string> ArmetMotionMap => _armetMotion;
    public IReadOnlyDictionary<ulong, string> SpiritMotionMap => _spiritMotion;
    public IReadOnlyDictionary<ulong, string> HeadMotionMap => _headMotion;
    public IReadOnlyDictionary<ulong, string> PelvisMotionMap => _pelvisMotion;
       
    // ── Load ──────────────────────────────────────────────────────────────────

    public Task LoadAsync(string conquerPath) => Task.Run(() =>
    {
        var _iniPath = Path.Combine(conquerPath, "ini");
        using var src = new WdbIniSource(Path.Combine(_iniPath, "c3.wdb"));

        // ── Helper: return full path to an INI file inside the ini folder ──
        string Ini(string f) => Path.Combine(_iniPath, f);
        // ── Stream-based Parsers via WDB / DBC / Plain INI Fallback ──────────
        // Role parts
        _roleParts.Clear();
        _roleParts.AddRange(ParseFromWdb(src, Ini("Armor.ini"), r => RolePartIniParser.Parse(r, RolePartType.Armor)));
        _roleParts.AddRange(ParseFromWdb(src, Ini("Armet.ini"), r => RolePartIniParser.Parse(r, RolePartType.Armet)));
        _roleParts.AddRange(ParseFromWdb(src, Ini("Weapon.ini"), r => RolePartIniParser.Parse(r, RolePartType.Weapon)));
        _roleParts.AddRange(ParseFromWdb(src, Ini("Mount.ini"), r => RolePartIniParser.Parse(r, RolePartType.Mount)));
        _roleParts.AddRange(ParseFromWdb(src, Ini("Cape.ini"), r => RolePartIniParser.Parse(r, RolePartType.Cape)));
        _roleParts.AddRange(ParseFromWdb(src, Ini("Head.ini"), r => RolePartIniParser.Parse(r, RolePartType.Head)));
        _roleParts.AddRange(ParseFromWdb(src, Ini("Misc.ini"), r => RolePartIniParser.Parse(r, RolePartType.Misc)));
        _roleParts.AddRange(ParseFromWdb(src, Ini("Pelvis.ini"), r => RolePartIniParser.Parse(r, RolePartType.Pelvis)));
        _roleParts.AddRange(ParseFromWdb(src, Ini("Spirit.ini"), r => RolePartIniParser.Parse(r, RolePartType.Spirit)));

        // ── ResIni Maps (Using Generic ParseFromWdb Helper) ───────────────────
        _mesh = ParseFromWdb(src, Ini("3dobj.ini"), ResIniParser.Parse);
        _tex = ParseFromWdb(src, Ini("3dtexture.ini"), ResIniParser.Parse);
        _motion = ParseFromWdb(src, Ini("3dmotion.ini"), ResIniParser.Parse);
        _weaponMotion = ParseFromWdb(src, Ini("WeaponMotion.ini"), ResIniParser.Parse);
        _mountMotion = ParseFromWdb(src, Ini("MountMotion.ini"), ResIniParser.Parse);
        _capeMotion = ParseFromWdb(src, Ini("capemotion.ini"), ResIniParser.Parse);
        _miscMotion = ParseFromWdb(src, Ini("miscmotion.ini"), ResIniParser.Parse);
        _armetMotion = ParseFromWdb(src, Ini("armetmotion.ini"), ResIniParser.Parse);
        _spiritMotion = ParseFromWdb(src, Ini("spiritmotion.ini"), ResIniParser.Parse);
        _headMotion = ParseFromWdb(src, Ini("headmotion.ini"), ResIniParser.Parse);
        _pelvisMotion = ParseFromWdb(src, Ini("pelvismotion.ini"), ResIniParser.Parse);
    });

    /// <summary>
    /// Opens a configuration file via <see cref="WdbIniSource"/> (prioritizing .dbc versions inside the WDB archive)
    /// with an automatic fallback to physical disk-bound .ini files if the archive structure is absent.
    /// </summary>
    private static T ParseFromWdb<T>(WdbIniSource src, string plainIniPath, Func<StreamReader, T> parseFunc) where T : new()
    {
        using var reader = src.OpenIni(plainIniPath);
        return reader is not null
            ? parseFunc(reader)
            : new T();
    }

    // ── Basic resolvers ───────────────────────────────────────────────────────

    public string? ResolveMesh(ulong id) => MeshMap.GetValueOrDefault(id);
    public string? ResolveTexture(ulong id) => TextureMap.GetValueOrDefault(id);
    public string? ResolveMotion(ulong motionId)
    {
        if (MotionMap.TryGetValue(motionId, out var direct)) return direct;

        var s = motionId.ToString();
        if (s.Length == 10)
        {
            var stripped = s[..6] + s[7..];
            if (ulong.TryParse(stripped, out var key2) && MotionMap.TryGetValue(key2, out var p2))
                return p2;
        }
        if (s.Length == 7)
        {
            var stretched = s.Insert(4, "0");
            if (ulong.TryParse(stretched, out var key3) && MotionMap.TryGetValue(key3, out var p3))
                return p3;
        }
        return null;
    }

    public string? ResolveWeaponMotion(ulong weaponId, int actionType)
    {
        weaponId = (weaponId / 10) * 10;

        ulong key = weaponId * 1000 + (ulong)actionType;
        if (WeaponMotionMap.TryGetValue(key, out var p1)) return p1;

        key = weaponId * 1000 + 999;
        if (WeaponMotionMap.TryGetValue(key, out var p2)) return p2;

        ulong categoryId = (weaponId / 1000) * 1000 + 999;
        key = categoryId * 1000 + (ulong)actionType;
        if (WeaponMotionMap.TryGetValue(key, out var p3)) return p3;

        key = categoryId * 1000 + 999;
        if (WeaponMotionMap.TryGetValue(key, out var p4)) return p4;

        return null;
    }

    public string? ResolveMountMotion(ulong mountId, int actionType)
    {
        mountId = (mountId / 10) * 10;

        ulong key = mountId * 1000 + (ulong)actionType;
        if (MountMotionMap.TryGetValue(key, out var p1)) return p1;

        key = mountId * 1000 + 999;
        if (MountMotionMap.TryGetValue(key, out var p2)) return p2;

        ulong categoryId = (mountId / 1000) * 1000 + 999;
        key = categoryId * 1000 + (ulong)actionType;
        if (MountMotionMap.TryGetValue(key, out var p3)) return p3;

        key = categoryId * 1000 + 999;
        if (MountMotionMap.TryGetValue(key, out var p4)) return p4;

        return null;
    }

    /// <summary>
    /// Generic resolver for the per-part-type motion maps (Cape, Misc, Armet, Spirit, Head, Pelvis).
    /// Key scheme: partId * 1000 + actionType, with a fallback to partId * 1000 + 999.
    /// </summary>
    private static string? ResolvePartMotion(IReadOnlyDictionary<ulong, string> map, ulong partId, int actionType)
    {
        ulong key = partId * 1000 + (ulong)actionType;
        if (map.TryGetValue(key, out var p1)) return p1;

        key = partId * 1000 + 999;
        if (map.TryGetValue(key, out var p2)) return p2;

        key = partId + (ulong)actionType;
        if (map.TryGetValue(key, out var p3)) return p3;

        key = partId + 999;
        if (map.TryGetValue(key, out var p4)) return p4;

        return null;
    }

    public string? ResolveCapeMotion(ulong capeId, int actionType)
        => ResolvePartMotion(_capeMotion, capeId, actionType);

    public string? ResolveMiscMotion(ulong miscId, int actionType)
        => ResolvePartMotion(_miscMotion, miscId, actionType);

    public string? ResolveArmetMotion(ulong armetId, int actionType)
        => ResolvePartMotion(_armetMotion, armetId, actionType);

    public string? ResolveSpiritMotion(ulong spiritId, int actionType)
        => ResolvePartMotion(_spiritMotion, spiritId, actionType);

    public string? ResolveHeadMotion(ulong headId, int actionType)
        => ResolvePartMotion(_headMotion, headId, actionType);

    public string? ResolvePelvisMotion(ulong pelvisId, int actionType)
        => ResolvePartMotion(_pelvisMotion, pelvisId, actionType);
        
    public RolePart? FindRolePart(uint id, RolePartType type)
    {
        foreach (var p in _roleParts)
            if (p.Id == id && p.PartType == type) return p;
        return null;
    }
}