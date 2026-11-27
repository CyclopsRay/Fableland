namespace Fableland.Run.Dev;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Godot;
using Fableland.Bosses;
using Fableland.Data;
using Fableland.Map;

/// <summary>
/// Headless check for the rules of a place — realm laws and zone 6 (Docs/Zone6.gdd, 40-QA.md §2):
///
///     Godot --headless --path . res://Scenes/Run/Zone6Check.tscn      # exit 0 = clean
///
/// 1. Logic — the tables validate; the gravity ladder, the seals and Māra's seals
///    (<see cref="Zone6State.SelfTest"/>); the rules record (<see cref="ArenaRules.Build"/>):
///    a Wulin node's law, a zone-6 node's rules and never a borrowed realm's law; which nodes
///    are zone-6 fights; the Styx Tide's schedule.
/// 2. The variant — the same for a seed every time, both variants dealt, drawn from a stream
///    of its own: the run's root stream and the map it generates are untouched.
/// 3. Saves — seals and the ladder survive a save and a load, and nothing a run does clears a
///    seal; a save from before v0.12.0 (no zone 6 in it) loads into an empty zone 6.
/// 4. Bodies, on plain colliders — the Wulin law on a real fighter (one more jump, each 75% as
///    high); gravity on a fighter, a foe and a projectile; Avīci's seals on every protagonist
///    of the roster (reload, damage taken, cooldowns, move speed, attack interval); Māra's
///    stand-in losing HP and damage for each power sealed in him.
/// 5. The Styx Tide — lanes of fighters jumping a real wave at swept moments, at gravity
///    ×0.5, ×1 and ×1.5: the window in which a jump clears it. The numbers print as `zone6`
///    lines and are the ones Zone6.gdd quotes.
///
/// Each failure prints on its own `FAIL` line. Slot files go to a scratch folder.
/// </summary>
public partial class Zone6Check : Node
{
    private readonly List<string> _problems = new();
    private int _checks;

    private const int SpeedUp = 8;
    private const float Tick = 1f / 60f;
    private const float FloorY = 600f;
    private const string PlayerScene = "res://Scenes/Pomegraknight.tscn";
    private const string CrabScene = "res://Scenes/Foes/CrabFoe.tscn";
    private const string SeedScene = "res://Scenes/PomeSeed.tscn";

    // Pads this check drives. The check node comes before every stage in the tree, so its
    // physics step — tap, then step the pads — runs before the bodies read them, each tick.
    private readonly List<VirtualPad> _pads = new();
    private Action<int> _onTick;
    private int _tick;
    private string _scratch;

    public override void _Process(double delta)
    {
        foreach (VirtualPad pad in _pads) pad.StepProcess();
    }

    public override void _PhysicsProcess(double delta)
    {
        _tick++;
        _onTick?.Invoke(_tick);
        foreach (VirtualPad pad in _pads) pad.StepPhysics();
    }

    public override async void _Ready()
    {
        int ticksPerSecond = Engine.PhysicsTicksPerSecond;
        double timeScale = Engine.TimeScale;
        string savesBefore = SaveGameService.DirectoryOverride;
        string forcedBefore = Zone6State.ForcedVariantId;
        _scratch = Path.Combine(Path.GetTempPath(), "fableland-zone6-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(_scratch);
            SaveGameService.DirectoryOverride = _scratch;      // never the player's own slots
            Zone6State.ForcedVariantId = null;

            CheckLogic();
            CheckTheVariant();
            CheckSaves();

            Engine.PhysicsTicksPerSecond = ticksPerSecond * SpeedUp;
            Engine.TimeScale = SpeedUp;
            await Ticks(1);
            await ProbeQinggong();
            await ProbeGravity();
            await ProbeSeals();
            await ProbeMara();
            await ProbeStyxTide();
        }
        catch (Exception e)
        {
            _problems.Add("zone-6 check threw: " + e);
        }
        finally
        {
            Engine.PhysicsTicksPerSecond = ticksPerSecond;
            Engine.TimeScale = timeScale;
            SaveGameService.DirectoryOverride = savesBefore;
            Zone6State.ForcedVariantId = forcedBefore;
            _onTick = null;
            try { if (Directory.Exists(_scratch)) Directory.Delete(_scratch, true); }
            catch { /* a leftover scratch folder in the temp directory is harmless */ }
        }

        foreach (string problem in _problems) GD.Print("FAIL  " + problem);
        GD.Print(_problems.Count == 0
            ? $"Zone6Check: OK ({_checks} checks)"
            : $"Zone6Check: {_problems.Count} problem(s) in {_checks} checks");
        GetTree().Quit(_problems.Count == 0 ? 0 : 1);
    }

    private void Expect(bool ok, string what)
    {
        _checks++;
        if (!ok) _problems.Add(what);
    }

    private void Absorb(string suite, List<string> failures)
    {
        _checks++;
        foreach (string failure in failures) _problems.Add(suite + ": " + failure);
    }

