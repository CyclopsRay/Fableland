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
///    are zone-6 fights; the Styx Tide's schedule; the Wheel of Becoming's clock — its order
///    from Hell, sealed realms skipped, a stack's second, the cycle table of Mara.gdd §4.3, the
///    Last Turn (<see cref="WheelOfBecoming.SelfTest"/>).
/// 2. The variant — the same for a seed every time, both variants dealt, drawn from a stream
///    of its own: the run's root stream and the map it generates are untouched.
/// 3. Saves — seals and the ladder survive a save and a load, and nothing a run does clears a
///    seal; a save from before v0.12.0 (no zone 6 in it) loads into an empty zone 6.
/// 4. Bodies, on plain colliders — the Wulin law on a real fighter (one more jump, each 75% as
///    high); gravity on a fighter, a foe and a projectile; Avīci's seals on every protagonist
///    of the roster (reload, damage taken, cooldowns, move speed, attack interval); the wheel
///    on a real boss — each realm's modifiers, ×1.5 only in the Interval, the lock holding
///    outside it and opening inside, the Last Turn, Liberation, Devour's heal and swallow, a
///    wheel with all five sealed standing still; the Tempter's Offering.
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
            await ProbeWheel();
            await ProbeOffering();
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
        Absorb("WheelTable", WheelTable.Validate());
        Absorb("WheelOfBecoming", WheelOfBecoming.SelfTest());
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
        Expect(string.Join(",", mara.MaraSealedPoisons) == "anger,doubt" && mara.SealedBossAbilities.Count == 2
               && mara.FinalBossLine.Contains("Wrath") && mara.FinalBossLine.Contains("Doubt"),
            $"rules: Wrath and Doubt sealed in Māra must reach the core as anger,doubt ('{string.Join(",", mara.MaraSealedPoisons)}', '{mara.FinalBossLine}')");
        Expect(mara.SealStacksOf("greed") == 1 && mara.SealStacksOf("pride") == 2 && mara.SealStacksOf("doubt") == 0,
            "rules: the core must carry the party's stacks of each poison, for the wheel's rests");
        // The Offering: one more stack for this fight only — a new record, the handshake's untouched.
        ArenaRules offered = mara.WithFightSeal("greed");
        ArenaRules capped = mara.WithFightSeal("pride").WithFightSeal("pride").WithFightSeal("pride");
        Expect(offered.SealStacksOf("greed") == 2 && Near(offered.SealReloadScale, 1.6f, 1e-5f) && mara.SealStacksOf("greed") == 1
               && Near(mara.SealReloadScale, 1.3f, 1e-5f) && offered.SealLine.Contains("Hunger ×2") && offered.MaraSealedPoisons.Count == 2,
            $"rules: an Offering of Greed must make Hunger ×2 (reload ×{offered.SealReloadScale}) for the fight and leave the run's record at ×1");
        Expect(capped.SealStacksOf("pride") == Zone6Table.SealStackCap, $"rules: an Offering must not stack a poison past its cap ({capped.SealStacksOf("pride")})");
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
        CheckWheelCycles();
    }

    /// <summary>Mara.gdd §4.3: what each gate won buys — the cycle and the Interval's share of
    /// it, above and below half HP, with nothing in the player.</summary>
    private void CheckWheelCycles()
    {
        string[] order = { "pride", "anger", "greed", "doubt", "delusion" };
        var rows = new List<string>();
        for (int k = 0; k <= 5; k++)
        {
            var wheel = new WheelOfBecoming(order[..k], null);
            float above = wheel.CycleSeconds(1f), below = wheel.CycleSeconds(0.4f);
            if (k < 5)
            {
                Expect(Near(above, 46f - 8f * k, 1e-4f) && Near(below, 36f - 6f * k, 1e-4f),
                    $"wheel: with {k} sealed a cycle is {above} s / {below} s; Mara.gdd §4.3 has {46 - 8 * k} s / {36 - 6 * k} s");
                rows.Add($"{k}: {above:0} s ({WheelTable.IntervalSeconds / above:P0}) / {below:0} s ({WheelTable.IntervalSeconds / below:P0})");
            }
            else
            {
                Expect(wheel.Still && wheel.Current == WheelRealm.Humans && above == 0f, "wheel: with all five sealed the wheel must stand on the human realm");
                rows.Add("5: still — the Interval for good");
            }
        }
        var stacked = new WheelOfBecoming(null, id => id == "doubt" ? 3 : 0);
        Expect(Near(stacked.RestSeconds(WheelRealm.Hell, 1f), 11f, 1e-4f) && Near(stacked.RestSeconds(WheelRealm.Hell, 0.4f), 9f, 1e-4f)
               && Near(stacked.RestSeconds(WheelRealm.Humans, 0.4f), WheelTable.IntervalSeconds, 1e-4f),
            "wheel: three stacks of Doubt must hold Hell 11 s (9 s below half HP); the Interval never changes");
        GD.Print($"  zone6    the wheel      sealed → cycle above / below half HP (the Interval's share): {string.Join(" · ", rows)}");
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

    // ---------------------------------------------------------------- 4d. the wheel

    /// <summary>A wheel on a real body: Māra's row on the stand-in kit (BOSS PumpKing's scene),
    /// which knows nothing of the wheel — so everything measured here is the wheel's own work,
    /// whatever kit stands in. Its clock is driven by hand, a whole number of ticks a step.</summary>
    private (CharacterController Boss, WheelOfBecomingField Field) StageWheel(Node2D stage, IEnumerable<string> sealedPoisons,
        Func<string, int> stacks = null, Func<CharacterController> player = null)
    {
        BossDef def = BossTable.Mara;
        CharacterController boss = Spawn<CharacterController>(stage, BossTable.PumpKing.ScenePath, new Vector2(2000f, FloorY - 32f));
        ((IBossFighter)boss).BeginFight(def, 8, new BossArena(new[] { boss.GlobalPosition }, 0));
        boss.GetNodeOrNull<BossBrain>("BossBrain")?.Sleep();
        var field = new WheelOfBecomingField { Name = "Wheel" };
        stage.AddChild(field);
        field.Begin(boss, def, sealedPoisons, stacks, player, 0f, 4000f, FloorY, new Vector2(2000f, FloorY));
        return (boss, field);
    }

    private static void Run(WheelOfBecomingField field, float seconds)
    {
        for (float t = 0f; t < seconds; t += Tick) field.Advance(Tick);
    }

    /// <summary>A hit of <paramref name="damage"/> from behind his back, after his stun has run out.</summary>
    private static float Struck(CharacterController boss, float damage, bool ranged = false, float fromX = -60f, float fromY = 0f)
    {
        HitInfo hit = new HitInfo(damage, Vector2.Zero, 0f);
        if (ranged) hit = hit.FromRanged("check", "shot");
        return boss.TakeHit(hit, boss.GlobalPosition + new Vector2(fromX, fromY));
    }

    private async Task ProbeWheel()
    {
        Node2D stage = BuildStage(0f, 4000f);
        (CharacterController boss, WheelOfBecomingField field) = StageWheel(stage, Array.Empty<string>());
        await Ticks(2);
        float max = boss.MaxHP;
        var landed = new List<WheelRealm>();
        field.Wheel.Landed += realm => landed.Add(realm);
        FoeStats.LevelStats level = FoeStats.ForLevel(8);
        Expect(Near(max, BossTable.Mara.BaseHp * level.HpMult, 0.5f), $"wheel: Māra has {max:0} HP; his row gives {BossTable.Mara.BaseHp} × {level.HpMult} — the seals no longer cut it");

        // Hell, as the fight opens: the lock holds, damage is plain.
        Expect(field.Realm == WheelRealm.Hell && Near(boss.HpFloor, max * WheelTable.LockShare, 0.01f), $"wheel: the fight opens on {field.Realm} with a floor of {boss.HpFloor:0}; Hell, and {max * WheelTable.LockShare:0}");
        float plain = Struck(boss, 100f);
        // Each step runs a realm's whole rest and a hair more: it lands just after the turn.
        Run(field, 16.05f);                                                // the animals, then the humans
        float interval = Struck(boss, 100f);
        bool openedLock = boss.HpFloor == 0f;
        Run(field, 6.05f);                                                 // the gods
        float throne = Struck(boss, 100f);
        Run(field, 8.05f);                                                 // the asuras
        float wrath = boss.CooldownScale;
        Expect(string.Join(" ", landed) == "Animals Humans Gods Asuras", $"wheel: from Hell it turned {string.Join(" ", landed)}");
        Expect(Near(plain, 100f, 0.01f) && Near(interval, 150f, 0.01f) && Near(throne, 30f, 0.01f),
            $"wheel: a 100-point hit dealt {plain:0.#} in Hell, {interval:0.#} in the Interval, {throne:0.#} on the throne; ×1, ×1.5, ×0.3");
        Expect(openedLock, "wheel: the lock must open in the Interval");
        Expect(Near(wrath, WheelTable.WrathScale, 1e-4f), $"wheel: in the asuras' realm his cooldowns run ×{wrath}; Wrath is ×{WheelTable.WrathScale}");

        // The hungry ghosts: Devour heals him for what he lands, and his front swallows shots.
        Run(field, 8.05f);
        Expect(field.Realm == WheelRealm.HungryGhosts && Near(boss.CooldownScale, 1f, 1e-4f), $"wheel: Wrath must end with its realm ({field.Realm}, cooldowns ×{boss.CooldownScale})");
        Struck(boss, max * 0.3f);
        float before = boss.CurrentHP;
        boss.ReportDamageDealt(12f);
        float healed = boss.CurrentHP - before;
        before = boss.CurrentHP;
        float front = Struck(boss, 50f, ranged: true, fromX: boss.FacingDirection.X * 50f);
        float swallowedHeal = boss.CurrentHP - before;
        float back = Struck(boss, 50f, ranged: true, fromX: -boss.FacingDirection.X * 50f);
        float crown = Struck(boss, 50f, ranged: true, fromX: boss.FacingDirection.X * 50f, fromY: -Units.Px(3f));
        Expect(Near(healed, max * WheelTable.DevourHealPerHit, 0.05f) && field.DevourHeals == 1,
            $"wheel: Devour healed {healed:0.#} for a hit he landed; it is {WheelTable.DevourHealPerHit:P1} of {max:0}");
        Expect(front == 0f && Near(swallowedHeal, max * WheelTable.SwallowHealShare, 0.05f) && back > 0f && crown > 0f && field.Swallowed == 1,
            $"wheel: a shot at his front must be swallowed and heal {WheelTable.SwallowHealShare:P0} (dealt {front}, healed {swallowedHeal:0.#}); from behind ({back:0}) or onto his crown ({crown:0}) it lands");
        Run(field, 8.05f);                                                 // Hell again
        boss.ReportDamageDealt(12f);
        Expect(field.Realm == WheelRealm.Hell && field.DevourHeals == 1 && Struck(boss, 50f, ranged: true, fromX: boss.FacingDirection.X * 50f) > 0f,
            "wheel: Devour must end with the hungry ghosts' realm");

        // The lock: a blow far past his HP stops at the floor; the Last Turn passes every realm.
        Struck(boss, max * 3f);
        bool locked = !boss.IsDefeated && Near(boss.CurrentHP, max * WheelTable.LockShare, 0.5f);
        var passed = new List<WheelRealm>();
        field.Wheel.Passed += realm => passed.Add(realm);
        field.Advance(Tick);
        bool spinning = field.Wheel.InLastTurn;
        Struck(boss, max);
        bool heldInSpin = !boss.IsDefeated;
        float spun = 0f;
        for (; spun < 10f && field.Wheel.InLastTurn; spun += Tick) field.Advance(Tick);
        Expect(locked && spinning && heldInSpin, $"wheel: outside the Interval the lock must hold at {max * WheelTable.LockShare:0} HP (HP {boss.CurrentHP:0}) and start the Last Turn ({spinning})");
        Expect(string.Join(" ", passed) == "Animals Gods Asuras HungryGhosts Hell" && field.Realm == WheelRealm.Humans && field.Wheel.InInterval
               && Near(spun, 5 * WheelTable.LastTurnStepSeconds, 0.05f),
            $"wheel: the Last Turn from Hell must pass all five realms in {5 * WheelTable.LastTurnStepSeconds:0.0} s and open the Interval ({string.Join(" ", passed)} in {spun:0.00} s → {field.Realm})");
        Struck(boss, max);
        Expect(boss.IsDefeated && field.Liberated && field.Wheel.Stopped && field.Realm == WheelRealm.Humans,
            "wheel: struck in the Interval he must fall, and the wheel stop on the human realm");
        GD.Print($"  zone6    the wheel      on the stand-in kit: a 100-point hit deals {plain:0} in Hell · {interval:0} in the Interval · {throne:0} on the throne · Wrath ×{wrath:0.0} · Devour +{healed:0} a hit, a swallowed shot +{swallowedHeal:0} · the lock at {max * WheelTable.LockShare:0} HP · the Last Turn {spun:0.0} s · liberated");
        await Strike(stage);

        // All five sealed: he stands in the Interval the whole fight; the lock never holds.
        stage = BuildStage(0f, 4000f);
        (boss, field) = StageWheel(stage, new[] { "greed", "anger", "delusion", "pride", "doubt" });
        await Ticks(2);
        int turns = 0;
        field.Wheel.Landed += _ => turns++;
        Run(field, 40f);
        float open = Struck(boss, 100f);
        Expect(field.Realm == WheelRealm.Humans && turns == 0 && boss.HpFloor == 0f && Near(open, 150f, 0.01f),
            $"wheel: with all five sealed he must stand in the Interval ({field.Realm}, {turns} turn(s), floor {boss.HpFloor:0}, a 100-point hit dealt {open:0})");
        await Strike(stage);
    }

    /// <summary>The Tempter's Offering: when the fighter in play first falls below 30% HP a bowl
    /// is set on the throne; taking it heals to full and names the poison that enters.</summary>
    private async Task ProbeOffering()
    {
        Node2D stage = BuildStage(0f, 4000f);
        CharacterController player = Fighter(stage, PlayerScene, new Vector2(1200f, FloorY - 32f), out _);
        var stacks = new Dictionary<string, int> { ["anger"] = 1, ["pride"] = 3 };
        (CharacterController boss, WheelOfBecomingField field) = StageWheel(stage, Array.Empty<string>(), id => stacks.GetValueOrDefault(id), () => player);
        string taken = null;
        field.OfferingTaken += poison => taken = poison;
        await Ticks(2);
        Run(field, 1f);
        Expect(field.Bowl == null && !field.Offered, "offering: no bowl while the fighter is above 30% HP");
        player.TakeHit(new HitInfo(player.MaxHP * 0.75f, Vector2.Zero, 0f), player.GlobalPosition + new Vector2(50f, 0f));
        Run(field, Tick);
        OfferingBowl bowl = field.Bowl;
        Expect(bowl != null && bowl.PoisonId == "anger" && bowl.Price.Contains("Anger"),
            $"offering: below 30% HP a bowl must offer the poison carried most under its cap — Anger, not capped Pride ({bowl?.PoisonId}, '{bowl?.Price}')");
        bowl?.TakeBy(player);
        Expect(taken == "anger" && Near(player.CurrentHP, player.MaxHP, 0.01f), $"offering: taking the bowl must heal to full ({player.CurrentHP:0}/{player.MaxHP:0}) and put Anger in ({taken})");
        player.TakeHit(new HitInfo(player.MaxHP * 0.8f, Vector2.Zero, 0f), player.GlobalPosition + new Vector2(50f, 0f));
        Run(field, 1f);
        Expect(field.Bowl == null, "offering: the bowl comes once a fight");
        string offeredNone = WheelOfBecomingField.OfferingPoisonFor(_ => 0);
        string offeredAllCapped = WheelOfBecomingField.OfferingPoisonFor(_ => Zone6Table.SealStackCap);
        Expect(offeredNone == WheelTable.OfferingDefaultPoison && offeredAllCapped == null,
            $"offering: a fighter who carries nothing must be offered Greed ({offeredNone}); one capped in all five, nothing ({offeredAllCapped})");
        GD.Print($"  zone6    the offering   below 30% HP the bowl offers Anger (Pride capped) · taken: healed to full, Anger enters · once a fight");
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
