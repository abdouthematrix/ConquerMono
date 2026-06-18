namespace ConquerMono.Core.Components;

public sealed class PlayerComponent : DrawableGameComponent
{
    private readonly ConquerGame _game;
    private readonly IGameDataService _gameData;
    private readonly TqPackageReader _assetLoader;

    // ── Modular Rendering Core ────────────────────────────────────────────
    private C3Role? _role;
    private IRoleAppearance? _currentAppearance;
    private MovementState _lastState = (MovementState)(-1);

    // ── Time & Shadows ────────────────────────────────────────────────────
    private double _frameTimer;
    private readonly double _secondsPerFrame = 1.0 / 15.0; // Conquer standard animation speed
    private Texture2D _shadow = null!;

    public PlayerComponent(ConquerGame game, IGameDataService gameData, TqPackageReader assetLoader) : base(game)
    {
        _game = game;
        _gameData = gameData;
        _assetLoader = assetLoader;
        DrawOrder = 10;
        UpdateOrder = 10;
    }

    protected override void LoadContent()
    {
        _role = new C3Role();
        _role.Initialize(GraphicsDevice);
        BuildShadow();
    }

    // ── Equipment Update ──────────────────────────────────────────────────
    public void UpdateAppearance(IRoleAppearance appearance)
    {
        if (_role == null) return;

        bool motionRefreshRequired = false;

        // Base Body & Armor
        if (_currentAppearance == null || _currentAppearance.Look != appearance.Look || _currentAppearance.ArmorId != appearance.ArmorId)
        {
            uint armorItemId = (appearance.Look * 1_000_000) + appearance.ArmorId;
            SyncSlot("Armor", armorItemId, RolePartType.Armor);
            motionRefreshRequired = true;
        }
        // Weapons & Headgear
        if (_currentAppearance?.ArmetId != appearance.ArmetId)
        {
            SyncSlot("Armet", appearance.ArmetId, RolePartType.Armet);
        }
        if (_currentAppearance?.RWeaponId != appearance.RWeaponId)
        {
            SyncSlot("RWeapon", appearance.RWeaponId, RolePartType.Weapon);
            motionRefreshRequired = true;
        }
        if (_currentAppearance?.LWeaponId != appearance.LWeaponId)
        {
            SyncSlot("LWeapon", appearance.LWeaponId, RolePartType.Weapon);
            motionRefreshRequired = true;
        }
        if (_currentAppearance?.MountId != appearance.MountId)
        {
            SyncSlot("Mount", appearance.MountId, RolePartType.Mount);
            motionRefreshRequired = true;
        }

        _currentAppearance = appearance;

        if (motionRefreshRequired) RefreshMotion();
    }

    private void SyncSlot(string slotName, uint itemId, RolePartType type)
    {
        if (itemId == 0)
        {
            _role?.ClearSlot(slotName);
            return;
        }

        var def = _gameData.FindRolePart(itemId, type);
        if (def == null) return;

        string? mesh = _gameData.ResolveMesh(def.MeshIds[0]);
        string? tex = _gameData.ResolveTexture(def.TextureIds[0]);

        if (mesh != null)
        {
            var part = LoadPart(mesh, tex, slotName, def.Id, def.Asb[0], def.Adb[0]);
            if (part != null)
            {
                part.Initialize(GraphicsDevice);
                _role?.AssignSlot(part);
            }
        }
    }

    // ── Motion Handling ───────────────────────────────────────────────────
    private void RefreshMotion()
    {
        if (_role == null || _game.Player == null) return;

        RoleActionType action = _game.Player.State switch
        {
            MovementState.Walking => RoleActionType.WalkL,
            MovementState.Running => RoleActionType.RunL,
            MovementState.Jumping => RoleActionType.Jump,
            _ => RoleActionType.StandBy
        };

        ChangeMotion(action);        
    }

    private void ChangeMotion(RoleActionType action)
    {
        uint look = _currentAppearance?.Look ?? 0;
        uint actionValue = (uint)action;
        uint rWeaponId = _currentAppearance?.RWeaponId ?? 0;
        uint lWeaponId = _currentAppearance?.LWeaponId ?? 0;
        uint rawMountId = _currentAppearance?.MountId ?? 0;

        uint mountId = ComputeMountId(rawMountId);
        uint weaponId = ComputeWeaponId(rWeaponId, lWeaponId);

        var motionPath = ResolveMotionWithFallback(mountId, look, weaponId, actionValue);
        if (string.IsNullOrEmpty(motionPath) || _assetLoader == null)
            return;

        using var stream = _assetLoader.LoadFile(motionPath);
        _role?.ChangeMotion(stream, "Body");
        
        var mountmotionPath = _gameData.ResolveMountMotion(rawMountId, (int)actionValue);
        if (!string.IsNullOrEmpty(mountmotionPath) && _assetLoader != null)
        {
            using var stream2 = _assetLoader.LoadFile(mountmotionPath);
            _role?.ChangeMotion(stream2, "Mount");
        }
        _role?.Calculate();
    }

