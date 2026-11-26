namespace Fableland.Vfx.Dev;

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Godot;
using Fableland.Run;

/// <summary>
/// Dev tool: the VFX gallery (Docs/VFX.gdd §10, 40-QA.md). It LOOKS at what the headless
/// check cannot — every archetype in every medium, in the real renderer.
///
///     open -g -n -W --env HOME=/scratch -a /Applications/Godot_mono.app --args --path "$PWD" \
///          --resolution 1600x900 res://Scenes/Dev/VfxGallery.tscn -- --shots=/abs/dir [--only=grid,looks,timing,air,fight] [--maps=a,b]
///
/// 1. grid   — archetype × style, each held on one moment of its life, on a dark and on a
///             pale backdrop (the contrast guard is visible as the difference).
/// 2. looks  — the five protagonists' own colours in their home media.
/// 3. timing — a hit and a slash at five moments of their lives: how each medium ends a mark.
/// 4. air    — each realm's ambience on its own canvas colour.
/// 5. fight  — Pomegraknight against crabs in the real Arena, on a mid-tone, a pale and a
///             dark map: effects in context, the hit flash and the camera's numbers.
///
/// With no <c>--shots</c> it plays the same sequence without saving. Needs a window (it
/// cannot run <c>--headless</c>); run it under a scratch HOME — an arena may touch
/// <c>user://</c>.
/// </summary>
public partial class VfxGallery : Node2D
{
    private const float ViewW = 1152f, ViewH = 648f;
    private const float LabelW = 104f, HeaderH = 24f;
    private static readonly Color DarkBackdrop = new("141a36");
    private static readonly Color PaleBackdrop = new("ece6d6");

    private string _shots;
    private ColorRect _backdrop;
    private Node2D _furniture;                     // labels, guides, silhouettes — cleared per sheet
    private readonly VirtualPad _pad = new();
    private int _swings, _waves, _hits, _deaths, _lands;
    private bool _missionStarted;
    private int _saved;

    public override void _Process(double delta) => _pad.StepProcess();
    public override void _PhysicsProcess(double delta) => _pad.StepPhysics();

    private readonly record struct Row(string Label, VfxArchetype Archetype, VfxElement Element, float Size, float Hold,
        int Stage = 1, VfxFlags Flags = VfxFlags.None, float Power = 0.5f, float ExtX = 0f, float ExtY = 0f, float ArcDeg = 0f,
        float Magnitude = 0f, float DirX = 1f, float DirY = 0f, float X = 0f, float Y = 0f, bool Body = false, bool Ground = false,
        VfxArchetype Second = VfxArchetype.Count, VfxElement SecondElement = VfxElement.Impact, int SecondStage = 1, float SecondX = 0f, float SecondSize = 0f);

