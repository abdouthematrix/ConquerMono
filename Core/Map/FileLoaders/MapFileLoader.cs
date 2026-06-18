namespace ConquerMono.Map.FileLoaders;

public sealed class MapFileLoader : IMapFileLoader
{
    private enum ObjType { Scene = 1, TerrainObject = 4, Backdrop = 8, Effect = 10, Sound = 15 }

    public MapData Load(Stream stream)
    {
        using var r = new BinaryReader(stream);

        var map = new MapData
        {
            DMapHeader = r.ReadASCIIString(8),
            PuzzlePath = r.ReadASCIIString(260),
            Bounds     = r.ReadSize(),
        };
        map.Cells = new MapCellCollection(map.Bounds) { CollectionSize = map.Bounds };

        LoadCells(r, map);
        LoadPortals(r, map);
        LoadObjects(r, map);
        LoadLayers(r, map);

        return map;
    }

    // ── Cells ─────────────────────────────────────────────────────────────────
    private static void LoadCells(BinaryReader r, MapData map)
    {
        for (int y = 0; y < map.Bounds.Height; y++)
        {
            ulong checksum = 0;
            for (int x = 0; x < map.Bounds.Width; x++)
            {
                var cell = new MapCell
                {
                    Access  = (MapCellAccessType)r.ReadInt16(),
                    Surface = r.ReadInt16(),
                    Height  = r.ReadInt16()
                };
                map.Cells[x, y] = cell;
                checksum += (ulong)((int)cell.Access * (cell.Surface + y + 1) +
                                    (cell.Height + 2) * (x + 1 + cell.Surface));
            }
            var fileChecksum = r.ReadUInt32();
            if (fileChecksum != checksum)
                Debug.WriteLine("[Dmap] Checksum mismatch on row " + y);
        }
    }

    // ── Portals ───────────────────────────────────────────────────────────────
    private static void LoadPortals(BinaryReader r, MapData map)
    {
        int count = r.ReadInt32();
        for (int i = 0; i < count; i++)
        {
            var p = new MapPortal { Location = r.ReadPoint(), PortalType = r.ReadInt32() };
            map.Portals.Add(p);
            TrySetAccess(map, (int)p.Location.X, (int)p.Location.Y, MapCellAccessType.Portal);
        }
    }

    // ── Objects ───────────────────────────────────────────────────────────────
    private static void LoadObjects(BinaryReader r, MapData map)
    {
        int count = r.ReadInt32();
        for (int i = 0; i < count; i++)
        {
            var type = (ObjType)r.ReadInt32();
            switch (type)
            {
                case ObjType.Scene:
                {
                    var s = new MapScene { ScenePath = r.ReadASCIIString(260), Location = r.ReadPoint() };
                    map.Scenes.Add(s);
                    TrySetAccess(map, s.Location.X, s.Location.Y, MapCellAccessType.Scene);
                    break;
                }
                case ObjType.TerrainObject:
                {
                    var t = new MapTerrainObject
                    {
                        AniPath     = r.ReadASCIIString(260),
                        AniName     = r.ReadASCIIString(128),
                        Location    = r.ReadPoint(),
                        Size        = r.ReadSize(),
                        ImageOffset = r.ReadPoint(),
                        Interval    = r.ReadInt32()
                    };
                    map.TerrainObjects.Add(t);
                    TrySetAccess(map, t.Location.X, t.Location.Y, MapCellAccessType.Terrain);
                    break;
                }
                case ObjType.Effect:
                {
                    var e = new Map3DEffect { Effect = r.ReadASCIIString(64), Location = r.ReadPoint() };
                    map.Effects.Add(e);
                    var cell = map.Cells.World2Cell(e.Location.X, e.Location.Y);
                    TrySetAccess(map, (int)cell.X, (int)cell.Y, MapCellAccessType.Effect);
                    break;
                }
                case ObjType.Sound:
                {
                    var s = new MapSound
                    {
                        SoundPath = r.ReadASCIIString(260),
                        Location  = r.ReadPoint(),
                        Volume    = r.ReadInt32(),
                        Range     = r.ReadInt32(),
                        Interval  = 100
                    };
                    map.Sounds.Add(s);
                    var cell = map.Cells.World2Cell(s.Location.X, s.Location.Y);
                    TrySetAccess(map, (int)cell.X, (int)cell.Y, MapCellAccessType.Sound);
                    break;
                }
                default:
                    Debug.WriteLine($"[Dmap] Unknown object type {(int)type}");
                    break;
            }
        }
    }

    // ── Layers ────────────────────────────────────────────────────────────────
    private static void LoadLayers(BinaryReader r, MapData map)
    {
        int count = r.ReadInt32();
        for (int i = 0; i < count; i++)
        {
            var layer = new MapLayer
            {
                index     = r.ReadInt32(),
                layertype = r.ReadInt32(),
                xInt      = r.ReadInt32(),
                yInt      = r.ReadInt32(),
            };

            int objCount = r.ReadInt32();
            for (int j = 0; j < objCount; j++)
            {
                var type = (ObjType)r.ReadInt32();
                switch (type)
                {
                    case ObjType.Backdrop:
                        layer.Backdrops.Add(new MapBackdrop { PuzzlePath = r.ReadASCIIString(260) });
                        break;

                    case ObjType.TerrainObject:
                        layer.TerrainObjects.Add(new MapTerrainObject
                        {
                            AniPath     = r.ReadASCIIString(260),
                            AniName     = r.ReadASCIIString(128),
                            Location    = r.ReadPoint(),
                            Size        = r.ReadSize(),
                            ImageOffset = r.ReadPoint(),
                            Interval    = r.ReadInt32()
                        });
                        break;

                    default:
                        Debug.WriteLine($"[Dmap] Unknown layer object type {(int)type}");
                        break;
                }
            }
            map.Layers.Add(layer);
        }
    }

    // ── Helper ────────────────────────────────────────────────────────────────
    private static void TrySetAccess(MapData map, int x, int y, MapCellAccessType type)
    {
        try   { map.Cells[x, y].Access = type; }
        catch (IndexOutOfRangeException) { Debug.WriteLine($"[Dmap] Cell ({x},{y}) out of bounds"); }
    }
}
