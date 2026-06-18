namespace ConquerMono.C3.Roles;

public sealed class C3Role : IDisposable
{
    // ── Equipment slots ───────────────────────────────────────────────────
    public C3RolePart? Mount { get; set; }
    public C3RolePart? Body { get; set; }
    public C3RolePart? MixBody { get; set; }
    public C3RolePart? Mix2Body { get; set; }
    public C3RolePart? Armet { get; set; }
    public C3RolePart? MixArmet { get; set; }
    public C3RolePart? Mix2Armet { get; set; }
    public C3RolePart? RWeapon { get; set; }
    public C3RolePart? LWeapon { get; set; }
    public C3RolePart? Mantle { get; set; }
    public C3RolePart? Cape { get; set; }
    public C3RolePart? Misc { get; set; }
    public C3RolePart? Pelvis { get; set; }
    public C3RolePart? Spirit { get; set; }

    // ── Frame state (delegated to Body) ───────────────────────────────────
    public int MaxFrameCount => Body?.MaxFrameCount ?? 0;
    public int CurrentFrame => Body?.CurrentFrame ?? 0;

    // ── Per-frame update ──────────────────────────────────────────────────
    public void AdvanceFrame(int step)
    {
        foreach (var p in AllParts()) p.AdvanceFrame(step);
    }

    public void SetFrame(int frame)
    {
        foreach (var p in AllParts()) p.SetFrame(frame);
    }

    public void Calculate()
    {
        Mount?.Calculate();
        BindAllParts();

        Body?.Calculate();
        MixBody?.Calculate();
        Mix2Body?.Calculate();

        foreach (var p in AttachmentParts())
        {
            p.Calculate();
        }
    }

    public void UpdateShapes()
    {
        foreach (var p in AllParts()) p.UpdateShapes();
    }

    public void Update()
    {
        foreach (var p in AllParts()) p.Update();
    }

    public void UploadAllVertices()
    {
        foreach (var p in AllParts()) p.UploadVertices();
    }    

    public void ChangeMotion(Stream stream, string partname ="BODY")
    {
        var part = GetSlot(partname);
        if (part == null) return;
        part.ChangeMotion(stream);
    }

    public void Initialize(GraphicsDevice gd)
    {
        foreach (var p in AllParts()) p.Initialize(gd);
    }

    public void Draw(GraphicsDevice gd, Matrix view, Matrix projection)
    {
        Mount?.Draw(gd, view, projection);
        Body?.Draw(gd, view, projection);
        MixBody?.Draw(gd, view, projection);
        Mix2Body?.Draw(gd, view, projection);

        foreach (var p in AttachmentParts())
        {
            p.Draw(gd, view, projection);
        }
    }

    public IEnumerable<string> GetPhyNames()
    {
        foreach (var p in AllParts())
            foreach (var n in p.GetPhyNames()) yield return n;
    }

    public bool GetPhyVisibility(string name)
    {
        foreach (var p in AllParts())
            if (p.GetPhyNames().Contains(name, StringComparer.OrdinalIgnoreCase))
                return p.GetPhyVisibility(name);
        return true;
    }

    public void SetPhyVisibility(string name, bool visible)
    {
        foreach (var p in AllParts()) p.SetPhyVisibility(name, visible);
    }

    // ── Slot assignment helpers ───────────────────────────────────────────
    public C3RolePart? GetSlot(string partname)
    {
        return partname.ToUpperInvariant() switch
        {
            "MOUNT" => Mount,
            "BODY" or "ARMOR" => Body,
            "MIX_BODY" => MixBody,
            "MIX2_BODY" => Mix2Body,
            "ARMET" => Armet,
            "MIX_ARMET" => MixArmet,
            "MIX2_ARMET" => Mix2Armet,
            "RWEAPON" => RWeapon,
            "LWEAPON" => LWeapon,
            "MANTLE" => Mantle,
            "CAPE" => Cape,
            "MISC" => Misc,
            "PELVIS" => Pelvis,
            "SPIRIT" => Spirit,
            _ => null,
        };
    }