    // Mount component of the key. Raw mount ids encode a type group in their
    // digits; we only need that group (divide by 10_000, mod 100), scaled
    // into the key's mount slot (x1000). No mount -> 0.
    private static uint ComputeMountId(uint rawMountId)
    {
        if (rawMountId == 0)
            return 0;

        return ((rawMountId / 10_000) % 100) * 1000;
    }

    // Weapon component of the key, derived from equipped right/left hand
    // item ids. These rules mirror the original item-id -> motion category
    // mapping; treat the magic numbers as fixed unless the item schema changes.
    private static uint ComputeWeaponId(uint rWeaponId, uint lWeaponId)
    {
        bool hasRight = rWeaponId != 0;
        bool hasLeft = lWeaponId != 0;

        if (!hasRight && !hasLeft)
            return 0;

        // Single or two-handed weapon, right hand only.
        if (hasRight && !hasLeft)
            return (rWeaponId % 1_000_000) / 1000;

        // Off-hand item only (shield/arrows), no right-hand weapon.
        if (!hasRight && hasLeft)
        {
            var category = (lWeaponId % 1_000_000) / 1000;
            return category == 50 ? 500u : 741u; // 50 = arrow, otherwise generic off-hand
        }

        // Both hands occupied: dual wield, weapon + shield, or bow.
        uint weaponId;
        if (((rWeaponId % 1_000_000) / 100_000) == 9) // shield in right-hand slot
        {
            weaponId = 700 + ((rWeaponId % 1_000_000) / 10_000);
        }
        else if (((lWeaponId % 1_000_000) / 100_000) == 4) // paired single-hand weapons
        {
            weaponId = 600
                + ((rWeaponId % 100_000) / 10_000) * 10
                + ((lWeaponId % 100_000) / 10_000);
        }
        else
        {
            weaponId = (rWeaponId % 1_000_000) / 1000;
        }

        if (rWeaponId / 1000 == 500) // bow overrides everything above
            weaponId = 500;

        return weaponId;
    }

    // Packs [mount][look][weapon][action] into fixed-width decimal slots.
    // E.g. weaponId 41 * 10_000 = 410000; combined with standby (action 100)
    // gives a final key of 410100.
    private static ulong BuildMotionKey(uint mountId, uint look, uint weaponId, uint action)
        => mountId * 10_000_000UL
         + look * 10_000_000UL
         + weaponId * 10_000UL
         + action;

    // Tries progressively less specific keys until one resolves to a real
    // motion file. Order matters: most specific first, falling back through
    // weapon then mount then action, in that priority.
    private string ResolveMotionWithFallback(uint mountId, uint look, uint weaponId, uint action)
    {
        uint genericMount = mountId > 0 ? 1000u : 0u;

        var attempts = new (uint mount, uint weapon, uint action)[]
        {
        (mountId,       weaponId, action), // 1. exact match
        (genericMount,  weaponId, action), // 2. generic mount category
        (mountId,       0,        action), // 3. exact mount, no weapon
        (genericMount,  0,        action), // 4. generic mount, no weapon
        (0,             weaponId, action), // 5. no mount
        (0,             weaponId, 100),    // 6. no mount, generic "standby" action        
        (0,             0,        action), // 7. no mount, no weapon
        (0,             0,        100),    // 8. no mount, no weapon, generic action
        };

        foreach (var (mount, weapon, act) in attempts)
        {
            var motionkey = BuildMotionKey(mount, look, weapon, act);
            var path = _gameData.ResolveMotion(motionkey);
            if (!string.IsNullOrEmpty(path))
                return path;
        }

        return null;
    }

