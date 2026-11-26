namespace Fableland.Vfx.Dev;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using Fableland.Data;
using Fableland.Run;

/// <summary>
/// Headless check for the VFX director (40-QA.md §2, Docs/VFX.gdd §10):
///
///     Godot --headless --path . res://Scenes/Feedback/VfxCheck.tscn      # exit 0 = clean
///
/// 1. Tables — every look names a real medium, every medium says every colour on every
///    canvas, every texture is generated at 64 px or under, the cone a swing carries
///    unpacks to the angle it was packed from.
/// 2. Coverage — every row of the cue vocabulary (Feedback.gdd §6) resolves for every owner
///    and every owner class, and plays — through the real bus — in all ten media without a
///    draw error.
/// 3. Whose medium — an attack in its attacker's, a body's own in its own, dust and hazards
///    in the arena's; an unwired kit's hit in its home medium; and the effect on the
///    hitbox's own numbers (reach and cone, box, radius, reach and direction).
/// 4. Pools — nothing is created after boot; every node comes back clean and is used again.
/// 5. The budget — under a flood the caps hold, decoration is dropped first, nothing
///    Critical is ever refused, and off-screen decoration is not started.
/// 6. Loops — started once, following their body, stopped by name, stopped when the source
///    leaves the tree.
/// 7. The arena — its medium and canvas come from arena.enter, its ambience starts, and all
///    of it is gone when the arena is; the director's bus subscription is a pair.
/// 8. Settings, authored sheets, variety (shuffle-bag, combo, boss phase), and allocation:
///    zero bytes per cue, per frame and per draw.
///
/// It reads what the director did — which nodes, in which medium, of what size — never what
/// a number is, so retuning the look does not break it. What it cannot do is look: that is
/// `Scenes/Dev/VfxGallery.tscn`.
/// </summary>
public partial class VfxCheck : Node
{
    private readonly List<string> _problems = new();
    private int _checks;
    private VfxDirector _d;
    private const int SpeedUp = 4;
    private static readonly Vector2 At = new(520f, 320f);

    public override async void _Ready()
    {
        double timeScale = Engine.TimeScale;
        _d = VfxDirector.Instance;
        try
        {
            if (_d == null) throw new InvalidOperationException("no VfxDirector autoload");
            Engine.TimeScale = SpeedUp;
            _d.Seed(20261126u);
            await Frames(3);
            CheckTables();
            await CheckCoverage();
            await CheckMediumAndGeometry();
            await CheckPools();
            await CheckBudget();
            await CheckLoops();
            await CheckArena();
            await CheckSettingsSheetsVariety();
            await CheckAllocation();
        }
        catch (Exception e)
        {
            _problems.Add("vfx check threw: " + e);
        }
        finally
        {
            Engine.TimeScale = timeScale;
            FeedbackSettings.ResetToDefaults();
            if (_d != null)
            {
                _d.ForceStyle = null;
                _d.Unlimited = false;
                _d.CullOffscreen = true;
                _d.MeasureAllocation = false;
                _d.LeaveArena();
                _d.TrimPools();
            }
            VfxMark.MeasureAllocation = false;
            VfxTextures.ForgetSheets();
            GetTree().Paused = false;
        }
        foreach (string problem in _problems) GD.Print("FAIL  " + problem);
        GD.Print(_problems.Count == 0 ? $"VfxCheck: OK ({_checks} checks)" : $"VfxCheck: {_problems.Count} problem(s) in {_checks} checks");
        GetTree().Quit(_problems.Count == 0 ? 0 : 1);
    }

    // ---------------------------------------------------------------- helpers