    public void AssignSlot(C3RolePart part)
    {
        switch (part.SlotName.ToUpperInvariant())
        {
            case "MOUNT": Mount = part; break;
            case "BODY":
            case "ARMOR": Body = part; break;
            case "MIX_BODY": MixBody = part; break;
            case "MIX2_BODY": Mix2Body = part; break;
            case "ARMET": Armet = part; break;
            case "MIX_ARMET": MixArmet = part; break;
            case "MIX2_ARMET": Mix2Armet = part; break;
            case "RWEAPON": RWeapon = part; break;
            case "LWEAPON": LWeapon = part; break;
            case "MANTLE": Mantle = part; break;
            case "CAPE": Cape = part; break;
            case "MISC": Misc = part; break;
            case "PELVIS": Pelvis = part; break;
            case "SPIRIT": Spirit = part; break;
        }
    }

    public void ClearSlot(string partname)
    {
        switch (partname.ToUpperInvariant())
        {
            case "MOUNT": Mount = null; break;
            case "BODY":
            case "ARMOR": Body = null; break;
            case "MIX_BODY": MixBody = null; break;
            case "MIX2_BODY": Mix2Body = null; break;
            case "ARMET": Armet = null; break;
            case "MIX_ARMET": MixArmet = null; break;
            case "MIX2_ARMET": Mix2Armet = null; break;
            case "RWEAPON": RWeapon = null; break;
            case "LWEAPON": LWeapon = null; break;
            case "MANTLE": Mantle = null; break;
            case "CAPE": Cape = null; break;
            case "MISC": Misc = null; break;
            case "PELVIS": Pelvis = null; break;
            case "SPIRIT": Spirit = null; break;
        }
    }

    // ── Socket binding ────────────────────────────────────────────────────
    public void BindAllParts()
    {
        var mountSocket = Mount?.GetVirtualMotion("v_mount");
        Body?.SetVirtualMotion(mountSocket);
        MixBody?.SetVirtualMotion(mountSocket);
        Mix2Body?.SetVirtualMotion(mountSocket);

        BindRolePart(Armet, Body, "v_armet");
        BindRolePart(MixArmet, Body, "v_armet");
        BindRolePart(Mix2Armet, Body, "v_armet");
        BindRolePart(RWeapon, Body, "v_r_weapon");
        BindRolePart(LWeapon, Body, "v_l_weapon");
        BindRolePart(Mantle, Body, "v_mantle");
        BindRolePart(Cape, Body, "v_back");
        BindRolePart(Misc, Body, "v_misc");
        BindRolePart(Pelvis, Body, "v_pelvis");
        BindRolePart(Spirit, Body, "v_rootloc");
    }

    private void BindRolePart(C3RolePart? child, C3RolePart? parent, string socketName)
    {
        if (child == null) return;
        Matrix? matrix = null;        
      //  if (socketName.Equals("v_r_weapon", StringComparison.OrdinalIgnoreCase) || socketName.Equals("v_l_weapon", StringComparison.OrdinalIgnoreCase))        
         //    matrix = Matrix.CreateScale(2f) * Matrix.CreateRotationX(MathHelper.ToRadians(180f)); // Rotate 180 degrees for weapons to face forward
        child.SetVirtualMotion(parent?.GetVirtualMotion(socketName), matrix);
        
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────
    public void Dispose()
    {
        foreach (var p in AllParts()) p.Dispose();
    }

    // ── Iteration ─────────────────────────────────────────────────────────
    public IEnumerable<C3RolePart> AllParts()
    {
        if (Mount != null) yield return Mount;
        if (Body != null) yield return Body;
        if (MixBody != null) yield return MixBody;
        if (Mix2Body != null) yield return Mix2Body;

        foreach (var p in AttachmentParts()) yield return p;
    }

    private IEnumerable<C3RolePart> AttachmentParts()
    {
        if (Armet != null) yield return Armet;
        if (MixArmet != null) yield return MixArmet;
        if (Mix2Armet != null) yield return Mix2Armet;
        if (RWeapon != null) yield return RWeapon;
        if (LWeapon != null) yield return LWeapon;
        if (Mantle != null) yield return Mantle;
        if (Cape != null) yield return Cape;
        if (Misc != null) yield return Misc;
        if (Pelvis != null) yield return Pelvis;
        if (Spirit != null) yield return Spirit;
    }
    public void SetWorld(Matrix world)
    {
        foreach (var p in AllParts())
            p.SetWorld(world);
    }
}