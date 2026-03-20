using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using ConquerMono.Components;
using ConquerMono.Core;
using ConquerMono.World;

namespace ConquerMono;

/// <summary>
/// Root game class.  Owns shared resources that DrawableGameComponents read via
/// the typed Game reference passed to their constructors.
/// </summary>
public class ConquerGame : Game
{
    private readonly GraphicsDeviceManager _graphics;

    // ── Shared resources (read by components) ────────────────────────────────
    public SpriteBatch  SpriteBatch { get; private set; } = null!;
    public IsometricCamera Camera  { get; private set; } = null!;
    public GameMap      Map        { get; private set; } = null!;
    public PlayerEntity Player     { get; private set; } = null!;
    public InputManager Input      { get; private set; } = null!;

    public ConquerGame()
    {
        _graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth  = 1280,
            PreferredBackBufferHeight = 720,
            SynchronizeWithVerticalRetrace = true,
        };
        Content.RootDirectory = "Content";
        IsMouseVisible  = true;
        IsFixedTimeStep = true;
        TargetElapsedTime = TimeSpan.FromSeconds(1.0 / 60.0);
    }

    // ─────────────────────────────────────────────────────────────────────────
    protected override void Initialize()
    {
        Window.Title = "ConquerMono  ·  2.5D Action RPG";

        // Core systems
        Input  = new InputManager();
        Map    = new GameMap(50, 50);
        Player = new PlayerEntity(new Vector2(25, 25));
        Camera = new IsometricCamera(
            _graphics.PreferredBackBufferWidth,
            _graphics.PreferredBackBufferHeight);
        Camera.SetPosition(Player.TilePosition);

        // DrawableGameComponents — drawn in DrawOrder order
        Components.Add(new MapComponent(this)    { DrawOrder = 0  });
        Components.Add(new PlayerComponent(this) { DrawOrder = 10 });
        Components.Add(new HudComponent(this)    { DrawOrder = 20 });

        base.Initialize();   // triggers component.Initialize() → component.LoadContent()
    }

    protected override void LoadContent()
    {
        SpriteBatch = new SpriteBatch(GraphicsDevice);
        base.LoadContent();
    }

    // ─────────────────────────────────────────────────────────────────────────
    protected override void Update(GameTime gameTime)
    {
        Input.Update();
        if (Input.IsPressed(Keys.Escape)) Exit();

        // ── Directional input (WASD / Arrow keys) ────────────────────────────
        var dir = Vector2.Zero;
        if (Input.IsHeld(Keys.W) || Input.IsHeld(Keys.Up))    dir.Y -= 1;
        if (Input.IsHeld(Keys.S) || Input.IsHeld(Keys.Down))  dir.Y += 1;
        if (Input.IsHeld(Keys.A) || Input.IsHeld(Keys.Left))  dir.X -= 1;
        if (Input.IsHeld(Keys.D) || Input.IsHeld(Keys.Right)) dir.X += 1;

        float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
        Player.Update(dir, dt, Map);
        Camera.SetPosition(Player.TilePosition);

        base.Update(gameTime);   // updates all components
    }

    // ─────────────────────────────────────────────────────────────────────────
    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(new Color(12, 10, 22));   // deep night sky
        base.Draw(gameTime);                            // draws all components
    }
}