    private static readonly Row[] Rows =
    {
        new("hit · medium", VfxArchetype.HitSpark, VfxElement.Impact, 18f, 0.30f),
        new("hit · massive, a kill", VfxArchetype.HitSpark, VfxElement.Impact, 28f, 0.30f, Flags: VfxFlags.Killed, Power: 1f),
        new("slash · stage 1", VfxArchetype.SlashArc, VfxElement.Impact, 80f, 0.34f, ArcDeg: 40f, X: -36f),
        new("slash · stage 3, burning", VfxArchetype.SlashArc, VfxElement.Fire, 80f, 0.38f, Stage: 3, Flags: VfxFlags.Burning, ArcDeg: 40f, X: -36f),
        new("burst (a blast)", VfxArchetype.Burst, VfxElement.Fire, 44f, 0.34f),

        new("ring (a field's tick)", VfxArchetype.Ring, VfxElement.Fire, 46f, 0.50f, ExtX: 46f, ExtY: 17f),
        new("trail (a volley)", VfxArchetype.Trail, VfxElement.Impact, 46f, 0.38f, Magnitude: 8f, DirX: 0f, DirY: -1f, Y: 30f),
        new("dust puff (landing)", VfxArchetype.DustPuff, VfxElement.Dust, 24f, 0.42f, Power: 0.9f, Y: 22f, Ground: true),
        new("splash (water)", VfxArchetype.Splash, VfxElement.Water, 20f, 0.32f, Power: 0.6f, DirY: 1f, DirX: 0f, Y: 14f, Ground: true),
        new("rustle (foliage)", VfxArchetype.Rustle, VfxElement.Leaf, 20f, 0.40f, DirX: 0.6f, DirY: -0.8f),

        new("status · burning", VfxArchetype.StatusLoop, VfxElement.Fire, 20f, 0.30f, Stage: 0, Body: true),
        new("status · frozen", VfxArchetype.StatusLoop, VfxElement.Ice, 20f, 0.30f, Stage: 2, Body: true),
        new("status · trapped, stunned", VfxArchetype.StatusLoop, VfxElement.Danger, 20f, 0.30f, Stage: 3, Body: true,
            Second: VfxArchetype.StatusLoop, SecondElement: VfxElement.Light, SecondStage: 4, SecondSize: 20f),
        new("telegraph (a dash)", VfxArchetype.Telegraph, VfxElement.Danger, 84f, 0.50f, Magnitude: 0.6f, X: -44f, Body: true),
        new("field loop (its box)", VfxArchetype.FieldLoop, VfxElement.Fire, 48f, 0.40f, ExtX: 48f, ExtY: 17f, Body: true),

        new("death burst", VfxArchetype.DeathBurst, VfxElement.Impact, 22f, 0.34f),
        new("spawn pop", VfxArchetype.SpawnPop, VfxElement.Light, 20f, 0.50f, Body: true),
        new("heal", VfxArchetype.Heal, VfxElement.Heal, 20f, 0.45f, Body: true),
        new("shine · cast", VfxArchetype.Shine, VfxElement.Light, 20f, 0.40f, X: -26f,
            Second: VfxArchetype.Cast, SecondElement: VfxElement.Light, SecondX: 26f, SecondSize: 20f),
        new("expire pop · sting", VfxArchetype.ExpirePop, VfxElement.Impact, 9f, 0.35f, X: -26f,
            Second: VfxArchetype.Sting, SecondElement: VfxElement.Danger, SecondX: 26f, SecondSize: 22f),
    };

    public override async void _Ready()
    {
        ProcessPriority = -100;
        ProcessPhysicsPriority = -100;
        string only = null, maps = "beach_sea_level,wulin_sea_level,dungeon_sea_level";
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--shots=")) _shots = arg.Substring("--shots=".Length);
            else if (arg.StartsWith("--only=")) only = arg.Substring("--only=".Length);
            else if (arg.StartsWith("--maps=")) maps = arg.Substring("--maps=".Length);
        }
        if (!string.IsNullOrWhiteSpace(_shots)) Directory.CreateDirectory(_shots);
        bool Wanted(string part) => only == null || ("," + only + ",").Contains("," + part + ",");

        _backdrop = new ColorRect { Size = new Vector2(ViewW * 3f, ViewH * 3f), Position = new Vector2(-ViewW, -ViewH), MouseFilter = Control.MouseFilterEnum.Ignore, ZIndex = -100 };
        AddChild(_backdrop);
        _furniture = new Node2D();
        AddChild(_furniture);

