namespace ConquerMono.Map.Domain;

public enum MapCellAccessType : short
{
    Walkable = 0,
    Blocked  = 1,
    Portal   = 2,
    Scene    = 3,
    Terrain  = 4,
    Effect   = 5,
    Sound    = 6,
}