    // ── Game Loop Updates (RESTORED INPUT AND CAMERA LOGIC!) ──────────────
    public override void Update(GameTime gt)
    {
        var input = _game.Input;
        var player = _game.Player;
        if (input == null || player == null || _role == null) return;

        // 1. Sync Appearance if it changed
        if (player is IRoleAppearance app) UpdateAppearance(app);

        var s = _game.Settings;
        bool shift = input.IsHeld(Keys.LeftShift) || input.IsHeld(Keys.RightShift);
        bool ctrl = input.IsHeld(Keys.LeftControl) || input.IsHeld(Keys.RightControl);
        var viewer = _game.MapViewer;

        // 2. Jump Logic
        bool jumpTriggered = (input.LeftClick && ctrl) || input.IsPressed(Keys.Space);
        if (jumpTriggered && player.State != MovementState.Jumping)
        {
            Vector2? jumpTarget = null;
            if (input.LeftClick && ctrl && viewer != null && !viewer.Camera.IsPanning)
            {
                jumpTarget = viewer.Camera.ViewportToCell(input.MousePosition);
                player.SetTarget(jumpTarget.Value);
            }
            player.Jump(0.6f, jumpTarget);
        }
        // 3. Click-to-move
        else if (input.LeftClick && viewer != null && !viewer.Camera.IsPanning && !ctrl)
        {
            player.SetTarget(viewer.Camera.ViewportToCell(input.MousePosition), run: shift);
        }

        // 4. WASD / Arrows
        var dir = Vector2.Zero;
        if (input.IsHeld(Keys.W) || input.IsHeld(Keys.Up)) dir.Y -= 1;
        if (input.IsHeld(Keys.S) || input.IsHeld(Keys.Down)) dir.Y += 1;
        if (input.IsHeld(Keys.A) || input.IsHeld(Keys.Left)) dir.X -= 1;
        if (input.IsHeld(Keys.D) || input.IsHeld(Keys.Right)) dir.X += 1;

        player.Update(dir, shift, (float)gt.ElapsedGameTime.TotalSeconds, s.PlayerWalkSpeed, s.PlayerRunSpeed);

        // 5. Handle State Changes (Idle -> Walk -> Run)
        if (player.State != _lastState)
        {
            RefreshMotion();
            _lastState = player.State;
        }

        // 6. Advance Animation Frames
        _frameTimer += gt.ElapsedGameTime.TotalSeconds;
        while (_frameTimer >= _secondsPerFrame)
        {
            _role.AdvanceFrame(1);
            _role.Calculate();
            _role.UpdateShapes();
            _frameTimer -= _secondsPerFrame;
        }
        _role.Update();

        // 7. RESTORED: Camera follow logic
        if (viewer?.Camera is GameCamera cam && viewer.CoordinateSystem is { } cs)
        {
            if (!cam.IsPanning)
                cam.Follow(cs.MapToScreen(player.CellPosition));
            cam.TrackCell(player.CellPosition);
        }
    }

    // ── Drawing ───────────────────────────────────────────────────────────
    public override void Draw(GameTime gt)
    {
        var player = _game.Player;
        var viewer = _game.MapViewer;
        if (player == null || viewer == null || !viewer.IsMapLoaded || _role == null) return;

        var cam = viewer.Camera;
        var cs = viewer.CoordinateSystem;
        if (cs == null) return;

        // 1. Draw Shadow
        var puzzlePx = cs.MapToScreen(player.CellPosition);
        var screenPos = new Vector2((puzzlePx.X - cam.DrawWindow.X) * cam.Zoom, (puzzlePx.Y - cam.DrawWindow.Y) * cam.Zoom);

        var sb = _game.SpriteBatch;
        sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend);
        sb.Draw(_shadow, new Rectangle((int)screenPos.X - 28, (int)screenPos.Y - 10, 56, 20), new Color(0, 0, 0, 90));
        sb.End();

        // 2. Setup 3D Matrices and Clear Depth (CRITICAL TO PREVENT CLIPPING)
        GraphicsDevice.BlendState = BlendState.AlphaBlend;
        GraphicsDevice.DepthStencilState = DepthStencilState.Default;
        GraphicsDevice.RasterizerState = RasterizerState.CullCounterClockwise;
        GraphicsDevice.SamplerStates[0] = SamplerState.LinearWrap;
        GraphicsDevice.Clear(ClearOptions.DepthBuffer, Color.Transparent, 1f, 0);

        float bob = player.State == MovementState.Jumping ? player.JumpHeight : 0f;
        float scale = _game.Settings.PlayerModelScale;

        // 3. Bulletproof XNA Matrix Construction
        var world = Matrix.CreateScale(scale);

        // C3Models are natively rotated by 90 on X upon load. We rotate by 180 on Y 
        // to face the camera correctly, then subtract the isometric facing angle.
        world *= Matrix.CreateRotationY(MathHelper.ToRadians(180f));
        world *= Matrix.CreateRotationY(-(player.FacingAngle - MathF.PI / 4f));

        // Translate final object into isometric cell space
        world *= Matrix.CreateTranslation(player.CellPosition.X, bob, player.CellPosition.Y);

        // 4. Propagate correct world matrix to all multi-part attachments
        foreach (var part in _role.AllParts())
        {
            if (part?.Model != null)
                part.Model.World = world;
        }