        CueBus.Raised += OnCue;
        CueBus.HitLanded += OnHit;
        VfxDirector director = VfxDirector.Instance;
        bool debugOverlay = Fableland.Debug.DebugManager.Instance?.Visible ?? false;
        if (Fableland.Debug.DebugManager.Instance != null) Fableland.Debug.DebugManager.Instance.Visible = false;
        try
        {
            if (director == null) throw new InvalidOperationException("no VfxDirector autoload");
            await Frames(8);
            director.Unlimited = true;
            director.CullOffscreen = false;
            if (Wanted("grid"))
            {
                for (int sheet = 0; sheet * 5 < Rows.Length; sheet++)
                {
                    await Grid(sheet, VfxCanvas.Dark);
                    await Grid(sheet, VfxCanvas.Light);
                }
            }
            if (Wanted("looks")) await Looks();
            if (Wanted("timing"))
            {
                await Timing("timing_hit", Rows[0]);
                await Timing("timing_slash", Rows[2]);
            }
            if (Wanted("air")) await Air();
            director.SetFrozen(false);
            director.TrimPools();
            director.Unlimited = false;
            director.CullOffscreen = true;
            ClearFurniture();
            if (Wanted("fight"))
                foreach (string map in maps.Split(',', StringSplitOptions.RemoveEmptyEntries)) await Fight(map.Trim());
        }
        catch (Exception e)
        {
            GD.PushError("VfxGallery: " + e);
        }
        finally
        {
            CueBus.Raised -= OnCue;
            CueBus.HitLanded -= OnHit;
            if (director != null)
            {
                director.SetFrozen(false);
                director.Unlimited = false;
                director.CullOffscreen = true;
            }
            if (Fableland.Debug.DebugManager.Instance != null) Fableland.Debug.DebugManager.Instance.Visible = debugOverlay;
        }
        GD.Print($"VfxGallery: wrote {_saved} image(s) to {_shots ?? "(nowhere — pass -- --shots=/abs/dir)"} · draw errors {VfxMark.DrawErrors}");
        if (_shots != null) GetTree().Quit(VfxMark.DrawErrors == 0 ? 0 : 1);
    }

    private void OnCue(Cue cue)
    {
        if (cue.Action == "ba.swing") _swings++;
        else if (cue.Action == "s2.wave") _waves++;
        else if (cue.Action == "death") _deaths++;
        else if (cue.Action == "land") _lands++;
        else if (cue.Owner == CueOwners.Flow && cue.Action == "mission.start") _missionStarted = true;
    }

    private void OnHit(HitCue hit) => _hits++;

    // ── helpers ──────────────────────────────────────────────────────────────────
    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async Task Seconds(float seconds)
    {
        double left = seconds;
        while (left > 0.0)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            left -= GetProcessDeltaTime();
        }
    }

    private async Task<bool> Until(Func<bool> done, float seconds)
    {
        double left = seconds;
        while (!done() && left > 0.0)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            left -= GetProcessDeltaTime();
        }
        return done();
    }

    /// <summary>Save what the viewport shows at the end of this frame.</summary>
    private async Task Shot(string name)
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        if (_shots == null) return;
        Image image = GetViewport().GetTexture().GetImage();
        string file = Path.Combine(_shots, name + ".png");
        if (image.SavePng(file) == Error.Ok) _saved++;
        else GD.PushError("VfxGallery: could not write " + file);
    }

    private void ClearFurniture()
    {
        foreach (Node child in _furniture.GetChildren()) child.QueueFree();
    }

    private void Text(string text, Vector2 at, Color colour, int size = 10, float width = 0f, HorizontalAlignment align = HorizontalAlignment.Left)
    {
        var label = new Label { Text = text, Position = at, Modulate = colour, HorizontalAlignment = align };
        label.AddThemeFontSizeOverride("font_size", size);
        if (width > 0f) { label.CustomMinimumSize = new Vector2(width, 0f); label.Size = new Vector2(width, 14f); }
        _furniture.AddChild(label);
    }

    private void Guide(Rect2 rect, Color colour)
    {
        _furniture.AddChild(new ColorRect { Position = rect.Position, Size = rect.Size, Color = colour, MouseFilter = Control.MouseFilterEnum.Ignore });
    }

    private static Color InkOn(VfxCanvas canvas, float alpha) =>
        canvas == VfxCanvas.Light ? new Color(0.1f, 0.1f, 0.14f, alpha) : new Color(0.92f, 0.94f, 1f, alpha);

    private void Sheet(VfxCanvas canvas, string title)
    {
        VfxDirector.Instance.SetFrozen(false);
        VfxDirector.Instance.ClearAll();
        ClearFurniture();
        _backdrop.Visible = true;
        _backdrop.Color = canvas == VfxCanvas.Light ? PaleBackdrop : canvas == VfxCanvas.Dark ? DarkBackdrop : new Color("6db8d8");
        Text(title, new Vector2(6f, 4f), InkOn(canvas, 0.55f), 10);
    }

    private void Play(in Row row, string style, Vector2 at, VfxCanvas canvas, float hold, VfxLook look = null, uint seed = 0u)
    {
        VfxDirector director = VfxDirector.Instance;
        Vector2 dir = new Vector2(row.DirX, row.DirY);
        if (dir.LengthSquared() > 0.01f) dir = dir.Normalized();
        Vector2 pos = at + new Vector2(row.X, row.Y);
        director.Preview(row.Archetype, style, pos, dir, row.Size, row.Element, canvas, look, row.Stage, row.Flags, row.Power,
            new Vector2(row.ExtX, row.ExtY), Mathf.DegToRad(row.ArcDeg), row.Magnitude, seed, hold);
        if (row.Second == VfxArchetype.Count) return;
        director.Preview(row.Second, style, at + new Vector2(row.SecondX, row.Y), dir, row.SecondSize, row.SecondElement, canvas, look,
            row.SecondStage, VfxFlags.None, row.Power, default, 0f, 0f, seed + 17u, hold);
    }

    // ── 1. the grid ──────────────────────────────────────────────────────────────
    private async Task Grid(int sheet, VfxCanvas canvas)
    {
        VfxStyle[] styles = VfxTable.Styles;
        float cellW = (ViewW - LabelW) / styles.Length, cellH = (ViewH - HeaderH) / 5f;
        Sheet(canvas, "");
        for (int c = 0; c < styles.Length; c++)
            Text(styles[c].Id, new Vector2(LabelW + c * cellW, 5f), InkOn(canvas, 0.85f), 11, cellW, HorizontalAlignment.Center);
        await Frames(2);
        VfxDirector.Instance.SetFrozen(true);
        for (int r = 0; r < 5 && sheet * 5 + r < Rows.Length; r++)
        {
            Row row = Rows[sheet * 5 + r];
            float cy = HeaderH + (r + 0.5f) * cellH;
            Text(row.Label, new Vector2(6f, cy - 8f), InkOn(canvas, 0.8f), 10, LabelW - 10f);
            for (int c = 0; c < styles.Length; c++)
            {
                var centre = new Vector2(LabelW + (c + 0.5f) * cellW, cy);
                if (row.Body) Guide(new Rect2(centre + new Vector2(row.X - 9f, -22f), new Vector2(18f, 44f)), InkOn(canvas, 0.13f));
                if (row.Ground) Guide(new Rect2(centre + new Vector2(-cellW * 0.42f, row.Y), new Vector2(cellW * 0.84f, 1.5f)), InkOn(canvas, 0.22f));
                Play(in row, styles[c].Id, centre, canvas, row.Hold, null, (uint)(1000 + sheet * 100 + r * 10 + c));
            }
        }
        await Frames(4);
        await Shot($"grid_{sheet + 1}_{(canvas == VfxCanvas.Light ? "pale" : "dark")}");
    }

    // ── 2. the protagonists' own colours ─────────────────────────────────────────
    private async Task Looks()
    {
        string[] owners = { "pomegraknight", "cleopastar", "sifu_pangda", "pumpking", "pixolotl" };
        int[] rows = { 2, 3, 0, 1, 4, 15 };                        // slash, burning slash 3, hit, kill, burst, death
        string[] heads = { "slash 1", "slash 3 · fire", "hit", "massive hit", "blast", "death" };
        float cellW = (ViewW - LabelW) / 12f, cellH = (ViewH - HeaderH) / owners.Length;
        Sheet(VfxCanvas.Dark, "");
        // The right half is pale, so both canvases are on one sheet.
        Guide(new Rect2(LabelW + 6f * cellW, 0f, 6f * cellW, ViewH), PaleBackdrop);
        await Frames(2);
        VfxDirector.Instance.SetFrozen(true);
        for (int o = 0; o < owners.Length; o++)
        {
            VfxLook look = VfxTable.LookOf(owners[o]);
            float cy = HeaderH + (o + 0.5f) * cellH;
            Text(owners[o] + "\n" + look.Style + (look.Provisional ? " (provisional)" : ""), new Vector2(6f, cy - 14f), InkOn(VfxCanvas.Dark, 0.85f), 10, LabelW - 8f);
            for (int half = 0; half < 2; half++)
            {
                VfxCanvas canvas = half == 0 ? VfxCanvas.Dark : VfxCanvas.Light;
                for (int k = 0; k < rows.Length; k++)
                {
                    Row row = Rows[rows[k]];
                    var centre = new Vector2(LabelW + (half * 6 + k + 0.5f) * cellW, cy);
                    if (row.Archetype == VfxArchetype.SlashArc && row.Stage == 3) row = row with { Element = VfxElement.Fire };
                    VfxLook use = row.Element == VfxElement.Fire && row.Archetype != VfxArchetype.Burst ? null : look;
                    Play(in row, look.Style, centre, canvas, row.Hold, use, (uint)(5000 + o * 100 + half * 10 + k));
                    if (o == 0) Text(heads[k], new Vector2(LabelW + (half * 6 + k) * cellW, 5f), InkOn(canvas, 0.8f), 9, cellW, HorizontalAlignment.Center);
                }
            }
        }
        await Frames(4);
        await Shot("looks_protagonists");
    }

    // ── 3. how a mark ends ───────────────────────────────────────────────────────
    private async Task Timing(string name, Row row)
    {
        VfxStyle[] styles = VfxTable.Styles;
        float[] moments = { 0.06f, 0.22f, 0.42f, 0.66f, 0.90f };
        float cellW = (ViewW - LabelW) / styles.Length, cellH = (ViewH - HeaderH) / moments.Length;
        Sheet(VfxCanvas.Dark, "");
        for (int c = 0; c < styles.Length; c++)
            Text($"{styles[c].Id} · {(styles[c].Fps > 0f ? styles[c].Fps + " fps" : "smooth")}", new Vector2(LabelW + c * cellW, 5f), InkOn(VfxCanvas.Dark, 0.85f), 10, cellW, HorizontalAlignment.Center);
        await Frames(2);
        VfxDirector.Instance.SetFrozen(true);
        for (int r = 0; r < moments.Length; r++)
        {
            float cy = HeaderH + (r + 0.5f) * cellH;
            Text($"{row.Label}\nat {moments[r] * 100f:0} % of its life", new Vector2(6f, cy - 14f), InkOn(VfxCanvas.Dark, 0.8f), 10, LabelW - 10f);
            for (int c = 0; c < styles.Length; c++)
                Play(in row, styles[c].Id, new Vector2(LabelW + (c + 0.5f) * cellW, cy), VfxCanvas.Dark, moments[r], null, (uint)(9000 + c));
        }
        await Frames(4);
        await Shot(name);
    }

    // ── 4. each realm's air ──────────────────────────────────────────────────────
    private async Task Air()
    {
        (string World, string Canvas)[] realms =
        {
            ("VK", "6db8d8"), ("SL", "17133a"), ("BM", "e4e9e8"), ("HC", "0e0d12"), ("TD", "f1e3c4"),
            ("PL", "0c1230"), ("KH", "111b3d"), ("TF", "0c0c22"), ("CC", "2a1b4a"), ("VOID", "050508"),
        };
        foreach ((string world, string hex) in realms)
        {
            var canvas = new Color(hex);
            VfxDirector.Instance.SetFrozen(false);
            VfxDirector.Instance.ClearAll();
            ClearFurniture();
            _backdrop.Visible = true;
            _backdrop.Color = canvas;
            VfxDirector.Instance.PreviewArena(world, canvas.Luminance);
            VfxStyle style = VfxDirector.Instance.ArenaStyle;
            Text($"{world} · {style.Medium} · ambience ({VfxDirector.Instance.LiveParticles} particles)", new Vector2(8f, 6f),
                InkOn(VfxDirector.CanvasFor(canvas.Luminance), 0.7f), 12);
            await Seconds(1.6f);
            await Shot("air_" + world.ToLowerInvariant());
        }
        VfxDirector.Instance.LeaveArena();
    }

    // ── 5. a staged fight in the real arena ──────────────────────────────────────
    private async Task Fight(string map)
    {
        _backdrop.Visible = false;
        ClearFurniture();
        _missionStarted = false;
        var arena = ResourceLoader.Load<PackedScene>("res://Scenes/Arena.tscn").Instantiate<GameManager>();
        arena.DebugMission = MissionType.Slaughter;
        arena.DebugCombatMapPath = "res://Maps/" + map + ".json";
        arena.DebugFoeLevel = 2;
        arena.PreCombatCountdownSeconds = 1.2f;
        AddChild(arena);
        await Frames(4);

        // The debug arena's authored body is not Pomegraknight: wear her the way the debug
        // page does (debug mode on for the swap, then off again).
        PressDebugToggle();
        bool swapped = arena.DebugSwapProtagonist("Pomegraknight");
        PressDebugToggle();
        await Frames(3);
        CharacterController player = CharacterController.LocalPlayer;
        if (player == null) { GD.PushError($"VfxGallery: {map} staged no player"); arena.QueueFree(); await Frames(4); return; }
        player.UseControls(_pad);
        player.ShowDebugRanges = false;
        player.Invincible = true;
        _pad.Facing = 1f;
        VfxDirector director = VfxDirector.Instance;
        director.ResetCounters();
        long commands = VfxPen.Commands, triangles = VfxPen.Triangles;
        GD.Print($"VfxGallery: fight on {map} — {player.FeedbackId} (swapped in: {swapped}), world '{director.ArenaWorld}', medium {director.ArenaStyle.Id}, canvas {director.ArenaCanvas}, ambience {director.LiveParticles} particles, flash {(FeelDirector.Instance.ArenaIsLight ? "ink" : "white")}");

        await Until(() => _missionStarted, 8f);
        await Seconds(0.5f);
        Node entities = arena.GetNode(arena.EntitiesPath);
        CrabFoe Crab(float dx)
        {
            var crab = ResourceLoader.Load<PackedScene>("res://Scenes/Foes/CrabFoe.tscn").Instantiate<CrabFoe>();
            entities.AddChild(crab);
            crab.GlobalPosition = player.GlobalPosition + new Vector2(dx, -6f);
            crab.Init(3);
            return crab;
        }
        var staged = new List<CrabFoe>();
        async Task Stage(params float[] offsets)
        {
            foreach (CrabFoe old in staged) if (IsInstanceValid(old)) old.QueueFree();
            staged.Clear();
            await Frames(2);
            foreach (float dx in offsets) staged.Add(Crab(dx));
            await Seconds(0.36f);                                   // their own spawn pops are over; not long enough to close in
        }
        string tag = "fight_" + map;
        float draws = (float)Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame);
        await Stage(84f, 116f);
        await Shot(tag + "_0_stand");

        // The combo: a flash frame and an arc frame for each of the three slashes.
        for (int stage = 1; stage <= 3; stage++)
        {
            await Stage(84f, 116f);
            int swings = _swings, hits = _hits;
            _pad.Tap(ControlAction.Attack);
            await Until(() => _swings > swings, 1.5f);
            await Shot($"{tag}_{stage}a_slash{stage}_flash");
            LogCamera($"slash {stage} ({_hits - hits} hit)");
            await Frames(4);
            await Shot($"{tag}_{stage}b_slash{stage}_arc");
            await Seconds(0.30f);
        }
        await Seconds(0.5f);

        // A kill: the third slash finishing a crab (a Medium hit, as her real one is).
        await Stage(80f);
        staged[0].TakeHit(new HitInfo(4000f, new Vector2(260f, -160f)).From(player, "ba", 3, Fableland.Data.HitWeight.Medium), player.GlobalPosition);
        await Shot(tag + "_4a_kill_flash");
        LogCamera("a kill");
        await Frames(6);
        await Shot(tag + "_4b_kill");
        await Seconds(0.5f);

        // The player is hit: the flash on her, in this arena's flash colour.
        await Stage(72f);
        player.Invincible = false;
        player.TakeHit(new HitInfo(36f, new Vector2(-220f, -120f)).From(staged[0], "contact"), staged[0].GlobalPosition);
        await Shot(tag + "_5a_player_hit");
        LogCamera("the player hit");
        await Frames(2);
        await Shot(tag + "_5b_player_hit_after");
        player.Invincible = true;
        await Seconds(0.8f);
        await Stage(80f, -84f);

        // Blush, then the burning slash that starts the Fire Tornado.
        _pad.Tap(ControlAction.Skill1);
        await Seconds(0.22f);
        await Shot(tag + "_6_blush");
        int before = _swings;
        _pad.Tap(ControlAction.Attack);
        await Until(() => _swings > before, 1.5f);
        await Frames(5);
        await Shot(tag + "_7_burning_slash");
        await Seconds(0.70f);
        await Shot(tag + "_8_tornado");
        await Seconds(2.3f);

        // The seed eruption: the fan leaving, then seeds dying on the ground.
        int waves = _waves;
        _pad.Tap(ControlAction.Skill2);
        await Until(() => _waves > waves, 2f);
        await Frames(5);
        await Shot(tag + "_9_seed_wave");
        await Seconds(0.75f);
        await Shot(tag + "_10_seeds_land");
        await Seconds(2.2f);

        // A jump and its landing.
        _pad.Tap(ControlAction.Jump);
        await Frames(5);
        await Shot(tag + "_11_jump");
        int lands = _lands;
        await Until(() => _lands > lands, 2.5f);
        await Frames(3);
        await Shot(tag + "_12_land");
        await Seconds(0.4f);

        float drawsNow = (float)Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame);
        GD.Print($"VfxGallery: {map} — peak {director.PeakMarks} marks / {director.PeakBursts} bursts / {director.PeakParticles} particles · started {director.Started[0]} critical, {director.Started[1]} confirm, {director.Started[2]} cosmetic · dropped {director.Dropped[0]}/{director.Dropped[1]}/{director.Dropped[2]} · stolen {director.Stolen} · {VfxPen.Commands - commands} canvas commands, {VfxPen.Triangles - triangles} triangles in all · draw calls in a frame {draws:0} standing, {drawsNow:0} at the end");

        _pad.ReleaseAll();
        GetTree().Paused = false;
        arena.QueueFree();
        await Frames(6);
    }

    /// <summary>Press the debug overlay's own toggle, as a user would.</summary>
    private static void PressDebugToggle()
    {
        Node overlay = Fableland.Debug.DebugManager.Instance;
        if (overlay == null) return;
        foreach (Node node in overlay.FindChildren("*", "Button", true, false))
        {
            if (node is not Button button || !button.Text.StartsWith("DBG")) continue;
            button.EmitSignal(BaseButton.SignalName.Pressed);
            return;
        }
    }

    private static void LogCamera(string moment)
    {
        ShakeCamera2D view = ShakeCamera2D.Instance;
        if (view == null) return;
        GD.Print($"VfxGallery:   camera at {moment}: trauma {view.Trauma:0.00}, offset {view.Offset.X:0.0},{view.Offset.Y:0.0} px, zoom {view.Zoom.X:0.000}");
    }
}
