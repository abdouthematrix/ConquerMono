namespace ConquerMono.Map.Domain;

public sealed class MapCellCollection
{
    public MapCell[,] CellData        { get; }
    public MapSize    CollectionSize  { get; set; }
    public int        CellWidth       { get; set; } = 64;
    public int        CellDepth       { get; set; } = 32;

    public MapCellCollection(MapSize size)
    {
        CellData        = new MapCell[size.Width, size.Height];
        CollectionSize  = size;
    }

    public MapCell this[int x, int y]
    {
        get => CellData[x, y];
        set => CellData[x, y] = value;
    }

    public int m_posOriginX => CellWidth  * CollectionSize.Width  / 2;
    public int m_posOriginY => CellDepth  / 2;

    public Vector2 World2Cell(int worldX, int worldY)
    {
        worldX -= m_posOriginX;
        worldY -= m_posOriginY;

        double dx = worldX, dy = worldY;
        double cw = CellWidth,  ch = CellDepth;

        double t0 = (dx / cw) + (dy / ch);
        double t1 = (dy / ch) - (dx / cw);

        return new Vector2(Double2Int(t0), Double2Int(t1));
    }

    public Vector2 Cell2World(int cellX, int cellY) => new(
        CellWidth  * (cellX - cellY) / 2 + m_posOriginX,
        CellDepth  * (cellX + cellY) / 2 + m_posOriginY);

    private static int Double2Int(double value) =>
        (int)(value + 0.5) > (int)value ? (int)value + 1 : (int)value;
}