        // 5. Force socket synchronization and render
        _role.Calculate();
        _role.Draw(GraphicsDevice, cam.ViewMatrix, cam.ProjectionMatrix);
    }

    private void BuildShadow()
    {
        const int W = 56, H = 20;
        var px = new Color[W * H];
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float dx = (x - W * .5f) / (W * .5f), dy = (y - H * .5f) / (H * .5f), d = dx * dx + dy * dy;
                if (d < 1f) px[y * W + x] = new Color(0, 0, 0, (int)((1f - d) * 180f));
            }
        _shadow = new Texture2D(GraphicsDevice, W, H);
        _shadow.SetData(px);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _role?.Dispose();
            _shadow?.Dispose();
        }
        base.Dispose(disposing);
    }

    // ── Model ─────────────────────────────────────────────────────────────
    public C3Model? LoadModel(string relativePath)
    {
        try
        {
            if (_assetLoader != null)
            {
                using var stream = _assetLoader.LoadFile(relativePath);
                return C3Model.LoadFromStream(stream, GraphicsDevice);
            }
            return C3Model.Load(relativePath, GraphicsDevice);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[C3AssetLoader] LoadModel '{relativePath}': {ex.Message}");
            return null;
        }
    }

    // ── New per-part API ──────────────────────────────────────────────────
    public C3RolePart? LoadPart(
        string meshPath,
        string? texturePath,
        string slotName,
        uint rolePartId,
        int asb = 5,
        int adb = 6)
    {
        var model = LoadModel(meshPath);
        if (model == null) return null;

        int effectiveAsb = asb > 0 ? asb : 5;
        int effectiveAdb = adb > 0 ? adb : 6;

        ApplyBlend(model, effectiveAsb, effectiveAdb);

        if (!string.IsNullOrEmpty(texturePath))
            BindTextureToPart(model, texturePath);

        return new C3RolePart(model, slotName, rolePartId, effectiveAsb, effectiveAdb);
    }
   
    // ── Texture ───────────────────────────────────────────────────────────

    public int LoadTexture(string relativePath)
    {
        if (string.IsNullOrEmpty(relativePath)) return -1;

        // 1 – cache hit (also increments DupCount)
        int cached = C3Texture.Texture_Load(relativePath);
        if (cached >= 0) return cached;

        try
        {
            if (_assetLoader != null)
            {
                using var stream = _assetLoader.LoadFile(relativePath);
                var tex = DecodeTexture(stream, relativePath);
                return C3Texture.Texture_Load(relativePath, tex);
            }
            return -1;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[C3AssetLoader] LoadTexture '{relativePath}': {ex.Message}");
            return -1;
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────
    private static void ApplyBlend(C3Model model, int asb, int adb)
    {
        foreach (var phy in model.Phys) { phy.BlendAsb = asb; phy.BlendAdb = adb; }
        foreach (var shape in model.Shapes) { shape.BlendAsb = asb; shape.BlendAdb = adb; }
        foreach (var ptcl in model.Ptcls) { ptcl.BlendAsb = asb; ptcl.BlendAdb = adb; }
        foreach (var scene in model.Scenes) { scene.BlendAsb = asb; scene.BlendAdb = adb; }
    }

    private void BindTextureToPart(C3Model model, string texturePath)
    {
        // Count total chunks that will hold a reference
        int totalRefs = model.Phys.Count + model.Shapes.Count
                      + model.Ptcls.Count + model.Scenes.Count;
        if (totalRefs == 0) return;

        // First load → DupCount = 1 (or DupCount++ if already cached)
        int idx = LoadTexture(texturePath);
        if (idx < 0) return;

        // Acquire remaining refs — one per additional chunk
        for (int i = 1; i < totalRefs; i++)
            C3Texture.Texture_Load(texturePath);  // DupCount++

        // Assign the slot index to every chunk
        var entry = C3Texture.Get(idx);
        foreach (var phy in model.Phys)
        {
            phy.TexIndex = idx;
            phy.GpuTexture = entry?.Texture;   // refresh GPU pointer post-load
        }
        foreach (var shape in model.Shapes) shape.TexIndex = idx;
        foreach (var ptcl in model.Ptcls) ptcl.TexIndex = idx;
        foreach (var scene in model.Scenes) scene.TexIndex = idx;
    }

    private Texture2D DecodeTexture(Stream stream, string nameHint)
    {
        var ext = Path.GetExtension(nameHint).ToLowerInvariant();
        using var br = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);
        return ext switch
        {
            ".dds" => DDSLoader.Load(GraphicsDevice, br),
            ".tga" => TGALoader.Load(GraphicsDevice, br),
            _ => Texture2D.FromStream(GraphicsDevice, stream)
        };
    }

}