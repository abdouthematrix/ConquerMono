# ConquerMono — 2.5D Isometric Action RPG

A MonoGame prototype inspired by *Conquer Online*, featuring:

- **Isometric tile map** rendered in 2D via `SpriteBatch` (painter's algorithm)
- **3D player mesh** rendered with `BasicEffect` + `VertexPositionColor`, composited over the 2D scene
- **Unified camera** — one set of maths drives both the 2D tile offset and the matching 3D View/Projection matrices
- **Zero external assets** — all textures and meshes are generated procedurally at startup
- **Modular `DrawableGameComponent` architecture** — each system is self-contained

---

## Requirements

| Tool | Version |
|------|---------|
| .NET SDK | 8.0+ |
| MonoGame | 3.8.1 (pulled from NuGet automatically) |

---

## Build & Run

```bash
cd ConquerMono
dotnet run
```

Controls: **WASD** or **Arrow Keys** to move · **Escape** to quit

---

## Architecture

```
ConquerGame          Root game — owns shared resources (SpriteBatch, Camera, Map, Player, Input)
│
├── Core/
│   ├── IsometricCamera   Converts tile↔screen (2D) and provides View/Projection (3D)
│   └── InputManager      Per-frame keyboard edge detection
│
├── World/
│   ├── GameMap           50×50 tile grid; procedural terrain via layered sine waves
│   ├── PlayerEntity      Position, stats, movement with axis-sliding collision
│   └── TileType          Grass | Road | Water | Stone | Sand
│
└── Components/           DrawableGameComponent subclasses (drawn in DrawOrder order)
    ├── MapComponent       DrawOrder=0   SpriteBatch pass — isometric tiles
    ├── PlayerComponent    DrawOrder=10  Shadow (SpriteBatch) + 3D humanoid mesh (BasicEffect)
    └── HudComponent       DrawOrder=20  SpriteBatch pass — bars, minimap, pixel digits
```

---

## Camera Mathematics

The 2D tile→screen formula (tile half-width = 32, half-height = 16):

```
screen.X = (tx − ty) × 32 + offsetX
screen.Y = (tx + ty) × 16 + offsetY
```

For the 3D camera to produce the same screen position for a mesh at world `(tx, 0, tz)`,
an orthographic camera placed at eye `(d, d·√(2/3), d)` relative to the target satisfies
the 2∶1 tile ratio exactly. The projection scales so that:

```
1 world unit  ≡  TileHalfW × √2  ≈  45.25 pixels
```

---

## Draw Pipeline (each frame)

```
GraphicsDevice.Clear(background)
│
├── MapComponent.Draw          SpriteBatch.Begin … tile quads … End
├── PlayerComponent.Draw
│   ├── SpriteBatch.Begin … blob shadow … End
│   ├── GraphicsDevice.Clear(DepthBuffer only)
│   └── BasicEffect … indexed box mesh …
└── HudComponent.Draw          SpriteBatch.Begin … bars, minimap, digits … End
```

---

## Extending the Game

### Replace the procedural player mesh with a real model

```csharp
// In PlayerComponent.LoadContent():
_model = Game.Content.Load<Model>("Characters/Warrior");
```

Add the `.fbx` (or `.x`) to `Content/Characters/Warrior.fbx` and reference it in `Content.mgcb`.

### Add animated sprites for NPC enemies

Create an `NpcComponent : DrawableGameComponent` that holds a `Texture2D` sprite sheet,
increments a frame counter in `Update`, and draws the current frame via `SpriteBatch`.
Use the same `IsometricCamera.TileToScreen()` helper to place each NPC.

### Add more tile types

1. Add the enum value to `TileType`.
2. Add a colour entry to `MapComponent._textures` dictionary.
3. Update `GameMap.Generate()` to use the new type.
4. Update `HudComponent.BuildMinimapTexture()` colour switch.

### Add elevation / height

Offset the tile's screen Y by `−height * TileHalfH` and shift the 3D mesh's
world Y accordingly. Both coordinate systems will remain in sync automatically.
