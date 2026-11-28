using System.Collections.Generic;
using Godot;
using Fableland.Data;
using Fableland.MapCreation.Data;
using Fableland.MapCreation.Runtime;
using Fableland.Realms;

/// <summary>
/// The arena's sub-realm spaces (`Docs/Tech/T50-REALMS.md` §3, §8). Before the fight it builds,
/// once, every realm the fight's owners can open — the party's kits, the boss, held items, the
/// map's weather (`RealmTable.SourcesOf`) — each from its map document, far above the
/// battlefield (40 000 px apart), with its own backdrop. Opening a realm then only switches a
/// prepared space on and moves bodies: no hitch (the owner's rule, `Realms.gdd` §15). A realm
/// nobody listed is built the first time it opens, with a warning.
/// </summary>
public partial class ArenaRealmSpaces : Node2D, IRealmSpaces
{
    /// <summary>Spaces are this far apart, and the first this far above the battlefield.</summary>
    public const float Spacing = 40000f;

    /// <summary>One prepared space: what the view needs to show it.</summary>
    public sealed class Prepared
    {
        public RealmSpace Space;
        public bool InUse;
        public readonly List<MapParallaxLayer> Parallax = new();
        public Color Canvas;
    }

    private readonly List<Prepared> _prepared = new();
    private Rect2 _main;

    public Rect2 MainBounds => _main;
    public IReadOnlyList<Prepared> Spaces => _prepared;

    public override void _Ready() => AddToGroup(Realm.SpacesGroup);

    /// <summary>Builds one space per listed realm and per simultaneous instance it can have
    /// (two owners of the same realm — the player's PumpKing and a BOSS PumpKing — two spaces).</summary>
    public void Prepare(IEnumerable<string> defIds, Rect2 mainBounds)
    {
        _main = mainBounds;
        var wanted = new Dictionary<string, int>();
        foreach (string id in defIds)
            if (!string.IsNullOrEmpty(id)) wanted[id] = wanted.GetValueOrDefault(id) + 1;
        foreach ((string id, int copies) in wanted)
        {
            if (!RealmTable.TryGet(id, out RealmDef def)) { GD.PushWarning($"[ArenaRealmSpaces] unknown realm '{id}'"); continue; }
            for (int i = 0; i < Mathf.Min(copies, 2); i++) Build(def);
        }
    }

    public RealmSpace Acquire(RealmDef def)
    {
        foreach (Prepared prepared in _prepared)
            if (!prepared.InUse && prepared.Space.Def.Id == def.Id) { prepared.InUse = true; return prepared.Space; }
        GD.PushWarning($"[ArenaRealmSpaces] '{def.Id}' was not preloaded; building it now (a hitch)");
        Prepared built = Build(def);
        if (built == null) return null;
        built.InUse = true;
        return built.Space;
    }

    public void Release(RealmSpace space)
    {
        foreach (Prepared prepared in _prepared)
            if (prepared.Space == space) prepared.InUse = false;
    }

    public Prepared Find(RealmSpace space)
    {
        foreach (Prepared prepared in _prepared)
            if (prepared.Space == space) return prepared;
        return null;
    }

    private Prepared Build(RealmDef def)
    {
        string path = def.MapPath;
        if (string.IsNullOrWhiteSpace(path)) { GD.PushError($"[ArenaRealmSpaces] '{def.Id}' has no map"); return null; }
        if (path.StartsWith("res://") || path.StartsWith("user://")) path = ProjectSettings.GlobalizePath(path);
        MapDocument document = CombatMapCatalog.LoadDocument(path);
        if (document == null) { GD.PushError($"[ArenaRealmSpaces] '{def.Id}': no map at {def.MapPath}"); return null; }

        int slot = _prepared.Count;
        var root = new Node2D { Name = $"Space{slot}_{def.Id}", Position = new Vector2(0f, -Spacing * (slot + 1)) };
        AddChild(root);
        var backdrop = new ColorRect { Name = "Backdrop", MouseFilter = Control.MouseFilterEnum.Ignore, ZIndex = -200, ZAsRelative = false };
        root.AddChild(backdrop);
        var world = new Node2D { Name = "World" };
        var hazards = new Node2D { Name = "Hazards" };
        root.AddChild(world);
        root.AddChild(hazards);
        CombatMapBuild build = CombatMapRuntime.Build(document, world, hazards);

        Vector2 anchor = root.GlobalPosition + (build.CharacterSpawns.Count > 0
            ? build.CharacterSpawns[0] : new Vector2(build.WidthPx * 0.5f, build.HeightPx * 0.5f));
        build.AnchorParallax(anchor);

        var prepared = new Prepared
        {
            Space = new RealmSpace
            {
                Def = def,
                Anchor = anchor,
                Root = root,
                Bounds = new Rect2(root.GlobalPosition, new Vector2(build.WidthPx, build.HeightPx)).Grow(Units.Px(6f)),
                View = new Rect2(root.GlobalPosition, new Vector2(build.WidthPx, build.HeightPx)),
            },
        };
        prepared.Parallax.AddRange(build.ParallaxLayers);
        // A layer that follows the camera is shown only to whoever is inside: it would draw itself
        // into the battlefield's view. The ground's own art is fixed in the world, far away, and
        // stays on, so the window from outside draws it (KNOWLEDGE v0.12.21).
        foreach (MapParallaxLayer layer in prepared.Parallax)
            if (layer.FollowsCamera) layer.Visible = false;
        try { prepared.Canvas = new Color(document.Canvas?.Color ?? "#000000"); }
        catch { prepared.Canvas = Colors.Black; }
        Vector2 margin = new(Units.Px(30f), Units.Px(20f));
        backdrop.Position = -margin;
        backdrop.Size = new Vector2(build.WidthPx, build.HeightPx) + margin * 2f;
        backdrop.Color = prepared.Canvas;

        _prepared.Add(prepared);
        return prepared;
    }
}
