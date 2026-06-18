namespace ConquerMono.Conquer.Services;

/// <summary>
/// Orchestrates loading a map file and all associated puzzle / backdrop assets.
/// Returns the fully populated <see cref="MapData"/> and the primary <see cref="Puzzle"/>.
/// </summary>
public sealed class MapLoadingService
{
    private readonly IPackageReader    _pkg;
    private readonly IMapFileLoader    _mapLoader;
    private readonly IPuzzleFileLoader _puzzleLoader;

    public MapLoadingService(
        IPackageReader    pkg,
        IMapFileLoader    mapLoader,
        IPuzzleFileLoader puzzleLoader)
    {
        _pkg          = pkg;
        _mapLoader    = mapLoader;
        _puzzleLoader = puzzleLoader;
    }

    /// <summary>
    /// Load and return (MapData, primary Puzzle).
    /// <paramref name="tileSize"/> is used as the fallback tile size when the
    /// puzzle file itself cannot determine it.
    /// </summary>
    public (MapData Map, Puzzle Puzzle) LoadMap(string path, int tileSize)
    {
        // ── Main map ──────────────────────────────────────────────────────────
        var mapData = _mapLoader.Load(_pkg.LoadFile(path));
        mapData.Cells.CellDepth = 32;
        mapData.Cells.CellWidth = 64;

        // ── Primary puzzle ────────────────────────────────────────────────────
        var puzzle = _puzzleLoader.Load(_pkg.LoadFile(mapData.PuzzlePath));

        // Auto-detect tile size from the first puzzle tile when possible
        int detected = _puzzleLoader.GetTileSize(puzzle, _pkg);
        // Fallback chain: detected → caller-supplied → standard Conquer tile size (64)
        puzzle.TileSize = detected > 0 ? detected
                        : tileSize  > 0 ? tileSize
                        : 64;

        // ── Backdrop puzzles ──────────────────────────────────────────────────
        foreach (var layer in mapData.Layers)
        foreach (var backdrop in layer.Backdrops)
        {
            try
            {
                var bp = _puzzleLoader.Load(_pkg.LoadFile(backdrop.PuzzlePath));
                bp.TileSize        = puzzle.TileSize;
                bp.HorizontalRate  = layer.xInt;
                bp.VerticalRate    = layer.yInt;
                backdrop.Puzzle    = bp;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MapLoadingService] backdrop {backdrop.PuzzlePath}: {ex.Message}");
            }
        }

        return (mapData, puzzle);
    }
}