    private void Expect(bool ok, string what)
    {
        _checks++;
        if (!ok) _problems.Add(what);
    }

    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    /// <summary>Wait in the director's own time (game seconds).</summary>
    private async Task Seconds(float seconds)
    {
        double left = seconds;
        while (left > 0.0)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            left -= GetProcessDeltaTime();
        }
    }

    private VfxCheckBody Body(string id, string cls, CombatSide side = CombatSide.Player, Vector2? at = null)
    {
        var body = new VfxCheckBody { Id = id, Class = cls, BodySide = side };
        AddChild(body);
        body.GlobalPosition = at ?? At;
        return body;
    }

    private List<VfxMark> MarksSince(long serial)
    {
        var marks = new List<VfxMark>();
        for (int i = 0; i < _d.MarkCount; i++)
            if (_d.MarkAt(i).Live && _d.MarkAt(i).Serial > serial) marks.Add(_d.MarkAt(i));
        return marks;
    }

    private List<VfxBurst> BurstsSince(long serial)
    {
        var bursts = new List<VfxBurst>();
        for (int i = 0; i < _d.BurstCount; i++)
            if (_d.BurstAt(i).Live && _d.BurstAt(i).Serial > serial) bursts.Add(_d.BurstAt(i));
        return bursts;
    }

    private VfxMark Find(long serial, VfxMarkKind kind) => MarksSince(serial).Find(m => m.S.Kind == kind);
    private int Pieces(long serial) => MarksSince(serial).Count + BurstsSince(serial).Count;
    private int Started => _d.Started[0] + _d.Started[1] + _d.Started[2];

    private static void Emit(string owner, string action, Vector2 at, Node2D source = null, string tag = null, string cls = null,
                             int variant = 0, float size = 0f, float magnitude = 0f, Vector2 direction = default,
                             Vector2 extents = default, CombatSide side = CombatSide.Player) =>
        CueBus.EmitAt(owner, action, at, source, side, variant, size, magnitude, direction, tag, cls, extents);

    private static void Hit(VfxCheckBody victim, in HitInfo info, HitWeight weight, bool killed = false) =>
        CueBus.EmitHit(new HitCue(victim, info, weight, info.Damage, info.Knockback, victim.GlobalPosition - new Vector2(40f, 0f), killed, 0.04f));

    private void Reset(bool unlimited, bool cull)
    {
        _d.ForceStyle = null;
        _d.Unlimited = unlimited;
        _d.CullOffscreen = cull;
        _d.LeaveArena();
        _d.ResetCounters();
    }

    // ---------------------------------------------------------------- 1. tables

    private void CheckTables()
    {
        _checks++;
        foreach (string failure in VfxTable.SelfTest()) _problems.Add("VfxTable: " + failure);

        (string Owner, string Style)[] looks =
        {
            ("pomegraknight", "watercolour"), ("crab", "watercolour"), ("seagull", "watercolour"), ("boss_crab", "watercolour"),
            ("cleopastar", "poster"), ("sifu_pangda", "ink"), ("pumpking", "scratchboard"), ("pixolotl", "pixel"),
            ("VK", "watercolour"), ("SL", "poster"), ("BM", "ink"), ("HC", "realism"), ("TD", "linocut"), ("PL", "gongbi"),
            ("KH", "scratchboard"), ("TF", "pixel"), ("CC", "enamel"), ("VOID", "void"),
        };
        foreach ((string owner, string style) in looks)
            Expect(VfxTable.LookOf(owner)?.Style == style, $"looks: {owner} must work in {style}, not {VfxTable.LookOf(owner)?.Style ?? "nothing"}");
        Expect(VfxTable.LookOf("cleopastar").Provisional && VfxTable.LookOf("sifu_pangda").Provisional && !VfxTable.LookOf("pomegraknight").Provisional,
            "looks: Cleopastar's and Sifu Pangda's media are marked provisional, the others are not");
        Expect(VfxTable.ArenaStyle("").Id == VfxTable.DefaultStyle && VfxTable.ArenaStyle("nowhere").Id == VfxTable.DefaultStyle && VfxTable.ArenaStyle(null).Id == VfxTable.DefaultStyle,
            "looks: an arena that names no world, or an unknown one, gets the default medium");
        Expect(VfxDirector.CanvasFor(0.91f) == VfxCanvas.Light && VfxDirector.CanvasFor(0.67f) == VfxCanvas.Mid && VfxDirector.CanvasFor(0.08f) == VfxCanvas.Dark && VfxDirector.CanvasFor(-1f) == VfxCanvas.Mid,
            "canvas: Wulin's paper is pale, the beach mid, the dungeon dark, and an unknown backdrop mid");

        foreach (string id in VfxTextures.All)
        {
            Texture2D texture = VfxTextures.Get(id);
            Expect(texture != null && texture.GetWidth() <= VfxTextures.MaxSize && texture.GetHeight() <= VfxTextures.MaxSize,
                $"textures: '{id}' must be generated at {VfxTextures.MaxSize} px or under");
        }
        Expect(VfxTextures.GeneratedBytes > 0 && VfxTextures.GeneratedBytes < 256 * 1024, $"textures: {VfxTextures.GeneratedBytes} bytes generated, budget 256 KB");

        Vector2 cone = CueShapes.Cone(130f, 40f);
        Expect(Mathf.Abs(Mathf.RadToDeg(CueShapes.ConeHalfAngle(130f, cone)) - 40f) < 0.01f && Mathf.IsEqualApprox(cone.X, 65f),
            "cone: a 130 px, 40° cone packs to its bounding half-extents and unpacks to 40°");
        Expect(CueShapes.ConeHalfAngle(130f, Vector2.Zero) == 0f && CueShapes.ConeHalfAngle(0f, cone) == 0f, "cone: a cue with no extents or no reach carries no cone");
        GD.Print($"  vfx      tables         {VfxTable.Styles.Length} media · {VfxTable.AllDefs().Count} rows ({VfxTable.Cues.Count} cue keys, {VfxTable.Hits.Count} hit keys) · {VfxTextures.GeneratedCount} textures, {VfxTextures.GeneratedBytes / 1024f:0.0} KB");
    }

    // ---------------------------------------------------------------- 2. coverage

    private static readonly string[] BodyActions =
    {
        "spawn", "jump", "land", "death", "respawn", "heal", HurtTickMeter.Action, "ult.ready", "switch.out", "switch.in",
        "aggro", "evolve", "hurt", "status.burning", "status.on_fire", "status.frozen", "status.trapped", "status.stunned", "telegraph",
    };

    private static readonly (string Owner, string Class)[] Owners =
    {
        ("pomegraknight", CueOwners.Fighter), ("cleopastar", CueOwners.Fighter), ("pixolotl", CueOwners.Fighter),
        ("pumpking", CueOwners.Fighter), ("sifu_pangda", CueOwners.Fighter), ("pumpking", CueOwners.Boss),
        ("crab", CueOwners.Foe), ("seagull", CueOwners.Foe), ("boss_crab", CueOwners.Foe),
        ("destroy_objective", CueOwners.Objective), ("protect_core", CueOwners.Objective),
        ("newcomer", CueOwners.Fighter), ("newcomer", CueOwners.Boss), ("newcomer", CueOwners.Foe),
        ("newcomer", CueOwners.Objective), ("newcomer", CueOwners.Projectile), ("newcomer", null),
    };

    private static readonly (string Owner, string Class, string Action, string[] Tags)[] NamedCues =
    {
        ("pomegraknight", CueOwners.Fighter, "ba.swing", new[] { null, "burning" }),
        ("pomegraknight", CueOwners.Fighter, "s1.cast", null),
        ("pomegraknight", CueOwners.Fighter, "s1.tornado", null),
        ("pomegraknight", CueOwners.Fighter, "s1.tornado.tick", null),
        ("pomegraknight", CueOwners.Fighter, "s2.cast", new[] { null, "burning" }),
        ("pomegraknight", CueOwners.Fighter, "s2.wave", new[] { null, "burning" }),
        ("pomegraknight", CueOwners.Projectile, "s2.seed.expire", new[] { null, "burning" }),
        ("pumpking", CueOwners.Projectile, "ba.explode", new[] { null, "dud" }),
        (CueOwners.World, null, "soft.enter", new[] { "water", "foliage", "cloud", "kit", "something_new" }),
        (CueOwners.World, null, "soft.exit", new[] { "water", "foliage", "cloud", "kit", "something_new" }),
        (CueOwners.Hazard, null, "tick", new[] { "fire", "freeze", "lava", "spike", "poop", "damage" }),
        (CueOwners.Flow, null, "arena.enter", null),
        (CueOwners.Flow, null, "countdown.tick", null),
        (CueOwners.Flow, null, "countdown.go", null),
        (CueOwners.Flow, null, "mission.start", new[] { "collection", "boss" }),
        (CueOwners.Flow, null, "mission.success", new[] { "slaughter" }),
        (CueOwners.Flow, null, "mission.fail", new[] { "protect" }),
        (CueOwners.Flow, null, "boss.intro", new[] { "boss_crab" }),
        (CueOwners.Flow, null, "boss.defeated", new[] { "pumpking" }),
        (CueOwners.Flow, null, "player.down", null),
        (CueOwners.Flow, null, "run.over", new[] { "death", "boss_timer", "victory" }),
    };

    private static readonly (string Owner, string Class, string Action)[] Attacks =
    {
        ("pomegraknight", CueOwners.Fighter, "ba"), ("pomegraknight", CueOwners.Fighter, "s1.tornado"),
        ("pomegraknight", CueOwners.Projectile, "s2.seed"), ("pumpking", CueOwners.Projectile, "ba.head"),
        ("pumpking", CueOwners.Projectile, "ba.explode"), ("crab", CueOwners.Foe, "contact"), ("seagull", CueOwners.Foe, "contact"),
        ("boss_crab", CueOwners.Foe, "contact"), ("seagull", CueOwners.Foe, "s2.dash"), ("seagull", CueOwners.Projectile, "s1.poop"),
        (CueOwners.Hazard, null, "tsunami"), (CueOwners.Any, null, ""), (CueOwners.Any, CueOwners.Fighter, ""),
        (CueOwners.Any, CueOwners.Boss, ""), ("cleopastar", CueOwners.Fighter, "an_attack_not_wired_yet"),
    };

    private static readonly (string Style, string World)[] Worlds =
    {
        ("watercolour", "VK"), ("poster", "SL"), ("ink", "BM"), ("realism", "HC"), ("linocut", "TD"),
        ("gongbi", "PL"), ("scratchboard", "KH"), ("pixel", "TF"), ("enamel", "CC"), ("void", "VOID"),
    };

    private async Task CheckCoverage()
    {
        // Every body action, for every owner and every class it could fall back through.
        int resolved = 0;
        foreach ((string owner, string cls) in Owners)
        {
            foreach (string action in BodyActions)
            {
                Expect(VfxTable.Cues.Resolve(owner, cls, action) != null, $"coverage: {owner} ({cls ?? "no class"}) / {action} resolves to no effect");
                resolved++;
            }
        }
        foreach ((string owner, string cls, string action, string[] tags) in NamedCues)
        {
            VfxDef def = VfxTable.Cues.Resolve(owner, cls, action);
            Expect(def != null, $"coverage: {owner} / {action} resolves to no effect");
            foreach (string tag in tags ?? new string[] { null })
            {
                Expect(VfxTable.ForTag(def, tag) != null, $"coverage: {owner} / {action} tagged '{tag}' resolves to no effect");
                resolved++;
            }
        }
        foreach ((string owner, string cls, string action) in Attacks)
        {
            VfxDef def = VfxTable.Hits.Resolve(owner, cls, action) ?? VfxTable.Hits.Resolve(owner, cls, "");
            Expect(def != null, $"coverage: a hit from {owner} / '{action}' resolves to no effect");
            resolved++;
        }
        Expect(VfxTable.ForTag(VfxTable.Cues.Resolve(CueOwners.World, null, "soft.enter"), "water").Archetype == VfxArchetype.Splash
            && VfxTable.ForTag(VfxTable.Cues.Resolve(CueOwners.World, null, "soft.enter"), "foliage").Archetype == VfxArchetype.Rustle,
            "coverage: water is a splash and foliage a rustle — a soft volume is told by its medium");

        // …and each of them plays, through the real bus, in every medium.
        Reset(unlimited: true, cull: false);
        int played = 0;
        long drawsBefore = VfxMark.Draws;
        foreach ((string style, string world) in Worlds)
        {
            _d.LeaveArena();
            var fighter = Body("newcomer", CueOwners.Fighter);
            var foe = Body("crab", CueOwners.Foe, CombatSide.Foe, At + new Vector2(60f, 0f));
            Emit(CueOwners.Flow, "arena.enter", At, null, world, magnitude: 0.5f, size: 2000f);
            _d.ForceStyle = style;
            Expect(_d.ArenaStyle.Id == style && _d.LiveAmbient == _d.ArenaStyle.Ambience.Length && _d.LiveAmbient > 0,
                $"coverage: entering a {world} arena starts {style}'s ambience ({_d.LiveAmbient} emitters, medium {_d.ArenaStyle.Id})");

            foreach (string action in BodyActions)
            {
                long serial = _d.Serial;
                bool loop = action.StartsWith("status.") || action == "telegraph";
                if (loop) CueBus.Start(foe, action, size: 300f, magnitude: 0.5f, direction: Vector2.Left);
                else CueBus.Emit(fighter, action, variant: 1, magnitude: 400f, direction: Vector2.Up, tag: action == HurtTickMeter.Action ? "burn" : null);
                Expect(Pieces(serial) > 0, $"coverage: {style} drew nothing for {action}");
                played++;
            }
            foreach ((string owner, string cls, string action, string[] tags) in NamedCues)
            {
                if (action == "arena.enter") continue;
                foreach (string tag in tags ?? new string[] { null })
                {
                    long serial = _d.Serial;
                    var source = Body(owner, cls ?? CueOwners.Fighter);       // a fresh source each time: cooldowns are per source
                    if (action == "s1.tornado") CueBus.StartAt(owner, action, source, At, size: 128f, magnitude: 2.5f, ownerClass: cls, extents: new Vector2(128f, 40f));
                    else Emit(owner, action, At, source, tag, cls, 2, 96f, 300f, Vector2.Right, action.StartsWith("s1.") ? new Vector2(128f, 40f) : CueShapes.Cone(96f, 40f));
                    Expect(Pieces(serial) > 0, $"coverage: {style} drew nothing for {owner} / {action}{(tag != null ? " (" + tag + ")" : "")}");
                    played++;
                    source.QueueFree();
                }
            }
            foreach ((string owner, string cls, string action) in Attacks)
            {
                long serial = _d.Serial;
                var info = new HitInfo(30f, new Vector2(120f, -40f));
                info = owner == CueOwners.Any ? (cls != null ? info.From(fighter, action) : info) : info.FromRanged(owner, action);
                Hit(foe, info, HitWeight.Heavy);
                Expect(Pieces(serial) > 0, $"coverage: {style} drew nothing for a hit from {owner} / '{action}'");
                played++;
            }
            await Frames(3);
            Expect(VfxMark.DrawErrors == 0, $"coverage: {VfxMark.DrawErrors} draw(s) threw in {style}");
            _d.ClearAll();
            fighter.QueueFree();
            foe.QueueFree();
        }
        _d.ForceStyle = null;
        long draws = VfxMark.Draws - drawsBefore;
        Expect(draws > 0, "coverage: no mark was drawn at all — a headless run still gets its _Draw calls, so nothing was verified");
        _d.TrimPools();
        GD.Print($"  vfx      coverage       {resolved} vocabulary rows resolve (every owner × every class) · {played} of them played in 10 media: {draws} marks drawn, {VfxMark.DrawErrors} draw errors");
    }

    // ---------------------------------------------------------------- 3. whose medium, and the hitbox's numbers

    private async Task CheckMediumAndGeometry()
    {
        Reset(unlimited: true, cull: false);
        Emit(CueOwners.Flow, "arena.enter", At, null, "BM", magnitude: 0.91f);              // Wulin: ink, a pale canvas
        var pome = Body("pomegraknight", CueOwners.Fighter);
        var crab = Body("crab", CueOwners.Foe, CombatSide.Foe, At + new Vector2(70f, 0f));
        var cleo = Body("cleopastar", CueOwners.Fighter);
        var crate = Body("destroy_objective", CueOwners.Objective, CombatSide.Foe);

        long serial = _d.Serial;
        CueBus.Emit(pome, "ba.swing", variant: 1, size: 130f, magnitude: 15f, direction: Vector2.Right, extents: CueShapes.Cone(130f, 40f));
        VfxMark arc = Find(serial, VfxMarkKind.Arc);
        Expect(arc != null && arc.Style.Id == "watercolour" && arc.Canvas == VfxCanvas.Light,
            $"medium: Pomegraknight's slash is watercolour in every arena — here ink's — and knows the canvas is pale ({arc?.Style.Id}, {arc?.Canvas})");
        Expect(arc != null && Mathf.IsEqualApprox(arc.S.Size, 130f) && Mathf.Abs(Mathf.RadToDeg(arc.S.Arc) - 40f) < 0.01f && ReferenceEquals(arc.Follow, pome),
            $"geometry: the slash is drawn on the cue's reach and cone (130 px, 40°), on her — got {arc?.S.Size} px, {Mathf.RadToDeg(arc?.S.Arc ?? 0f):0.0}°");
        serial = _d.Serial;
        CueBus.Emit(pome, "ba.swing", variant: 3, size: 130f, direction: Vector2.Left, tag: "burning", extents: CueShapes.Cone(130f, 40f));
        VfxMark third = Find(serial, VfxMarkKind.Arc);
        Expect(third != null && (third.S.Flags & VfxFlags.Double) != 0 && (third.S.Flags & VfxFlags.Burning) != 0 && Pieces(serial) >= 3,
            "state: the third slash is a double crescent, and a burning swing adds fire to it");
        serial = _d.Serial;
        CueBus.Emit(pome, "ba.swing", variant: 2, size: 130f, direction: Vector2.Right);     // a swing with no cone packed
        Expect(Find(serial, VfxMarkKind.Arc) is { } bare && bare.S.Arc == 0f, "geometry: a swing that packs no cone still draws (the style's default angle)");

        serial = _d.Serial;
        CueBus.Emit(pome, "land", magnitude: 700f, direction: Vector2.Down);
        Expect(Find(serial, VfxMarkKind.Puff)?.Style.Id == "ink", "medium: landing dust belongs to the ground — the arena's medium");
        serial = _d.Serial;
        CueBus.Emit(pome, "jump", variant: 2, tag: "air");
        Expect(Find(serial, VfxMarkKind.Ring)?.Style.Id == "watercolour", "medium: an air jump touches no ground — its ring is the body's own medium");
        serial = _d.Serial;
        CueBus.Emit(pome, "death");
        Expect(Find(serial, VfxMarkKind.Impact)?.Style.Id == "watercolour", "medium: a body's own death is its own medium");
        serial = _d.Serial;
        CueBus.Emit(crate, "death");
        Expect(Find(serial, VfxMarkKind.Impact)?.Style.Id == "ink", "medium: an owner with no look (an objective) is drawn in the arena's medium");
        serial = _d.Serial;
        Emit(CueOwners.Hazard, "tick", At, crate, "spike", size: 32f, magnitude: 5f);
        Expect(MarksSince(serial).Count > 0 && MarksSince(serial)[0].Style.Id == "ink", "medium: a hazard's tick is the arena's medium");

        serial = _d.Serial;
        Hit(crab, new HitInfo(30f, new Vector2(200f, 0f)).From(pome, "ba", 3), HitWeight.Medium);
        List<VfxMark> marks = MarksSince(serial);
        Expect(marks.Exists(m => m.S.Kind == VfxMarkKind.Impact && m.Style.Id == "watercolour"), "medium: a hit's impact is the attacker's medium");
        Expect(BurstsSince(serial).Count >= 2, "medium: a Medium hit also throws pieces off the victim (its reaction)");
        serial = _d.Serial;
        Hit(crab, new HitInfo(20f, new Vector2(200f, 0f)).FromRanged("pumpking", "ba.head"), HitWeight.Light);
        Expect(Find(serial, VfxMarkKind.Impact)?.Style.Id == "scratchboard", "medium: PumpKing's head hits in scratchboard");
        serial = _d.Serial;
        Hit(crab, new HitInfo(20f, new Vector2(200f, 0f)).From(cleo, null), HitWeight.Light);
        Expect(Find(serial, VfxMarkKind.Impact)?.Style.Id == "poster", "medium: a kit that is not wired yet still hits in its home medium (the body that swung)");
        serial = _d.Serial;
        Hit(pome, new HitInfo(20f, new Vector2(-200f, 0f)), HitWeight.Light);
        Expect(Find(serial, VfxMarkKind.Impact)?.Style.Id == "ink", "medium: a hit that names nobody, on the player, falls to the arena's medium");
        serial = _d.Serial;
        Hit(crab, new HitInfo(2f), HitWeight.Graze);
        Expect(MarksSince(serial).Count == 1 && BurstsSince(serial).Count == 0, $"restraint: a graze is one fleck and nothing else ({MarksSince(serial).Count} marks, {BurstsSince(serial).Count} bursts)");
        serial = _d.Serial;
        Hit(crab, new HitInfo(200f, new Vector2(300f, 0f)).From(pome, "ba", 3), HitWeight.Massive);
        VfxMark light = null, massive = Find(serial, VfxMarkKind.Impact);
        serial = _d.Serial;
        Hit(crab, new HitInfo(10f, new Vector2(100f, 0f)).From(pome, "ba", 1), HitWeight.Light);
        light = Find(serial, VfxMarkKind.Impact);
        float lightPx = light?.S.Size ?? 0f, massivePx = massive?.S.Size ?? 0f;
        Expect(light != null && massive != null && massive.S.Size > light.S.Size * 2f && light.S.Size < 16f,
            $"dynamic range: a massive hit's mark is several times a light one's, and a light one is a small part of a fighter ({light?.S.Size:0.0} vs {massive?.S.Size:0.0} px)");

        // The field, the blast and the telegraph on their own numbers.
        serial = _d.Serial;
        CueBus.Start(pome, "s1.tornado", size: 128f, magnitude: 2.5f, extents: new Vector2(128f, 40f));
        VfxMark field = Find(serial, VfxMarkKind.Field);
        Expect(field != null && field.S.Extents == new Vector2(128f, 40f) && field.Looping && field.Priority == VfxPriority.Critical,
            "geometry: the Fire Tornado is a loop on its real box (128 × 40 px half-extents), never culled");
        serial = _d.Serial;
        CueBus.Emit(pome, "s1.tornado.tick", variant: 1, size: 128f, magnitude: 7f, extents: new Vector2(128f, 40f));
        Expect(Find(serial, VfxMarkKind.Ring)?.S.Extents == new Vector2(128f, 40f), "geometry: a tornado tick pulses on the same box");
        CueBus.Stop(pome, "s1.tornado");
        serial = _d.Serial;
        Emit("pumpking", "ba.explode", At, null, null, CueOwners.Projectile, size: 96f, magnitude: 60f);
        VfxMark blast = MarksSince(serial).Find(m => m.S.Kind == VfxMarkKind.Ring);
        Expect(blast != null && Mathf.IsEqualApprox(blast.S.Size, 96f) && blast.Style.Id == "scratchboard", $"geometry: the head's blast ring goes to exactly its radius, in scratchboard ({blast?.S.Size} px, {blast?.Style.Id})");
        serial = _d.Serial;
        Emit("pumpking", "ba.explode", At, null, "dud", CueOwners.Projectile, size: 96f);
        Expect(MarksSince(serial).Count > 0 && !MarksSince(serial).Exists(m => m.S.Kind == VfxMarkKind.Ring), "state: a head that only pops is not a blast");
        serial = _d.Serial;
        var direction = new Vector2(-0.8f, 0.6f);
        CueBus.Start(crab, "telegraph", size: 480f, magnitude: 0.6f, direction: direction);
        VfxMark zone = Find(serial, VfxMarkKind.Zone);
        Expect(zone != null && Mathf.IsEqualApprox(zone.S.Size, 480f) && Mathf.Abs(Mathf.AngleDifference(zone.Rotation + zone.S.Rot, direction.Angle())) < 0.001f
            && Mathf.IsEqualApprox(zone.S.Param, 0.6f) && zone.Priority == VfxPriority.Critical && zone.ZIndex == VfxDirector.MustReadZ,
            "geometry: a telegraph lies along the attack's real reach and direction, knows the tell's length, and is drawn above everything");
        CueBus.Stop(crab, "telegraph");

        // A medium that keeps its grid is never turned: the direction goes into the drawing.
        _d.ForceStyle = "pixel";
        serial = _d.Serial;
        CueBus.Emit(pome, "ba.swing", variant: 1, size: 130f, direction: direction, extents: CueShapes.Cone(130f, 40f));
        VfxMark pixelArc = Find(serial, VfxMarkKind.Arc);
        Expect(pixelArc != null && pixelArc.Rotation == 0f && Mathf.Abs(Mathf.AngleDifference(pixelArc.S.Rot, direction.Angle())) < 0.14f
            && pixelArc.Position == VfxPen.Snap(pixelArc.Position, 2f),
            "pixel art: a mark is never turned off the grid and sits on whole art pixels");
        _d.ForceStyle = null;

        await Frames(2);
        Expect(VfxMark.DrawErrors == 0, "medium: a draw threw");
        _d.ClearAll();
        foreach (VfxCheckBody body in new[] { pome, crab, cleo, crate }) body.QueueFree();
        GD.Print($"  vfx      whose medium   a slash is the attacker's (watercolour in an ink arena) · dust the arena's · an unwired kit its home medium's · the slash on 130 px / 40.0°, the field on 128 × 40, the blast to 96 px, the tell along 480 px · a light hit's mark {lightPx:0.0} px, a massive one's {massivePx:0.0} px");
    }

    // ---------------------------------------------------------------- 4. pools

    private async Task CheckPools()
    {
        Reset(unlimited: false, cull: false);
        await Frames(2);
        int nodes = _d.PoolNodes, children = _d.GetChildCount();
        Expect(_d.MarkCount == VfxBudget.Marks && _d.BurstCount == VfxBudget.Bursts && nodes == VfxBudget.Marks + VfxBudget.Bursts + VfxBudget.Ambient,
            $"pools: the director owns exactly its budget of nodes ({_d.MarkCount} marks, {_d.BurstCount} bursts)");
        var pome = Body("pomegraknight", CueOwners.Fighter);
        var crab = Body("crab", CueOwners.Foe, CombatSide.Foe, At + new Vector2(60f, 0f));
        int usesBefore = 0;
        for (int i = 0; i < _d.MarkCount; i++) usesBefore += _d.MarkAt(i).Uses;
        for (int round = 0; round < 40; round++)
        {
            CueBus.Emit(pome, "ba.swing", variant: round % 3 + 1, size: 130f, direction: Vector2.Right, extents: CueShapes.Cone(130f, 40f));
            Hit(crab, new HitInfo(30f, new Vector2(150f, 0f)).From(pome, "ba", 1), HitWeight.Medium);
            CueBus.Emit(pome, "land", magnitude: 600f);
            await Frames(2);
        }
        Expect(_d.LiveMarks > 0, "pools: effects are alive while a fight goes on");
        await Seconds(3f);
        int uses = 0, dirty = 0, reused = 0;
        for (int i = 0; i < _d.MarkCount; i++)
        {
            VfxMark m = _d.MarkAt(i);
            uses += m.Uses;
            if (m.Uses > 1) reused++;
            if (!m.IsClean) dirty++;
        }
        for (int i = 0; i < _d.BurstCount; i++) if (!_d.BurstAt(i).IsClean) dirty++;
        Expect(_d.LiveMarks == 0 && _d.LiveBursts == 0 && _d.LiveParticles == 0 && _d.FreeMarks == VfxBudget.Marks && _d.FreeBursts == VfxBudget.Bursts,
            $"pools: when the effects are over every node is back ({_d.LiveMarks} marks, {_d.LiveBursts} bursts, {_d.LiveParticles} particles still out)");
        Expect(dirty == 0, $"pools: {dirty} node(s) came back carrying something of their last effect");
        Expect(_d.PoolNodes == nodes && _d.GetChildCount() == children, "pools: nothing was created after boot");
        Expect(reused > 0 && uses - usesBefore > VfxBudget.Marks, $"pools: nodes are used again ({uses - usesBefore} rents of {VfxBudget.Marks} marks, {reused} used more than once)");
        pome.QueueFree();
        crab.QueueFree();
        GD.Print($"  vfx      pools          {nodes} nodes for the whole game ({VfxBudget.Marks} marks, {VfxBudget.Bursts} emitters, {VfxBudget.Ambient} ambient) · {uses - usesBefore} mark rents in 40 rounds, 0 created, all returned clean");
    }

    // ---------------------------------------------------------------- 5. the budget

    private async Task CheckBudget()
    {
        Reset(unlimited: false, cull: true);
        Engine.TimeScale = 1.0;                                   // effects must outlive a few frames for the pool to fill
        await Frames(2);
        var pome = Body("pomegraknight", CueOwners.Fighter);
        var crab = Body("crab", CueOwners.Foe, CombatSide.Foe, At + new Vector2(60f, 0f));

        // One frame, four hundred of everything.
        int started = Started;
        for (int i = 0; i < 400; i++)
        {
            Hit(crab, new HitInfo(30f, new Vector2(150f, 0f)).From(pome, "ba", 1), HitWeight.Medium);
            CueBus.Emit(pome, "land", magnitude: 600f);
        }
        Expect(Started - started <= VfxBudget.SpawnsPerFrame && _d.Dropped[(int)VfxPriority.Confirm] > 300 && _d.Dropped[(int)VfxPriority.Cosmetic] > 300,
            $"budget: at most {VfxBudget.SpawnsPerFrame} decorative effects start in one frame ({Started - started} did of 1200 asked)");

        // A flood that lasts: thirty frames of it, with Critical effects among them.
        var burning = new List<VfxCheckBody>();
        for (int frame = 0; frame < 30; frame++)
        {
            for (int i = 0; i < 40; i++)
            {
                Hit(crab, new HitInfo(80f, new Vector2(150f, 0f)).From(pome, "ba", 3), HitWeight.Heavy);
                CueBus.Emit(pome, "land", magnitude: 900f);
                Emit(CueOwners.World, "soft.enter", At, null, "water", size: 22f, magnitude: 500f, direction: Vector2.Down);
            }
            if (frame == 14)
            {
                // …and then thirty bodies catch fire at once, with the pool already crowded.
                for (int i = 0; i < 30; i++)
                {
                    var body = Body("crab", CueOwners.Foe, CombatSide.Foe, At + new Vector2(i * 6f - 90f, 40f));
                    burning.Add(body);
                    CueBus.Start(body, "status.burning");
                }
            }
            Emit("pumpking", "ba.explode", At, null, null, CueOwners.Projectile, size: 96f, magnitude: 60f);
            await Frames(1);
        }
        Expect(_d.PeakMarks <= VfxBudget.Marks && _d.PeakBursts <= VfxBudget.Bursts && _d.PeakParticles <= VfxBudget.Particles && _d.MarkCount == VfxBudget.Marks && _d.BurstCount == VfxBudget.Bursts,
            $"budget: under a flood the caps hold (peak {_d.PeakMarks}/{VfxBudget.Marks} marks, {_d.PeakBursts}/{VfxBudget.Bursts} emitters, {_d.PeakParticles}/{VfxBudget.Particles} particles)");
        Expect(_d.Dropped[(int)VfxPriority.Critical] == 0, $"budget: nothing Critical is ever refused ({_d.Dropped[(int)VfxPriority.Critical]} were)");
        Expect(_d.LiveLoops == Math.Min(burning.Count, VfxBudget.Loops), $"budget: every status loop that fits its table is running ({_d.LiveLoops})");
        int auras = 0, critical = 0;
        for (int i = 0; i < _d.MarkCount; i++)
        {
            VfxMark m = _d.MarkAt(i);
            if (m.Live && m.S.Kind == VfxMarkKind.Aura) auras++;
            if (m.Live && m.Priority == VfxPriority.Critical) critical++;
        }
        Expect(auras == _d.LiveLoops, $"budget: each running status loop has its mark even with the pool under a flood ({auras} of {_d.LiveLoops})");
        Expect(_d.Stolen > 0, "budget: with the pool full a Critical effect takes the oldest lesser mark (none was taken — the flood never filled the pool)");
        int peakMarks = _d.PeakMarks, peakBursts = _d.PeakBursts, peakParticles = _d.PeakParticles, stolen = _d.Stolen;
        int droppedConfirm = _d.Dropped[1], droppedCosmetic = _d.Dropped[2];

        // Decoration is the first to go: with Confirm effects holding the pool, Cosmetic gets nothing.
        foreach (VfxCheckBody body in burning) { CueBus.Stop(body, "status.burning"); body.QueueFree(); }
        burning.Clear();
        _d.ClearAll();
        _d.ResetCounters();
        _d.CullOffscreen = false;
        for (int frame = 0; frame < 20 && _d.LiveMarks < VfxBudget.CosmeticMarks; frame++)
        {
            for (int i = 0; i < 6; i++) Hit(crab, new HitInfo(200f, new Vector2(150f, 0f)).FromRanged("pumpking", "ba.head"), HitWeight.Massive);
            await Frames(1);
        }
        int cosmeticDropped = _d.Dropped[(int)VfxPriority.Cosmetic], cosmeticStarted = _d.Started[(int)VfxPriority.Cosmetic];
        long serial = _d.Serial;
        CueBus.Emit(pome, "land", magnitude: 600f);
        Expect(_d.LiveMarks >= VfxBudget.CosmeticMarks && MarksSince(serial).Count == 0 && BurstsSince(serial).Count == 0,
            $"budget: past {VfxBudget.CosmeticMarks} live marks a Cosmetic effect gets no piece ({_d.LiveMarks} live, {MarksSince(serial).Count} given)");
        serial = _d.Serial;
        Hit(crab, new HitInfo(30f, new Vector2(150f, 0f)).FromRanged("pumpking", "ba.head"), HitWeight.Medium);
        Expect(_d.LiveMarks >= VfxBudget.ConfirmMarks || MarksSince(serial).Count > 0, "budget: …while a Confirm effect is still served until its own threshold");

        // Off screen: decoration is not started; what must be read still is.
        _d.ClearAll();
        _d.CullOffscreen = true;
        await Frames(2);
        var far = new Vector2(90000f, 90000f);
        var distant = Body("crab", CueOwners.Foe, CombatSide.Foe, far);
        serial = _d.Serial;
        CueBus.Emit(distant, "land", magnitude: 600f);
        CueBus.Emit(distant, "spawn");
        Expect(Pieces(serial) == 0, "budget: decoration off screen is not started");
        CueBus.Start(distant, "telegraph", size: 300f, magnitude: 0.5f, direction: Vector2.Left);
        Expect(Find(serial, VfxMarkKind.Zone) != null, "budget: a telegraph off screen still is — the camera may get there before it ends");
        CueBus.Stop(distant, "telegraph");
        distant.QueueFree();
        pome.QueueFree();
        crab.QueueFree();
        _d.ClearAll();
        Engine.TimeScale = SpeedUp;
        GD.Print($"  vfx      budget         a flood of 3 660 cues over 30 frames: peak {peakMarks}/{VfxBudget.Marks} marks, {peakBursts}/{VfxBudget.Bursts} emitters, {peakParticles}/{VfxBudget.Particles} particles · refused {droppedConfirm} Confirm, {droppedCosmetic} Cosmetic, 0 Critical · {stolen} marks handed to Critical effects · 30 status loops asked, {VfxBudget.Loops} run");
    }

    // ---------------------------------------------------------------- 6. loops

    private async Task CheckLoops()
    {
        Reset(unlimited: false, cull: false);
        var crab = Body("crab", CueOwners.Foe, CombatSide.Foe);
        long serial = _d.Serial;
        CueBus.Start(crab, "status.frozen");
        CueBus.Start(crab, "status.frozen");
        VfxMark aura = Find(serial, VfxMarkKind.Aura);
        Expect(_d.LiveLoops == 1 && MarksSince(serial).Count == 1 && aura != null && aura.Looping && aura.S.Variant == VfxTable.StatusIndex("status.frozen"),
            $"loops: a loop started twice is one loop, and its mark says which status ({_d.LiveLoops} loops, {MarksSince(serial).Count} marks)");
        Expect(aura != null && aura.Style.Id == "watercolour" && aura.Priority == VfxPriority.Critical, "loops: a status is worn in the body's own medium and is never culled");
        crab.GlobalPosition += new Vector2(140f, -30f);
        await Frames(2);
        Expect(aura != null && aura.Position.DistanceTo(crab.GlobalPosition) < 1f, "loops: a status loop follows its body");
        await Seconds(2.5f);
        Expect(aura != null && aura.Live && _d.LiveLoops == 1, "loops: a loop does not end by itself");
        int uses = aura?.Uses ?? 0;
        CueBus.Stop(crab, "status.frozen");
        Expect(_d.LiveLoops == 0, "loops: a loop is stopped by its source, owner and action");
        await Seconds(0.6f);
        Expect(aura != null && (!aura.Live || aura.Uses != uses), "loops: a stopped loop's mark leaves (within its medium's release)");

        // The source leaves the tree without stopping anything.
        CueBus.Start(crab, "status.burning");
        CueBus.Start(crab, "telegraph", size: 200f, magnitude: 0.4f, direction: Vector2.Right);
        Expect(_d.LiveLoops == 2, "loops: two loops on one body");
        crab.QueueFree();
        await Frames(4);
        await Seconds(0.5f);
        Expect(_d.LiveLoops == 0 && _d.LiveMarks == 0, $"loops: a loop whose source left the tree is ended by the director ({_d.LiveLoops} loops, {_d.LiveMarks} marks left)");

        // A loop with no source is matched by owner and action alone.
        CueBus.StartAt("pomegraknight", "s1.tornado", null, At, extents: new Vector2(128f, 40f), ownerClass: CueOwners.Fighter);
        Expect(_d.LiveLoops == 1, "loops: a loop may have no source");
        CueBus.StopAt("pomegraknight", "s1.tornado", null);
        Expect(_d.LiveLoops == 0, "loops: …and is stopped the same way");
        await Seconds(0.5f);
        _d.ClearAll();
        GD.Print("  vfx      loops          started once however often asked · follow their body · stopped by name · ended when the source leaves the tree");
    }

    // ---------------------------------------------------------------- 7. the arena

    private async Task CheckArena()
    {
        Reset(unlimited: false, cull: true);
        await Frames(2);
        int nodes = _d.PoolNodes, children = _d.GetChildCount();

        // A real arena on a real map: Wulin.
        var arena = ResourceLoader.Load<PackedScene>("res://Scenes/Arena.tscn").Instantiate<GameManager>();
        arena.DebugMission = MissionType.Slaughter;
        arena.DebugCombatMapPath = "res://Maps/wulin_sea_level.json";
        arena.PreCombatCountdownSeconds = 0.2f;
        AddChild(arena);
        Expect(_d.InArena && _d.ArenaWorld == "BM" && _d.ArenaStyle.Id == "ink" && _d.ArenaCanvas == VfxCanvas.Light,
            $"arena: a Wulin map makes the arena's medium ink on a pale canvas (world '{_d.ArenaWorld}', {_d.ArenaStyle.Id}, {_d.ArenaCanvas})");
        int ambient = _d.LiveAmbient, motes = _d.LiveParticles;
        Expect(ambient == _d.ArenaStyle.Ambience.Length && ambient > 0 && motes > 0 && motes <= 24, $"arena: its ambience starts with it ({ambient} emitters, {motes} particles)");
        Expect(_d.AmbientAt(0).Emitting && _d.AmbientAt(0).ZIndex == VfxDirector.AmbientZ, "arena: the ambient emitter is emitting, under the effects");
        await Seconds(2.5f);
        GetTree().Paused = false;
        Expect(_d.InArena, "arena: the ambience lasts as long as the arena does");
        int startedInArena = Started;
        arena.QueueFree();
        await Frames(4);
        Expect(!_d.InArena && _d.LiveAmbient == 0 && _d.LiveMarks == 0 && _d.LiveBursts == 0 && _d.LiveLoops == 0 && _d.LiveParticles == 0,
            $"arena: when the arena leaves the tree everything of it is gone ({_d.LiveAmbient} emitters, {_d.LiveMarks} marks, {_d.LiveBursts} bursts, {_d.LiveLoops} loops, {_d.LiveParticles} particles left)");
        Expect(!_d.AmbientAt(0).Emitting && !_d.AmbientAt(0).Visible, "arena: its emitters are switched off, not just forgotten");
        Expect(_d.ArenaStyle.Id == VfxTable.DefaultStyle && _d.ArenaWorld == "", "arena: with no arena the medium is the default again");

        // A second arena straight after — the legacy one, which names no world.
        arena = ResourceLoader.Load<PackedScene>("res://Scenes/Arena.tscn").Instantiate<GameManager>();
        arena.DebugMission = MissionType.Slaughter;
        arena.DebugCombatMapPath = "";
        arena.PreCombatCountdownSeconds = 0.2f;
        AddChild(arena);
        Expect(_d.InArena && _d.ArenaWorld == "" && _d.ArenaStyle.Id == VfxTable.DefaultStyle && _d.LiveAmbient > 0, "arena: the legacy arena gets the default medium and its ambience");
        await Seconds(1f);
        GetTree().Paused = false;
        arena.QueueFree();
        await Frames(4);
        Expect(!_d.InArena && _d.LiveAmbient == 0 && _d.LiveMarks == 0 && _d.LiveBursts == 0 && _d.LiveParticles == 0, "arena: …and leaves nothing behind either");
        Expect(_d.PoolNodes == nodes && _d.GetChildCount() == children, $"arena: two arenas later the director owns the same nodes ({_d.PoolNodes} / {nodes})");

        // An arena.enter that names no source: the scene changing under it ends it. Here the
        // scene does not change, so it lasts — and the next arena.enter replaces it.
        Emit(CueOwners.Flow, "arena.enter", At, null, "KH", magnitude: 0.1f);
        Expect(_d.InArena && _d.ArenaStyle.Id == "scratchboard" && _d.ArenaCanvas == VfxCanvas.Dark, "arena: an arena.enter with no source still sets the medium");
        await Frames(3);
        Expect(_d.InArena, "arena: …and is not ended while the scene it was raised in is still the current one");
        Emit(CueOwners.Flow, "arena.enter", At, null, "TF", magnitude: 0.05f);
        Expect(_d.ArenaStyle.Id == "pixel" && _d.LiveAmbient == _d.ArenaStyle.Ambience.Length, "arena: a new arena.enter replaces the last arena's ambience, it does not add to it");
        _d.LeaveArena();

        // The bus subscription is a pair: out of the tree the director hears nothing.
        Node root = _d.GetParent();
        int index = _d.GetIndex();
        root.RemoveChild(_d);
        int before = Started;
        Emit("pomegraknight", "ba.swing", At, null, null, CueOwners.Fighter, 1, 130f);
        Expect(VfxDirector.Instance == null && _d.Started[0] + _d.Started[1] + _d.Started[2] == before, "bus: a director out of the tree has unsubscribed");
        root.AddChild(_d);
        root.MoveChild(_d, index);
        Emit("pomegraknight", "ba.swing", At, null, null, CueOwners.Fighter, 1, 130f);
        Expect(VfxDirector.Instance == _d && Started == before + 1, $"bus: back in the tree it hears each cue once ({Started - before} effects for one cue)");
        _d.ClearAll();
        GD.Print($"  vfx      the arena      Wulin: ink, a pale canvas, {ambient} ambient emitters / {motes} particles, {startedInArena} effects in 2.5 s of a real arena · everything gone when the arena left · the legacy arena: the default medium · pool unchanged at {nodes} nodes");
    }

    // ---------------------------------------------------------------- 8. settings, sheets, variety

    private async Task CheckSettingsSheetsVariety()
    {
        Reset(unlimited: true, cull: false);
        var pome = Body("pomegraknight", CueOwners.Fighter);
        var crab = Body("crab", CueOwners.Foe, CombatSide.Foe, At + new Vector2(60f, 0f));

        // The particles setting scales decoration and leaves what must be read alone.
        int Droplets(float setting, out int marks, out int blastPieces, out int motes)
        {
            FeedbackSettings.Particles = setting;
            _d.ClearAll();
            _d.Seed(7u);
            Emit(CueOwners.Flow, "arena.enter", At, null, "VK", magnitude: 0.67f);
            motes = _d.LiveParticles;
            long serial = _d.Serial;
            Hit(crab, new HitInfo(200f, new Vector2(150f, 0f)).From(pome, "ba", 3), HitWeight.Massive);
            marks = MarksSince(serial).FindAll(m => m.S.Kind != VfxMarkKind.Glint).Count;
            int amount = 0;
            foreach (VfxBurst burst in BurstsSince(serial)) amount += burst.Particles;
            serial = _d.Serial;
            Emit("pomegraknight", "ba.explode", At, null, null, CueOwners.Projectile, size: 96f, magnitude: 60f);     // Critical, in watercolour
            blastPieces = 0;
            foreach (VfxBurst burst in BurstsSince(serial)) blastPieces += burst.Particles;
            return amount;
        }
        int full = Droplets(1f, out int marksFull, out int blastFull, out int motesFull);
        int quarter = Droplets(0.25f, out int marksQuarter, out int blastQuarter, out int motesQuarter);
        Expect(quarter < full && quarter >= 1 && quarter <= full / 2, $"settings: Particles at 0.25 thins a hit's pieces ({full} → {quarter})");
        Expect(marksQuarter >= 1 && marksQuarter == marksFull, $"settings: …and never removes the effect itself ({marksFull} marks → {marksQuarter})");
        Expect(blastQuarter == blastFull && blastFull > 0, $"settings: a Critical effect ignores it ({blastFull} → {blastQuarter} pieces)");
        Expect(motesQuarter < motesFull && motesQuarter >= 1, $"settings: the ambience thins with it ({motesFull} → {motesQuarter} particles)");
        FeedbackSettings.ResetToDefaults();
        _d.LeaveArena();

        // Authored art replaces procedural: a sheet for (medium, effect id), a texture by id.
        var strip = ImageTexture.CreateFromImage(Image.CreateEmpty(64, 16, false, Image.Format.Rgba8));
        VfxTextures.SetSheet("watercolour", "pome_slash", strip);
        long s = _d.Serial;
        CueBus.Emit(pome, "ba.swing", variant: 1, size: 130f, direction: Vector2.Right, extents: CueShapes.Cone(130f, 40f));
        VfxMark sheet = Find(s, VfxMarkKind.Sheet);
        Expect(sheet != null && sheet.S.Frames == 4 && ReferenceEquals(sheet.S.Sheet, strip) && Mathf.IsEqualApprox(sheet.S.Size, 130f) && Find(s, VfxMarkKind.Arc) == null,
            "sheets: an authored strip replaces the procedural effect — 4 square frames, on the same size rule");
        VfxTextures.SetSheet("watercolour", "pome_slash", null);
        s = _d.Serial;
        CueBus.Emit(pome, "ba.swing", variant: 1, size: 130f, direction: Vector2.Right, extents: CueShapes.Cone(130f, 40f));
        Expect(Find(s, VfxMarkKind.Arc) != null && Find(s, VfxMarkKind.Sheet) == null, "sheets: without one the effect is procedural again");
        Texture2D generated = VfxTextures.Get("watercolour/drop");
        VfxTextures.Override("watercolour/drop", strip);
        bool replaced = ReferenceEquals(VfxTextures.Get("watercolour/drop"), strip);
        VfxTextures.Override("watercolour/drop", null);
        Expect(replaced && VfxTextures.Get("watercolour/drop") != null && !ReferenceEquals(VfxTextures.Get("watercolour/drop"), strip) && generated != null,
            "textures: a texture is replaced by id, and comes back when the replacement goes");

        // Never the same twice: the shuffle-bag, the jitter, the rare flourish.
        _d.ClearAll();
        _d.Seed(99u);
        int repeats = 0, last = -1, flourishes = 0;
        var seen = new HashSet<int>();
        float smallest = float.MaxValue, largest = 0f;
        for (int i = 0; i < 400; i++)
        {
            s = _d.Serial;
            Hit(crab, new HitInfo(30f, new Vector2(150f, 0f)).FromRanged("pomegraknight", "ba"), HitWeight.Medium);
            List<VfxMark> marks = MarksSince(s);
            VfxMark impact = marks.Find(m => m.S.Kind == VfxMarkKind.Impact);
            if (impact == null) continue;
            if (impact.S.Variant == last) repeats++;
            last = impact.S.Variant;
            seen.Add(last);
            smallest = Mathf.Min(smallest, impact.S.Size);
            largest = Mathf.Max(largest, impact.S.Size);
            if (marks.Exists(m => m.S.Kind == VfxMarkKind.Glint)) flourishes++;
            if (i % 40 == 39) { _d.ClearAll(); await Seconds(VfxTable.ComboWindow + 0.2f); }      // a new combo: size is jitter and escalation
        }
        Expect(repeats == 0 && seen.Count == 3, $"variety: three shapes from a shuffle-bag, never the same twice running ({repeats} repeats, {seen.Count} shapes)");
        Expect(largest > smallest * 1.1f && largest < smallest * 2.2f, $"variety: size jitters inside its range ({smallest:0.0}–{largest:0.0} px)");
        Expect(flourishes >= 5 && flourishes <= 50, $"variety: the flourish is rare — about one hit in twenty ({flourishes} in 400)");

        // The blackboard: a combo, the fight's intensity, the boss's phase.
        _d.ClearAll();
        await Seconds(VfxTable.ComboWindow + 0.3f);
        Expect(_d.Combo == 0, "blackboard: a combo ends when the hits stop");
        for (int i = 0; i < 9; i++) Hit(crab, new HitInfo(15f, new Vector2(150f, 0f)).From(pome, "ba", 1), HitWeight.Light);
        Hit(pome, new HitInfo(15f, new Vector2(-150f, 0f)).From(crab, "contact"), HitWeight.Light);
        Expect(_d.Combo == 9 && _d.Intensity > 0.5f, $"blackboard: nine hits by the player's side are a combo of nine, and the fight is intense ({_d.Combo}, {_d.Intensity:0.00})");
        var boss = Body("pumpking", CueOwners.Boss, CombatSide.Foe);
        Emit(CueOwners.Flow, "boss.intro", At, boss, "pumpking", side: CombatSide.Foe);
        Expect(_d.BossPhase == 1, "blackboard: a boss on stage is phase 1");
        boss.State = FeedbackState.LowHp;
        await Frames(2);
        Expect(_d.BossPhase == 2, "blackboard: a boss at low health is phase 2");
        Emit(CueOwners.Flow, "boss.defeated", At, boss, "pumpking", side: CombatSide.Foe);
        Expect(_d.BossPhase == 0, "blackboard: a boss that fell is no phase at all");
        float intensity = _d.Intensity;
        await Seconds(4f);
        Expect(_d.Intensity < intensity * 0.4f && _d.Combo == 0, $"blackboard: intensity cools in seconds ({intensity:0.00} → {_d.Intensity:0.00})");

        _d.ClearAll();
        _d.TrimPools();
        foreach (VfxCheckBody body in new[] { pome, crab, boss }) body.QueueFree();
        GD.Print($"  vfx      settings       Particles 1 → 0.25: a massive hit's pieces {full} → {quarter}, its marks {marksFull} → {marksQuarter}, a Critical blast {blastFull} → {blastQuarter}, ambience {motesFull} → {motesQuarter} · variety: 3 shapes, 0 repeats in 400, size {smallest:0.0}–{largest:0.0} px, {flourishes} flourishes");
    }

    // ---------------------------------------------------------------- 9. allocation

    private async Task CheckAllocation()
    {
        Reset(unlimited: false, cull: false);
        Emit(CueOwners.Flow, "arena.enter", At, null, "VK", magnitude: 0.67f);
        var pome = Body("pomegraknight", CueOwners.Fighter);
        var crab = Body("crab", CueOwners.Foe, CombatSide.Foe, At + new Vector2(60f, 0f));
        var melee = new HitInfo(15f, new Vector2(40f, 0f)).From(pome, "ba", 1);
        var ranged = new HitInfo(80f, new Vector2(40f, 0f)).FromRanged("pumpking", "ba.head");
        await Frames(2);

        // Raising cues — with the director resolving rows, renting pieces and refusing the rest.
        for (int i = 0; i < 300; i++) RaiseOneOfEach(pome, crab, melee, ranged);
        long before = GC.GetAllocatedBytesForCurrentThread();
        const int rounds = 2000;
        for (int i = 0; i < rounds; i++) RaiseOneOfEach(pome, crab, melee, ranged);
        long perCue = GC.GetAllocatedBytesForCurrentThread() - before;
        Expect(perCue == 0, $"allocation: raising {rounds * CuesPerRound} cues allocated {perCue} bytes ({perCue / (float)(rounds * CuesPerRound):0.0} a cue); a cue must cost no garbage");

        // The director's own frame, and the marks' drawing, with effects alive throughout.
        _d.ClearAll();
        Emit(CueOwners.Flow, "arena.enter", At, null, "VK", magnitude: 0.67f);
        CueBus.Start(crab, "status.burning");
        CueBus.Start(pome, "s1.tornado", size: 128f, magnitude: 2.5f, extents: new Vector2(128f, 40f));
        for (int i = 0; i < 20; i++) { RaiseOneOfEach(pome, crab, melee, ranged); await Frames(1); }
        _d.ResetCounters();
        VfxMark.AllocatedInDraw = 0;
        long draws = VfxMark.Draws;
        _d.MeasureAllocation = true;
        VfxMark.MeasureAllocation = true;
        for (int frame = 0; frame < 120; frame++)
        {
            if (frame % 4 == 0)
            {
                _d.MeasureAllocation = false;                 // the cue's own cost was measured above
                RaiseOneOfEach(pome, crab, melee, ranged);
                _d.MeasureAllocation = true;
            }
            await Frames(1);
        }
        _d.MeasureAllocation = false;
        VfxMark.MeasureAllocation = false;
        draws = VfxMark.Draws - draws;
        Expect(_d.AllocatedInUpdate == 0, $"allocation: 120 frames of the director's own work allocated {_d.AllocatedInUpdate} bytes; a frame must cost no garbage");
        Expect(VfxMark.AllocatedInDraw == 0 && draws > 100, $"allocation: {draws} mark draws allocated {VfxMark.AllocatedInDraw} bytes; drawing must cost no garbage");

        // Every medium's drawing, not only watercolour's.
        long worst = 0;
        string worstStyle = "";
        _d.Unlimited = true;
        foreach (VfxStyle style in VfxTable.Styles)
        {
            _d.ClearAll();
            _d.ForceStyle = style.Id;
            for (int i = 0; i < 6; i++) { RaiseOneOfEach(pome, crab, melee, ranged); await Frames(1); }     // warm
            VfxMark.AllocatedInDraw = 0;
            VfxMark.MeasureAllocation = true;
            for (int i = 0; i < 12; i++) { RaiseOneOfEach(pome, crab, melee, ranged); await Frames(1); }
            VfxMark.MeasureAllocation = false;
            if (VfxMark.AllocatedInDraw > worst) { worst = VfxMark.AllocatedInDraw; worstStyle = style.Id; }
        }
        _d.ForceStyle = null;
        _d.Unlimited = false;
        Expect(worst == 0, $"allocation: drawing in {worstStyle} allocated {worst} bytes; no medium may");

        CueBus.Stop(crab, "status.burning");
        CueBus.Stop(pome, "s1.tornado");
        _d.LeaveArena();
        _d.TrimPools();
        pome.QueueFree();
        crab.QueueFree();
        GD.Print($"  vfx      allocation     {rounds * CuesPerRound} cues raised with the director acting on each: {perCue} bytes · 120 frames of its update: {_d.AllocatedInUpdate} bytes · {draws} mark draws: {VfxMark.AllocatedInDraw} bytes · worst medium to draw: {worst} bytes");
    }

    private const int CuesPerRound = 9;

    /// <summary>Nine cues: a swing, a landing, a hurt tick, a loop's start and stop, a
    /// crossing, a hazard tick, a melee hit and a ranged one.</summary>
    private static void RaiseOneOfEach(VfxCheckBody pome, VfxCheckBody crab, in HitInfo melee, in HitInfo ranged)
    {
        CueBus.Emit(pome, "ba.swing", variant: 2, size: 130f, magnitude: 15f, direction: Vector2.Right, tag: "burning", extents: new Vector2(65f, 83.56f));
        CueBus.Emit(pome, "land", magnitude: 600f, direction: Vector2.Down);
        CueBus.Emit(crab, HurtTickMeter.Action, magnitude: 3f, tag: "burn");
        CueBus.Start(pome, "telegraph", size: 200f, magnitude: 0.5f, direction: Vector2.Right);
        CueBus.Stop(pome, "telegraph");
        CueBus.EmitAt(CueOwners.World, "soft.enter", crab.GlobalPosition, crab, CombatSide.Foe, magnitude: 300f, tag: "water");
        CueBus.EmitAt(CueOwners.Hazard, "tick", pome.GlobalPosition, null, CombatSide.Player, size: 32f, magnitude: 4f, tag: "fire");
        CueBus.EmitHit(new HitCue(crab, melee, HitWeight.Light, 15f, melee.Knockback, pome.GlobalPosition, false, 0.044f));
        CueBus.EmitHit(new HitCue(pome, ranged, HitWeight.Heavy, 80f, ranged.Knockback, crab.GlobalPosition, false, 0.1f));
    }
}
