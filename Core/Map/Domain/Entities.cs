namespace ConquerMono.Map.Domain;

public sealed class MapPortal
{
    public MapPoint Location   { get; set; }
    public int      PortalType { get; set; }
}

public sealed class MapScene
{
    public string   ScenePath { get; set; } = string.Empty;
    public MapPoint Location  { get; set; }
}

public sealed class MapTerrainObject
{
    public string   AniPath     { get; set; } = string.Empty;
    public string   AniName     { get; set; } = string.Empty;
    public MapPoint Location    { get; set; }
    public MapSize  Size        { get; set; }
    public MapPoint ImageOffset { get; set; }
    public int      Interval    { get; set; }
}

public sealed class Map3DEffect
{
    public string   Effect   { get; set; } = string.Empty;
    public MapPoint Location { get; set; }
}

public sealed class MapSound
{
    public string   SoundPath { get; set; } = string.Empty;
    public MapPoint Location  { get; set; }
    public int      Volume    { get; set; }
    public int      Range     { get; set; }
    public int      Interval  { get; set; }
}

public sealed class MapBackdrop
{
    public string  PuzzlePath { get; set; } = string.Empty;
    public Puzzle? Puzzle     { get; set; }
}

public sealed class MapLayer
{
    public int                    index          { get; set; }
    public int                    layertype      { get; set; }
    public int                    xInt           { get; set; }
    public int                    yInt           { get; set; }
    public List<MapBackdrop>      Backdrops      { get; set; } = new();
    public List<MapTerrainObject> TerrainObjects { get; set; } = new();
}

public sealed class Scene
{
    public List<ScenePart> SceneParts { get; set; } = new();
}

public sealed class ScenePart
{
    public string   AniPath     { get; set; } = string.Empty;
    public string   AniName     { get; set; } = string.Empty;
    public MapPoint ImageOffset { get; set; }
    public int      Interval    { get; set; }
    public MapSize  Size        { get; set; }
    public int      Thick       { get; set; }
    public MapPoint Location    { get; set; }
    public int      Height      { get; set; }
    public MapCell[,] Cells     { get; set; } = new MapCell[0, 0];
}

public sealed class GameMap
{
    public int    Id       { get; set; }
    public string Path     { get; set; } = string.Empty;
    public int    TileSize { get; set; }

    public string DisplayName =>
        $"{System.IO.Path.GetFileNameWithoutExtension(Path)} (ID: {Id})";
}

public enum DrawingAspect
{
    Backdrop,
    BackdropGrid,
    Puzzle,
    MapCell,
    Portals,
    Scene,
    TerrainObject,
    PuzzleGrid,
    TerrainObjectGrid,
    SceneGrid,
    Effect,
    Sound,
}
