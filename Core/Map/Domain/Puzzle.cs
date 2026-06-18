namespace ConquerMono.Map.Domain;

public sealed class Puzzle
{
    public string  PuzzleType      { get; set; } = string.Empty;
    public string  AniPath         { get; set; } = string.Empty;
    public int     HorizontalTiles { get; set; }
    public int     VerticalTiles   { get; set; }
    public short[,] Tiles          { get; set; } = new short[0, 0];
    public int     TileSize        { get; set; }
    public int?    HorizontalRate  { get; set; }
    public int?    VerticalRate    { get; set; }

    /// <summary>Total puzzle width in pixels.</summary>
    public int Width  => HorizontalTiles * TileSize;
    /// <summary>Total puzzle height in pixels.</summary>
    public int Height => VerticalTiles   * TileSize;
}