    private async Task Ticks(int count)
    {
        for (int i = 0; i < count; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }

    private static string M(float px) => (px / Units.PixelsPerMeter).ToString("0.00");
    private static bool Near(float a, float b, float tolerance) => Mathf.Abs(a - b) <= tolerance;

    // ---------------------------------------------------------------- 1. logic

    private void CheckLogic()
    {
        Absorb("RealmLawTable", RealmLawTable.Validate());
        Absorb("Zone6Table", Zone6Table.Validate());
        Absorb("BossTable", BossTable.Validate());
        Absorb("Zone6State", Zone6State.SelfTest());
        Expect(SaveGameService.RoundTripSelfTest(out string error), "save DTO: the round trip lost a field: " + error);

        // A realm's law, on its realm's nodes only.
        ArenaRules wulin = ArenaRules.Build("BM", null, Zone6Role.None);
        Expect(wulin.HasLaw && wulin.LawExtraJumps == 1 && Near(wulin.LawJumpHeightScale, 0.75f, 1e-6f) && !wulin.LawAppliesToFoes && !wulin.InZone6,
            "rules: a Wulin node must carry Qinggong — one more jump, each 0.75 as high, fighters only");
        List<string> wulinLines = wulin.HudLines();
        Expect(wulinLines.Count == 1 && wulinLines[0].Contains("Qinggong") && wulinLines[0].Contains("轻功"),
            $"rules: a Wulin fight shows '{string.Join(" | ", wulinLines)}'; it must show its law in one line");
        Expect(!ArenaRules.Build("VK", null, Zone6Role.None).HasLaw && ArenaRules.Build("VK", null, Zone6Role.None).HudLines().Count == 0,
            "rules: the Vanilla Kingdom has no law yet and must show none");

        // Zone 6: its own rules, never the law of the world whose map it borrows.
        var vf = new Zone6State { VariantId = Zone6Table.VoidFrontierId };
        vf.RecordWin("XX-5-1");
        ArenaRules mini = ArenaRules.Build("BM", vf, Zone6Role.MiniBoss);
        Expect(!mini.HasLaw && mini.InZone6 && mini.SurvivableDefeat && Near(mini.Zone6Gravity, 0.8f, 1e-6f) && Near(mini.FighterGravity, 0.8f, 1e-6f),
            "rules: a Void Frontier mini-boss after one win must carry gravity ×0.8, be survivable, and apply no borrowed realm's law");
        ArenaRules core = ArenaRules.Build("XX", vf, Zone6Role.Final);
        Expect(core.InZone6 && !core.SurvivableDefeat, "rules: the core keeps permadeath");

        var av = new Zone6State { VariantId = Zone6Table.AviciId };
        av.RecordLoss("XX-5-1");                                     // 贪 Greed: Hunger ×1
        av.RecordLoss("XX-5-4");
        av.RecordLoss("XX-5-4");                                     // 慢 Pride: Burden ×2
        av.RecordWin("XX-5-2");
        av.RecordWin("XX-5-5");                                      // Wrath and Doubt sealed in Māra
        ArenaRules gate = ArenaRules.Build("XX", av, Zone6Role.MiniBoss);
        Expect(Near(gate.SealReloadScale, 1.3f, 1e-5f) && Near(gate.SealMoveSpeedScale, 0.7f, 1e-5f)
               && Near(gate.SealDamageTakenScale, 1f, 1e-6f) && Near(gate.Zone6Gravity, 1f, 1e-6f),
            $"rules: Hunger ×1 and Burden ×2 must give reload ×1.3 and move ×0.7 (got ×{gate.SealReloadScale} and ×{gate.SealMoveSpeedScale}); Avīci never changes gravity");
        Expect(gate.SealLine.Contains("Hunger ×1") && gate.SealLine.Contains("Burden ×2"), $"rules: the seal line reads '{gate.SealLine}'");
        Expect(ArenaRules.Build("VK", av, Zone6Role.None).HasSeals, "rules: a seal is on you everywhere — an outer realm's fight too");
        ArenaRules mara = ArenaRules.Build("XX", av, Zone6Role.Final);
        Expect(mara.SealedBossAbilities.Count == 2 && Near(mara.FinalBossHpScale, 0.76f, 1e-5f) && Near(mara.FinalBossDamageBonus, -0.2f, 1e-5f)
               && mara.FinalBossLine.Contains("Wrath") && mara.FinalBossLine.Contains("Doubt"),
            $"rules: two powers sealed in Māra must cost his stand-in 24% HP and 20 points of damage (×{mara.FinalBossHpScale}, {mara.FinalBossDamageBonus:+0.00;-0.00}, '{mara.FinalBossLine}')");
        Expect(Near(100f / (100f + ArenaRulesApplier.DefenseForDamageTaken(1.15f)), 1.15f, 1e-4f),
            "rules: the defense a seal sets must make damage taken exactly its scale");

        // Which nodes are zone-6 fights, and what they are fought as.
        MapGraph graph = MapGenerator.Generate("ZONE6CHECK");
        int minis = 0, finals = 0, wrong = 0;
        foreach (MapNode node in graph.Nodes)
        {
            Zone6Role role = RunState.Zone6RoleOf(node);
            if (node.WorldIndex != -1 || !node.IsCombat) { if (role != Zone6Role.None) wrong++; continue; }
            if (role == Zone6Role.MiniBoss) minis++;
            else if (role == Zone6Role.Final) finals++;
            if (RunState.EffectiveMission(node) != MissionType.Boss) wrong++;
        }
        Expect(minis == 5 && finals == 1 && wrong == 0,
            $"nodes: a map holds {minis} zone-6 mini-boss nodes and {finals} final (and {wrong} misfiled); every zone-6 combat node is a boss fight — five gates and the core");

        CheckStyxSchedule();
    }

    private void CheckStyxSchedule()
    {
        var launched = new List<float>();
        var schedule = new StyxTideSchedule(direction => { launched.Add(direction); return null; }, new DetRandom("styx-check"));
        void Run(float seconds, float hp)
        {
            for (float t = 0f; t < seconds; t += Tick) schedule.Tick(Tick, hp);
        }
        Run(5.9f, 1f);
        Expect(launched.Count == 0, $"styx: {launched.Count} wave(s) before {StyxTideSchedule.FirstWaveSeconds} s");
        Run(1.1f, 1f);
        Expect(launched.Count == 1, $"styx: {launched.Count} wave(s) by 7 s at full HP; one comes at {StyxTideSchedule.FirstWaveSeconds} s");
        Run(9.5f, 0.4f);
        Expect(launched.Count == 3 && launched[1] == launched[2],
            $"styx: below half HP the Rising Tide must send a second wave from the same side ({launched.Count} waves: {string.Join(" ", launched)})");
        Run(9f, 0.2f);
        Expect(launched.Count == 5 && launched[3] == -launched[4],
            $"styx: below a quarter the second wave must come from the other side ({launched.Count} waves: {string.Join(" ", launched)})");
    }

    // ---------------------------------------------------------------- 2. the variant

    private void CheckTheVariant()
    {
        const int Seeds = 400;
        var dealt = new Dictionary<string, int>();
        int unstable = 0;
        for (int i = 0; i < Seeds; i++)
        {
            string seed = "Z6SEED" + i;
            string first = Zone6Table.VariantForSeed(seed);
            if (Zone6Table.VariantForSeed(seed) != first || Zone6State.VariantFor(seed) != first) unstable++;
            dealt[first] = dealt.GetValueOrDefault(first) + 1;
        }
        Expect(unstable == 0, $"variant: {unstable} of {Seeds} seeds drew a different variant when asked again");
        var tally = new List<string>();
        foreach (Zone6VariantDef variant in Zone6Table.Variants)
        {
            int n = dealt.GetValueOrDefault(variant.Id);
            tally.Add($"{variant.Id} {n}");
            Expect(n >= Seeds * 0.35f && n <= Seeds * 0.65f, $"variant: {variant.Id} was dealt to {n} of {Seeds} seeds; a rotation of two deals each to about half");
        }

        // Its own stream: the run's root stream and its map are what they were before zone 6 had variants.
        RunState run = RunState.Instance;
        run.NewRun("WALKVOID");
        string signature = RunWalkCheck.Signature(run.Graph);
        Expect(run.Rng.NextULong() == new DetRandom("WALKVOID").NextULong(), "variant: drawing zone 6's variant took a number from the run's root stream");
        Expect(signature == RunWalkCheck.Signature(MapGenerator.Generate("WALKVOID")),
            "variant: a run's map is not the map its seed generates on its own");
        Expect(run.Zone6.VariantId == Zone6Table.VariantForSeed("WALKVOID") && run.Zone6.PlayerSeals.Count == 0 && Near(run.Zone6.Gravity, 1f, 1e-6f),
            "variant: a new run must start an empty zone 6 on its seed's variant");

        // The debug knob forces it, and lets go.
        Zone6State.ForcedVariantId = Zone6Table.AviciId;
        run.NewRun("WALKVOID");
        string forced = run.Zone6.VariantId;
        Zone6State.ForcedVariantId = Zone6Table.VoidFrontierId;
        run.NewRun("WALKVOID");
        string forcedToo = run.Zone6.VariantId;
        Zone6State.ForcedVariantId = null;
        Expect(forced == Zone6Table.AviciId && forcedToo == Zone6Table.VoidFrontierId, "variant: the debug knob does not force the variant");
        GD.Print($"  zone6    the variant    {Seeds} seeds: {string.Join(" · ", tally)} · the same every time · its own stream (WALKVOID still {signature})");
    }

    // ---------------------------------------------------------------- 3. saves

    private void CheckSaves()
    {
        RunState run = RunState.Instance;
        Zone6State.ForcedVariantId = Zone6Table.AviciId;
        bool started = run.StartNewRunInSlot(0, "Z6SAVES", out string error);
        Zone6State.ForcedVariantId = null;
        Expect(started, "saves: a run could not be started in a scratch slot: " + error);
        if (!started) return;

        run.Zone6.RecordLoss("XX-5-1");     // Hunger
        run.Zone6.RecordLoss("XX-5-4");     // Burden
        run.Zone6.RecordWin("XX-5-2");      // Wrath sealed in Māra
        // Nothing a run does clears a seal: a blessing, a heal, a spent shelter.
        run.RestBlessing();
        run.HealParty(1f);
        run.ConsumeBlessing(run.Graph.Nodes.Find(node => node.Kind == NodeKind.Shelter)?.Id);
        Expect(run.Zone6.SealStacks("greed") == 1 && run.Zone6.SealStacks("pride") == 1, "saves: a blessing or a heal cleared a seal; a seal cannot be cleansed");

        Expect(run.SaveActiveRun(out error), "saves: the run did not save: " + error);
        run.NewRun("SOMETHINGELSE");
        bool loaded = run.TryLoadRunFromSlot(0, out error);
        Expect(loaded, "saves: the run did not load: " + error);
        if (loaded)
            Expect(run.Zone6.VariantId == Zone6Table.AviciId && run.Zone6.SealStacks("greed") == 1 && run.Zone6.SealStacks("pride") == 1
                   && run.Zone6.MaraSealed.Contains("anger") && run.Zone6.Nodes.TryGetValue("XX-5-4", out Zone6NodeRecord record) && record.Losses == 1,
                $"saves: zone 6 did not survive a save and a load ({run.Zone6.StatusLine()})");

        // A save from before v0.12.0 is this file without its zone 6 — in the run and in its checkpoint.
        string slot1 = Path.Combine(_scratch, "slot_1.json");
        string slot2 = Path.Combine(_scratch, "slot_2.json");
        JsonNode json = JsonNode.Parse(File.ReadAllText(slot1));
        bool had = json is JsonObject root && root.Remove("zone6");
        if (json?["lastDayCheckpoint"] is JsonObject checkpoint) checkpoint.Remove("zone6");
        Expect(had, "saves: a v0.12.0 save carries no 'zone6'");
        File.WriteAllText(slot2, json.ToJsonString());
        bool old = run.TryLoadRunFromSlot(1, out error);
        Expect(old, "saves: a save without zone 6 (from before v0.12.0) does not load: " + error);
        if (old)
            Expect(run.Zone6.VariantId == Zone6Table.VariantForSeed("Z6SAVES") && run.Zone6.PlayerSeals.Count == 0
                   && run.Zone6.MaraSealed.Count == 0 && run.Zone6.Nodes.Count == 0 && Near(run.Zone6.Gravity, 1f, 1e-6f),
                $"saves: a save from before v0.12.0 must load into an empty zone 6 on its seed's variant ({run.Zone6.StatusLine()})");
        GD.Print($"  zone6    saves          seals, Māra's seals and the per-node record survive a save and a load; a blessing and a heal leave them · a save without zone 6 loads an empty one on its seed's variant ({Zone6Table.VariantForSeed("Z6SAVES")})");
    }

    // ---------------------------------------------------------------- stage

    private Node2D BuildStage(float x0, float x1, float floorY = FloorY)
    {
        var stage = new Node2D { Name = "Stage" };
        AddChild(stage);
        AddBox(stage, new Rect2(x0, floorY, x1 - x0, 200f));
        AddBox(stage, new Rect2(x0 - 40f, floorY - 2000f, 40f, 2200f));
        AddBox(stage, new Rect2(x1, floorY - 2000f, 40f, 2200f));
        return stage;
    }

    private static void AddBox(Node2D stage, Rect2 rect)
    {
        var body = new StaticBody2D { Position = rect.GetCenter(), CollisionLayer = 0, CollisionMask = 0 };
        body.SetCollisionLayerValue(Units.LayerGround, true);
        body.AddChild(new CollisionShape2D { Shape = new RectangleShape2D { Size = rect.Size } });
        stage.AddChild(body);
    }

    private static T Spawn<T>(Node parent, string scenePath, Vector2 at) where T : Node2D
    {
        var node = ResourceLoader.Load<PackedScene>(scenePath).Instantiate<T>();
        parent.AddChild(node);
        node.GlobalPosition = at;
        if (node is CharacterController body) body.SetSpawnPoint(at);
        return node;
    }

    /// <summary>A fighter on its own pad, standing on the floor.</summary>
    private CharacterController Fighter(Node2D stage, string scenePath, Vector2 at, out VirtualPad pad)
    {
        CharacterController body = Spawn<CharacterController>(stage, scenePath, at);
        pad = new VirtualPad();
        body.UseControls(pad);
        _pads.Add(pad);
        return body;
    }

    private async Task Strike(Node2D stage)
    {
        _onTick = null;
        _pads.Clear();
        stage.QueueFree();
        await Ticks(2);
    }

    // ---------------------------------------------------------------- 4a. Qinggong

    /// <summary>The Wulin law on real bodies: the same fighter with and without it, jumping
    /// together — the law's gets two jumps where the other gets one, each 75% as high.</summary>
    private async Task ProbeQinggong()
    {
        Node2D stage = BuildStage(0f, 4000f);
        CharacterController plain = Fighter(stage, PlayerScene, new Vector2(800f, FloorY - 32f), out VirtualPad plainPad);
        CharacterController light = Fighter(stage, PlayerScene, new Vector2(2400f, FloorY - 32f), out VirtualPad lightPad);
        ArenaRulesApplier.ApplyToFighter(light, ArenaRules.Build("BM", null, Zone6Role.None));
        await Ticks(20);

        Expect(plain.JumpCharges == 1 && light.JumpCharges == 2, $"qinggong: jumps {plain.JumpCharges} without the law and {light.JumpCharges} with it; Pomegraknight has one, and Wulin gives one more");
        Expect(Near(light.JumpHeightScale, 0.75f, 1e-6f) && Near(light.JumpLaunchScale, Mathf.Sqrt(0.75f), 1e-5f), "qinggong: the law must make every jump 0.75 as high");
        Expect(Near(light.JumpCooldown, plain.JumpCooldown, 1e-6f) && Near(light.CoyoteTime, plain.CoyoteTime, 1e-6f), "qinggong: coyote time and the jump cooldown must not change");

        // Every tick is sampled where the check runs — before the bodies move that tick — so
        // sample t is where each body stood after tick t−1.
        float plainFloor = plain.GlobalPosition.Y, lightFloor = light.GlobalPosition.Y;
        const int Ticked = 80;
        var plainY = new float[Ticked + 1];
        var lightY = new float[Ticked + 1];
        var plainVy = new float[Ticked + 1];
        var lightVy = new float[Ticked + 1];
        int t0 = _tick;
        _onTick = tick =>
        {
            int t = tick - t0;
            if (t > Ticked) return;
            plainY[t] = plain.GlobalPosition.Y;
            lightY[t] = light.GlobalPosition.Y;
            plainVy[t] = plain.Velocity.Y;
            lightVy[t] = light.Velocity.Y;
            if (t == 1) { plainPad.Tap(ControlAction.Jump); lightPad.Tap(ControlAction.Jump); }
            if (t == 40) { plainPad.Tap(ControlAction.Jump); lightPad.Tap(ControlAction.Jump); }   // in the air, past the cooldown
            if (t == 62) lightPad.Tap(ControlAction.Jump);                                          // a third: none left
        };
        await Ticks(Ticked + 2);
        _onTick = null;

        float plainTop = plainFloor, lightTop = lightFloor;
        for (int t = 1; t <= 39; t++)
        {
            plainTop = Mathf.Min(plainTop, plainY[t]);
            lightTop = Mathf.Min(lightTop, lightY[t]);
        }
        float plainRise = plainFloor - plainTop, lightRise = lightFloor - lightTop;
        Expect(plainRise > Units.Px(7f) && Near(lightRise / plainRise, 0.75f, 0.03f),
            $"qinggong: the first jump rose {M(lightRise)} m with the law and {M(plainRise)} m without; it must be 0.75 as high");

        // The air jump: the law's body is launched up again, the other falls on.
        float launch = -light.JumpVelocity * light.JumpLaunchScale;
        float lightAfter = Mathf.Min(lightVy[42], lightVy[43]);
        Expect(lightVy[40] > 0f && lightAfter < -launch * 0.8f,
            $"qinggong: the air jump did not launch (vertical speed {M(lightVy[40])} → {M(lightAfter)} m/s; a jump launches at {M(launch)} m/s up)");
        Expect(plainVy[43] > plainVy[40], $"qinggong: without the law a second jump in the air must do nothing (speed {M(plainVy[40])} → {M(plainVy[43])} m/s)");
        Expect(lightVy[65] > lightVy[62], $"qinggong: a third jump in the air launched (speed {M(lightVy[62])} → {M(lightVy[65])} m/s); the law gives one more, not two");
        GD.Print($"  zone6    qinggong       Pomegraknight rises {M(plainRise)} m on one jump; under Wulin's law {M(lightRise)} m on each of two (×{lightRise / plainRise:0.00}); coyote time and the jump cooldown unchanged");
        await Strike(stage);
    }

    // ---------------------------------------------------------------- 4b. gravity

    /// <summary>The Void Frontier's gravity on a fighter, a foe and a projectile: each pair
    /// falls from the same height, one at ×0.5 — it must fall half as far.</summary>
    private async Task ProbeGravity()
    {
        Node2D stage = BuildStage(0f, 6000f);
        var half = new ArenaRules { Zone6Gravity = 0.5f };
        CharacterController fighter = Spawn<CharacterController>(stage, PlayerScene, new Vector2(600f, FloorY - 1400f));
        CharacterController control = Spawn<CharacterController>(stage, PlayerScene, new Vector2(1200f, FloorY - 1400f));
        fighter.UseControls(new VirtualPad());
        control.UseControls(new VirtualPad());
        CrabFoe crab = Spawn<CrabFoe>(stage, CrabScene, new Vector2(2400f, FloorY - 1400f));
        CrabFoe crabControl = Spawn<CrabFoe>(stage, CrabScene, new Vector2(3000f, FloorY - 1400f));
        ArenaRulesApplier.ApplyToFighter(fighter, half);
        ArenaRulesApplier.ApplyToFoe(crab, half);
        Expect(Near(fighter.GravityScale, 0.5f, 1e-6f) && Near(crab.GravityScale, 0.5f, 1e-6f) && Near(control.GravityScale, 1f, 1e-6f),
            "gravity: the rules did not set ×0.5 on the fighter and the foe");

        // A projectile takes its thrower's share when it is thrown.
        var seedScene = ResourceLoader.Load<PackedScene>(SeedScene);
        PomeSeed light = seedScene.Instantiate<PomeSeed>();
        PomeSeed heavy = seedScene.Instantiate<PomeSeed>();
        stage.AddChild(light);
        stage.AddChild(heavy);
        light.GlobalPosition = new Vector2(4000f, FloorY - 1400f);
        heavy.GlobalPosition = new Vector2(4600f, FloorY - 1400f);
        light.Init(Vector2.Zero, null, false, 1f, fighter);
        heavy.Init(Vector2.Zero, null, false, 1f, control);

        await Ticks(1);
        float[] y0 = { fighter.GlobalPosition.Y, control.GlobalPosition.Y, crab.GlobalPosition.Y, crabControl.GlobalPosition.Y, light.GlobalPosition.Y, heavy.GlobalPosition.Y };
        await Ticks(24);
        float Fell(Node2D n, int i) => n.GlobalPosition.Y - y0[i];
        float fighterRatio = Fell(fighter, 0) / Mathf.Max(1f, Fell(control, 1));
        float crabRatio = Fell(crab, 2) / Mathf.Max(1f, Fell(crabControl, 3));
        float seedRatio = Fell(light, 4) / Mathf.Max(1f, Fell(heavy, 5));
        Expect(Near(fighterRatio, 0.5f, 0.03f), $"gravity: a fighter at ×0.5 fell {M(Fell(fighter, 0))} m to the other's {M(Fell(control, 1))} m (×{fighterRatio:0.00})");
        Expect(Near(crabRatio, 0.5f, 0.03f), $"gravity: a foe at ×0.5 fell {M(Fell(crab, 2))} m to the other's {M(Fell(crabControl, 3))} m (×{crabRatio:0.00})");
        Expect(Near(seedRatio, 0.5f, 0.03f), $"gravity: a seed thrown at ×0.5 fell {M(Fell(light, 4))} m to the other's {M(Fell(heavy, 5))} m (×{seedRatio:0.00})");
        GD.Print($"  zone6    gravity ×0.5   in 0.4 s a fighter falls ×{fighterRatio:0.00}, a crab ×{crabRatio:0.00}, a thrown seed ×{seedRatio:0.00} as far as at ×1");
        await Strike(stage);
    }

    // ---------------------------------------------------------------- 4c. seals

    /// <summary>
    /// Avīci's seals on every protagonist of the roster — the body a switch-in puts on the
    /// field is the same scene. One stack of each of the five: reload +30%, damage taken +15%,
    /// cooldowns +25%, move speed −15%, attack interval +20%, each measured against the same
    /// kit without seals.
    /// </summary>
    private async Task ProbeSeals()
    {
        var all = new Zone6State { VariantId = Zone6Table.AviciId };
        for (int i = 1; i <= 5; i++) all.RecordLoss($"XX-5-{i}");
        ArenaRules rules = ArenaRules.Build("XX", all, Zone6Role.MiniBoss);
        var lines = new List<string>();
        int kits = 0;

        foreach ((string id, string scenePath) in Debug.ProtagonistRoster.Entries)
        {
            Node2D stage = BuildStage(0f, 6000f);
            CharacterController seal = Fighter(stage, scenePath, new Vector2(800f, FloorY - 40f), out VirtualPad sealPad);
            CharacterController free = Fighter(stage, scenePath, new Vector2(3200f, FloorY - 40f), out VirtualPad freePad);
            ArenaRulesApplier.ApplyToFighter(seal, rules);
            await Ticks(20);
            string who = $"seals: {id}";

            Expect(Near(seal.ReloadScale, 1.3f, 1e-4f) && Near(seal.CooldownScale, 1.25f, 1e-4f) && Near(seal.MoveSpeedScale, 0.85f, 1e-4f),
                $"{who}: the body carries reload ×{seal.ReloadScale}, cooldowns ×{seal.CooldownScale}, move ×{seal.MoveSpeedScale}");
            if (free.ReloadSeconds > 0f)
                Expect(Near(seal.ReloadSeconds / free.ReloadSeconds, 1.3f, 0.01f), $"{who}: reloads in {seal.ReloadSeconds:0.00} s, the kit in {free.ReloadSeconds:0.00} s; Hunger adds 30%");
            if (free.AttackIntervalSeconds > 0f)
                Expect(Near(seal.AttackIntervalSeconds / free.AttackIntervalSeconds, 1.2f, 0.01f), $"{who}: attacks every {seal.AttackIntervalSeconds:0.00} s, the kit every {free.AttackIntervalSeconds:0.00} s; Hesitation adds 20%");

            // Damage taken, through the defense pool.
            float dealtSealed = seal.TakeHit(new HitInfo(40f, Vector2.Zero), seal.GlobalPosition + new Vector2(-50f, 0f));
            float dealtFree = free.TakeHit(new HitInfo(40f, Vector2.Zero), free.GlobalPosition + new Vector2(-50f, 0f));
            Expect(dealtFree > 0f && Near(dealtSealed / dealtFree, 1.15f, 0.01f), $"{who}: a 40-point hit dealt {dealtSealed:0.0} sealed and {dealtFree:0.0} free; Smoulder adds 15%");

            // Cooldowns: the same skill pressed on both bodies; where the kit starts one, the
            // sealed body's is a quarter longer.
            int measured = 0;
            var buttons = new (ControlAction Button, Func<CharacterController, (float Remaining, float Max)> Read)[]
            {
                (ControlAction.Skill1, c => c.ShiftCooldown), (ControlAction.Skill2, c => c.ESkillCooldown),
            };
            foreach ((ControlAction button, Func<CharacterController, (float Remaining, float Max)> read) in buttons)
            {
                sealPad.Tap(button);
                freePad.Tap(button);
                await Ticks(4);
                (float sealedLeft, float sealedMax) = read(seal);
                (float freeLeft, float freeMax) = read(free);
                if (freeLeft > 0.05f)
                {
                    // Both started on the same tick and have run as long since: each started at
                    // its own max, and the sealed max is a quarter longer.
                    measured++;
                    Expect(Near(sealedMax / Mathf.Max(0.001f, freeMax), 1.25f, 0.01f) && Near(sealedMax - sealedLeft, freeMax - freeLeft, 0.02f),
                        $"{who}: {button} left a {sealedLeft:0.00}/{sealedMax:0.00} s cooldown sealed and {freeLeft:0.00}/{freeMax:0.00} s free; Fog adds 25%");
                }
                await Ticks(60 * 3);
            }

            // Move speed: both walk right; the sealed body tops out 15% slower.
            sealPad.Hold(ControlAction.MoveRight);
            freePad.Hold(ControlAction.MoveRight);
            float sealTop = 0f, freeTop = 0f;
            for (int i = 0; i < 50; i++)
            {
                await Ticks(1);
                sealTop = Mathf.Max(sealTop, seal.Velocity.X);
                freeTop = Mathf.Max(freeTop, free.Velocity.X);
            }
            sealPad.ReleaseAll();
            freePad.ReleaseAll();
            Expect(freeTop > 0f && Near(sealTop / freeTop, 0.85f, 0.03f), $"{who}: walks at {M(sealTop)} m/s sealed, {M(freeTop)} m/s free; Burden takes 15%");
            lines.Add($"{id} ×{dealtSealed / Mathf.Max(0.001f, dealtFree):0.00} dmg, ×{sealTop / Mathf.Max(0.001f, freeTop):0.00} speed, {measured} cooldown(s)");
            kits++;
            await Strike(stage);
        }
        Expect(kits == Debug.ProtagonistRoster.Entries.Length, $"seals: {kits} of {Debug.ProtagonistRoster.Entries.Length} protagonists were checked");
        GD.Print($"  zone6    seals          every stack once, on every protagonist: {string.Join(" · ", lines)}");
    }

    // ---------------------------------------------------------------- 4d. Māra

    /// <summary>Māra's stand-in (BOSS PumpKing's kit under Māra's row) at the core's foe level,
    /// with two powers sealed in him: his row's HP and damage, less 12% and 10 points each.</summary>
    private async Task ProbeMara()
    {
        Node2D stage = BuildStage(0f, 4000f);
        BossDef def = BossTable.Mara;
        CharacterController boss = Spawn<CharacterController>(stage, def.ScenePath, new Vector2(2000f, FloorY - 40f));
        ((IBossFighter)boss).BeginFight(def, 8, new BossArena(new[] { boss.GlobalPosition }, 0));
        boss.GetNodeOrNull<BossBrain>("BossBrain")?.Sleep();

        var av = new Zone6State { VariantId = Zone6Table.AviciId };
        av.RecordWin("XX-5-1");
        av.RecordWin("XX-5-3");
        ArenaRules rules = ArenaRules.Build("XX", av, Zone6Role.Final);
        ArenaRulesApplier.ApplyToFinalBoss(boss, rules);
        await Ticks(2);

        FoeStats.LevelStats level = FoeStats.ForLevel(8);
        float hp = def.BaseHp * level.HpMult * 0.76f;
        float damage = def.DamageScale * level.AtkMult - 0.2f;
        Expect(Near(boss.MaxHP, hp, 0.5f) && Near(boss.CurrentHP, hp, 0.5f),
            $"māra: with Devour and Delusion sealed his stand-in has {boss.CurrentHP:0}/{boss.MaxHP:0} HP; his row gives {def.BaseHp:0} × {level.HpMult} × 0.76 = {hp:0}");
        Expect(Near(boss.DamageDealtScale, damage, 1e-3f), $"māra: he deals ×{boss.DamageDealtScale:0.00}; his row gives {def.DamageScale} × {level.AtkMult} − 0.20 = ×{damage:0.00}");
        GD.Print($"  zone6    māra           at foe level 8 with two powers sealed: {boss.MaxHP:0} HP (of {def.BaseHp * level.HpMult:0}), damage ×{boss.DamageDealtScale:0.00} (of ×{def.DamageScale * level.AtkMult:0.00}) · {rules.FinalBossLine}");
        await Strike(stage);
    }

    // ---------------------------------------------------------------- 5. the Styx Tide

    /// <summary>
    /// How hard the Styx Tide is to jump at a given gravity, measured: lanes of Pomegraknight
    /// (one jump), each on its own floor with its own wave, each jumping at its own moment —
    /// every second tick across the two seconds before the wave arrives. A lane clears when
    /// its wave passes without touching its fighter. The window is how long the run of
    /// clearing moments lasts. Zone6.gdd's targets: comfortable at ≤ ×0.6, only with good
    /// timing at ×1, nearly impossible from ×1.3.
    /// </summary>
    private async Task ProbeStyxTide()
    {
        var windows = new List<string>();
        var measured = new Dictionary<float, float>();
        foreach (float gravity in new[] { 0.5f, 1f, 1.5f })
        {
            (float window, int lanes, int hit, float damageShare, float carried) = await StyxLanes(gravity);
            measured[gravity] = window;
            windows.Add($"×{gravity:0.0}: {window:0.00} s ({lanes - hit}/{lanes} lanes clear)");
            Expect(hit > 0 && Near(damageShare, StyxTideHazard.DamageShareOfMaxHp, 0.02f),
                $"styx ×{gravity:0.0}: a fighter the wave caught lost {damageShare:P0} of its HP; a wave takes {StyxTideHazard.DamageShareOfMaxHp:P0}");
            Expect(hit == 0 || carried > Units.Px(3f), $"styx ×{gravity:0.0}: a fighter the wave caught was carried only {M(carried)} m");
        }
        Expect(measured[0.5f] >= 0.8f, $"styx: at gravity ×0.5 the window to clear the wave is {measured[0.5f]:0.00} s; it must be comfortable (≥ 0.8 s)");
        Expect(measured[1f] > 0.05f && measured[1f] <= 0.4f, $"styx: at gravity ×1 the window is {measured[1f]:0.00} s; it must be clearable, but only with good timing (0.05–0.4 s)");
        Expect(measured[1.5f] <= 0.001f, $"styx: at gravity ×1.5 the window is {measured[1.5f]:0.00} s; one jump must not clear it");
        GD.Print($"  zone6    styx tide      {StyxTideHazard.HeightM} m tall, {StyxTideHazard.CrestWidthM} m deep, {StyxTideHazard.SpeedMps} m/s · window for one jump at {string.Join(" · ", windows)}");
    }

    private async Task<(float Window, int Lanes, int Hit, float DamageShare, float Carried)> StyxLanes(float gravity)
    {
        const float LaneGap = 900f;
        const float StandX = 1000f;
        const int FirstJump = 30, LastJump = 136, Step = 2;
        int lanes = (LastJump - FirstJump) / Step + 1;
        var stage = new Node2D { Name = "StyxLanes" };
        AddChild(stage);
        var bodies = new CharacterController[lanes];
        var pads = new VirtualPad[lanes];
        var waves = new StyxTideHazard[lanes];
        var hp = new float[lanes];
        var rules = new ArenaRules { Zone6Gravity = gravity };
        for (int i = 0; i < lanes; i++)
        {
            float floor = FloorY + i * LaneGap;
            AddBox(stage, new Rect2(0f, floor, 2600f, 120f));
            bodies[i] = Fighter(stage, PlayerScene, new Vector2(StandX, floor - 32f), out pads[i]);
            ArenaRulesApplier.ApplyToFighter(bodies[i], rules);
        }
        await Ticks(30);
        for (int i = 0; i < lanes; i++) hp[i] = bodies[i].CurrentHP;

        int t0 = _tick;
        for (int i = 0; i < lanes; i++)
        {
            waves[i] = new StyxTideHazard { Name = "Wave" + i };
            waves[i].Configure(1f, FloorY + i * LaneGap, 0f, 2600f);
            stage.AddChild(waves[i]);
        }
        _onTick = tick =>
        {
            int t = tick - t0;
            for (int i = 0; i < lanes; i++)
                if (t == FirstJump + i * Step) pads[i].Tap(ControlAction.Jump);
        };
        var hitX = new float[lanes];
        bool[] noted = new bool[lanes];
        for (int t = 0; t < 330; t++)
        {
            await Ticks(1);
            for (int i = 0; i < lanes; i++)
                if (!noted[i] && GodotObject.IsInstanceValid(waves[i]) && waves[i].HasHit(bodies[i])) { noted[i] = true; hitX[i] = bodies[i].GlobalPosition.X; }
        }

        int clear = 0, hit = 0, longest = 0, run = 0;
        float damageShare = 0f, carried = 0f;
        for (int i = 0; i < lanes; i++)
        {
            bool touched = noted[i] || bodies[i].CurrentHP < hp[i] - 0.01f;
            if (touched)
            {
                hit++;
                run = 0;
                damageShare = Mathf.Max(damageShare, (hp[i] - bodies[i].CurrentHP) / bodies[i].MaxHP);
                carried = Mathf.Max(carried, bodies[i].GlobalPosition.X - StandX);
            }
            else
            {
                clear++;
                run++;
                longest = Mathf.Max(longest, run);
            }
        }
        _onTick = null;
        foreach (VirtualPad pad in pads) _pads.Remove(pad);
        stage.QueueFree();
        await Ticks(2);
        return (longest * Step * Tick, lanes, hit, damageShare, carried);
    }
}
