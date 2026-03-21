# ConquerMono  ·  2.5D Isometric Action RPG

A MonoGame project that loads and renders **real Conquer Online maps** (`.dmap` + WDF packages)
while displaying the player as a **3-D humanoid mesh** composited over the authentic 2-D tile map.

---

## Requirements

| Dependency | Version |
|---|---|
| .NET SDK | 8.0+ |
| MonoGame | 3.8.1 (pulled via NuGet) |
| Conquer Online client | Any version with `c3.wdf` / `data.wdf` and `ini/gamemap.dat` |

---

## First-run setup

On first launch (or whenever `%AppData%\ConquerMono\settings.json` is missing)
the game will display a setup message in the window title.

Edit `%AppData%\ConquerMono\settings.json`:

```json
{
  "ConquerDirectory": "C:\\Games\\Conquer",
  "GameMapFilePath":  "C:\\Games\\Conquer\\ini\\gamemap.dat",
  "DefaultMapId": 1006,
  "DefaultZoom": 0.5,
  "LastMapPath": ""
}
```

Then run again — the default map (ID 1006 = Twin City) will load automatically.

---

## Build & Run

```bash
cd ConquerMono
dotnet run
```

---

## Controls

| Input | Action |
|---|---|
| W / A / S / D | Move player |
| Right-drag mouse | Pan camera |
| Scroll wheel | Zoom in / out |
| H or Home | Reset camera view |
| F | Fit map to window |
| Shift + Arrow | Fast camera pan |
| 1 – 8 | Toggle rendering layers |
| Escape | Quit |

### Layer toggles (number keys)

| Key | Layer |
|---|---|
| 1 | Backdrop |
| 2 | Puzzle (ground tiles) |
| 3 | Scenes (animated objects from .scene files) |
| 4 | Terrain objects |
| 5 | Portals |
| 6 | Cell access debug overlay |
| 7 | Puzzle tile grid overlay |
| 8 | Terrain object bounding grid |

---

## Architecture

```
ConquerMono/
│
├── Domain/                         Pure data models (no MonoGame dependency)
│   ├── MapData, MapCell, MapCellCollection, Puzzle, …
│   └── Entities.cs                 MapPortal, MapScene, MapTerrainObject, …
│
├── Interfaces/                     Contracts (IPackageReader, IAniDictionary, …)
│
├── Infrastructure/
│   ├── Animation/                  AniParser, AnimationIndex, AniDictionary
│   ├── FileLoaders/                MapFileLoader, PuzzleFileLoader, SceneFileLoader
│   ├── FileSystem/                 WdfPackageReader, TqPackageReader
│   ├── Graphics/                   DDSHelper, TGAHelper, TextureCache
│   └── Repositories/               GameMapRepository (gamemap.dat index)
│
├── Rendering/
│   ├── Coordinates/                IsometricCoordinateSystem (cell ↔ pixel)
│   ├── Drawing/                    All IDrawingComponent implementations
│   │   ├── BaseDrawingComponent
│   │   ├── PuzzleDrawingComponent
│   │   ├── BackdropDrawingComponent
│   │   ├── ObjectDrawingComponents  (Portal, Scene, TerrainObject)
│   │   └── DebugDrawingComponents   (MapCell, grids, Effect, Sound)
│   └── Primitives/                 CellVertexBuilder (DynamicVertexBuffer lines)
│
├── Services/
│   ├── MapLoadingService           Loads .dmap + puzzles + backdrops
│   └── MapViewerService            Owns all IDrawingComponent lists + GameCamera
│
├── Core/
│   ├── GameCamera                  Unified 2D scroll + 3D View/Projection matrices
│   └── InputAndSettings.cs         InputManager + GameSettings (JSON)
│
├── World/
│   └── PlayerEntity                Cell-space position, stats, WASD movement
│
├── Components/                     DrawableGameComponent wrappers (MonoGame lifecycle)
│   ├── MapRenderComponent  [0]
│   ├── PlayerComponent    [10]
│   └── HudComponent       [20]
│
└── ConquerGame.cs                  Root — wires everything together
```

---

## Coordinate systems

```
Cell space          integer (x, y) indices into MapCellCollection
       ↕  IsometricCoordinateSystem.MapToScreen / ScreenToMap
Puzzle space        pixel (x, y) within the full puzzle image
       ↕  GameCamera.TransformMatrix   (scale + scroll offset)
Viewport space      pixel (x, y) on screen

3-D world space     (cellX, 0, cellY) — directly equals cell space
       ↕  GameCamera.ViewMatrix + ProjectionMatrix
Clip space          fed to BasicEffect for the player mesh
```

The key invariant: `IsometricCoordinateSystem.MapToScreen(cell)` and the 3-D projection of
`Vector3(cell.X, 0, cell.Y)` land on **the same viewport pixel**.  
This is guaranteed by calibrating the orthographic projection to `PixelsPerUnit = TileHalfW × √2`.

---

## Extending

### Load a different map at runtime

```csharp
var map = game.MapRepo?.GetById(1038); // Market
if (map != null) game.LoadMap(map);
```

### Add a new rendering layer

1. Implement `BaseDrawingComponent` in `Rendering/Drawing/`.
2. Add a value to the `DrawingAspect` enum in `Domain/Entities.cs`.
3. Register the component inside `MapViewerService.BuildComponents()`.
4. Add a toggle call in `HudComponent.Update()`.

### Replace the procedural player mesh with a real model

In `PlayerComponent.LoadContent()`, replace `BuildMesh()` with:

```csharp
_model = Game.Content.Load<Model>("Characters/Warrior");
```

Add `Characters/Warrior.fbx` to `Content/` and reference it in `Content.mgcb`.
In `Draw()`, call `_model.Draw(_effect.World, cam.ViewMatrix, cam.ProjectionMatrix)`.

### Enable sound playback

`MapData.Sounds` already lists every ambient sound with path, volume, and range.
Wire `Microsoft.Xna.Framework.Audio.SoundEffect` loading through `IPackageReader`
and trigger playback when the player enters a sound's range circle.

---

## Audit changelog (applied during review passes)

### Coordinate system
- **`ScreenToMap` fixed** — was a simplified formula ignoring puzzle centre and map height offsets; replaced with exact algebraic inverse of `MapToScreen`
- **`TransformMatrix` double-offset fixed** — was `Translate(-pos)×Scale(zoom)` but components already subtract `DrawWindow.X/Y`; corrected to `Scale(zoom)` only

### Walkability
- **`MapCell.IsWalkable` broadened** — changed from `== Walkable` to `!= Blocked` so portals, scenes, terrain markers, effects, and sounds are all passable by the player

### Frame update ordering
- **`MapRenderComponent.UpdateOrder` raised to 20** — `UpdateAllComponents` now runs after `PlayerComponent` (10) has moved the player and updated the camera, eliminating a one-frame cull lag

### Camera tracking
- **`GameCamera.TrackCell()`** — added method so the 3-D View matrix follows the player's cell each frame; the mesh now projects to the exact screen pixel as the 2-D tile shadow

### Robustness
- **`TileSize` fallback chain** — `0 → auto-detect → caller-supplied → 64` so maps always render even when `gamemap.dat` has no tile size
- **Redundant `GraphicsDevice.Clear` removed** from `MapRenderComponent.Draw`
- **`WalkPhase` and portal `_time`** clamped with `% Tau` / `% 600f` to prevent float precision loss
- **`goto` replaced** with `FindSpawnCell()` helper in `ConquerGame.LoadMap`
- **Arrow-key conflict resolved** — removed camera pan from arrow keys; player uses WASD+arrows exclusively, camera uses scroll wheel and right-drag
