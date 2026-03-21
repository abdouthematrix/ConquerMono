namespace ConquerMono.Domain;

public sealed class MapData
{
    public string             DMapHeader    { get; set; } = string.Empty;
    public string             PuzzlePath    { get; set; } = string.Empty;
    public MapSize            Bounds        { get; set; }
    public MapCellCollection  Cells         { get; set; } = null!;
    public List<MapPortal>    Portals       { get; set; } = new();
    public List<MapScene>     Scenes        { get; set; } = new();
    public List<MapTerrainObject> TerrainObjects { get; set; } = new();
    public List<Map3DEffect>  Effects       { get; set; } = new();
    public List<MapSound>     Sounds        { get; set; } = new();
    public List<MapLayer>     Layers        { get; set; } = new();
}
