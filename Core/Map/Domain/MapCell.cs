namespace ConquerMono.Map.Domain;

public sealed class MapCell
{
    public MapCellAccessType Access  { get; set; }
    public short             Surface { get; set; }
    public short             Height  { get; set; }

    /// <summary>Colour used by MapCellDrawingComponent debug overlay.</summary>
    public Color AccessColor => Access switch
    {
        MapCellAccessType.Walkable => new Color(  0, 220,   0,  70),
        MapCellAccessType.Blocked  => new Color(220,   0,   0,  90),
        MapCellAccessType.Portal   => new Color(255, 255,   0, 160),
        MapCellAccessType.Scene    => new Color(  0,  80, 255,  80),
        MapCellAccessType.Terrain  => new Color(  0, 220, 220,  80),
        MapCellAccessType.Effect   => new Color(255, 140,   0,  80),
        MapCellAccessType.Sound    => new Color(160,   0, 255,  80),
        _                          => Color.Gray,
    };

    /// <summary>
    /// A cell is impassable only when its access flag is explicitly <see cref="MapCellAccessType.Blocked"/>.
    /// Portals, scenes, terrain markers, effects, and sounds are logical markers —
    /// the player can walk through them.
    /// </summary>
    public bool IsWalkable => Access != MapCellAccessType.Blocked;
}
