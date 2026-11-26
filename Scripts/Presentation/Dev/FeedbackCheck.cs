namespace Fableland.Feedback.Dev;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using Fableland.Data;
using Fableland.Missions;
using Fableland.Run;

/// <summary>
/// Headless check for hit feedback (40-QA.md §2, Feedback.gdd):
///
///     Godot --headless --path . res://Scenes/Feedback/FeedbackCheck.tscn      # exit 0 = clean
///
/// 1. Pure logic — the hit-stop table and clock, the feel table, the flash limiter, the
///    owner → class → "any" lookup, and every body's content id.
/// 2. Hits, on real bodies on a stage of plain colliders: one HitCue per hit that landed,
///    with the right attacker, attack and weight; a refused hit raises none; a kill says so.
/// 3. Hit-stop: a stopped body does not move and resumes with the velocity it had, on the
///    ground and in the air; its clocks keep running; stops take the max and re-arm; one
///    swing through three targets holds its attacker for one stop; a ranged hit never holds
///    its owner; the setting switches it off; Jump pressed during a stop is not lost; and
///    the kits that move themselves (Sifu Pangda's dive, PumpKing's Soul Form) hold too.
/// 4. Body cues — spawn, jump, land, heal, ult.ready, death, respawn, switch.in / out, the
///    five status loops (start AND stop, on death and on leaving the tree too), the merged
///    hurt.tick, hazards, soft volumes, a foe's aggro / evolve / telegraph.
/// 5. Pomegraknight, the reference kit: every cue of her kit, with its variants.
/// 6. The feel director: shake stays under its tier's cap however many hits coincide, the
///    kick, the roll, the zoom punch, the comfort settings, the flash (applied, restored,
///    and never touching Modulate / Visible), light or dark by the arena, the flash limits.
/// 7. The bus: a listener that throws does not break a hit or silence the others, and
///    raising cues allocates nothing.
/// 8. Flow, through the real Arena scene: arena.enter, the countdown, mission.start,
///    boss.intro, player.down, boss.defeated, mission.success, run.over.
///
/// It drives a fighter through a virtual pad it steps itself (so a tap is exact in both
/// loops), reads positions, and compares against what the tables predict — retuning a
/// number does not break it, breaking a mechanism does. Each failure prints on its own
/// `FAIL` line; measured numbers print as `feel` lines.
/// </summary>
public partial class FeedbackCheck : Node
{
    private readonly List<string> _problems = new();
    private int _checks;

    private const int SpeedUp = 8;
    private const float Tick = 1f / 60f;
    private const float FloorY = 600f;
    private const string PomeScene = "res://Scenes/Pomegraknight.tscn";
    private const string DummyScene = "res://Scenes/Missions/DestroyObjective.tscn";

    // The bus, as this check hears it.
    private readonly List<Cue> _cues = new();
    private readonly List<Cue> _starts = new();
    private readonly List<Cue> _stops = new();
    private readonly List<HitCue> _hits = new();
    private bool _recording;

    // The pad the fighter under test wears. Stepped here, before any body runs.
    private readonly VirtualPad _pad = new();

    public override void _Process(double delta) => _pad.StepProcess();
    public override void _PhysicsProcess(double delta) => _pad.StepPhysics();

    private void OnCue(Cue cue) { if (_recording) _cues.Add(cue); }
    private void OnStart(Cue cue) { if (_recording) _starts.Add(cue); }
    private void OnStop(Cue cue) { if (_recording) _stops.Add(cue); }
    private void OnHit(HitCue hit) { if (_recording) _hits.Add(hit); }

    public override async void _Ready()
    {
        ProcessPriority = -100;
        ProcessPhysicsPriority = -100;
        int ticksPerSecond = Engine.PhysicsTicksPerSecond;
        double timeScale = Engine.TimeScale;
        CueBus.Raised += OnCue;
        CueBus.LoopStarted += OnStart;
        CueBus.LoopStopped += OnStop;
        CueBus.HitLanded += OnHit;
        _recording = true;
        try
        {
            RunLogicSelfTests();
            CheckBodyIds();

            Engine.PhysicsTicksPerSecond = ticksPerSecond * SpeedUp;
            Engine.TimeScale = SpeedUp;
            await Ticks(1);          // every stage starts at the same point of a tick
            await ProbeHits();
            await ProbeHitStop();
            await ProbeKitPhysics();
            await ProbeBodyCues();
            await ProbeStatusAndHurt();
            await ProbeWorld();
            await ProbeFoes();
            await ProbePomegraknight();
            await ProbeFeel();
            await ProbeTheBus();
            await ProbeFlow();
        }
        catch (Exception e)
        {
            _problems.Add("feedback check threw: " + e);
        }
        finally
        {
            Engine.PhysicsTicksPerSecond = ticksPerSecond;
            Engine.TimeScale = timeScale;
            FeedbackSettings.ResetToDefaults();
            CueBus.Enabled = true;
            CueBus.Raised -= OnCue;
            CueBus.LoopStarted -= OnStart;
            CueBus.LoopStopped -= OnStop;
            CueBus.HitLanded -= OnHit;
            GetTree().Paused = false;
        }

        foreach (string problem in _problems) GD.Print("FAIL  " + problem);
        GD.Print(_problems.Count == 0
            ? $"FeedbackCheck: OK ({_checks} checks)"
            : $"FeedbackCheck: {_problems.Count} problem(s) in {_checks} checks");
        GetTree().Quit(_problems.Count == 0 ? 0 : 1);
    }

    // ---------------------------------------------------------------- helpers

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

    /// <summary>Wait for something to happen, with a budget in ticks. Never a fixed count.</summary>
    private async Task<bool> Until(Func<bool> done, int budget)
    {
        for (int i = 0; i < budget && !done(); i++) await Ticks(1);
        return done();
    }

    private void ClearLog()
    {
        _cues.Clear();
        _starts.Clear();
        _stops.Clear();
        _hits.Clear();
    }

    private static int Count(List<Cue> log, string owner, string action, string tag = null)
    {
        int n = 0;
        foreach (Cue cue in log)
            if (cue.Owner == owner && cue.Action == action && (tag == null || cue.Tag == tag)) n++;
        return n;
    }

    private static bool Find(List<Cue> log, string owner, string action, out Cue found, int variant = -1)
    {
        foreach (Cue cue in log)
        {
            if (cue.Owner != owner || cue.Action != action || (variant >= 0 && cue.Variant != variant)) continue;
            found = cue;
            return true;
        }
        found = default;
        return false;
    }

    private static string M(float px) => (px / Units.PixelsPerMeter).ToString("0.0");
    private static string Ms(float seconds) => (seconds * 1000f).ToString("0");

    /// <summary>How many physics ticks a stop of this length holds a body — by the clock's
    /// own arithmetic, so the expectation follows the table.</summary>
    private static int TicksFor(float seconds)
    {
        var clock = new HitStopClock();
        clock.Request(seconds);
        int held = 0;
        while (clock.Tick(Tick)) held++;
        return held;
    }

    /// <summary>Count the ticks a body stays held from now.</summary>
    private async Task<int> HeldTicks(IFeedbackBody body, int budget = 60)
    {
        int held = 0;
        while (body.InHitStop && held < budget)
        {
            await Ticks(1);
            held++;
        }
        return held;
    }

    private Node2D BuildStage(float x0, float x1)
    {
        var stage = new Node2D { Name = "Stage" };
        AddChild(stage);
        AddBox(stage, new Rect2(x0, FloorY, x1 - x0, 200f));
        AddBox(stage, new Rect2(x0 - 40f, FloorY - 3000f, 40f, 3200f));     // walls: nothing leaves the stage
        AddBox(stage, new Rect2(x1, FloorY - 3000f, 40f, 3200f));
        return stage;
    }

    private static void AddBox(Node2D stage, Rect2 rect)
    {
        var body = new StaticBody2D { Position = rect.GetCenter(), CollisionLayer = 0, CollisionMask = 0 };
        body.SetCollisionLayerValue(Units.LayerGround, true);
        body.AddChild(new CollisionShape2D { Shape = new RectangleShape2D { Size = rect.Size } });
        stage.AddChild(body);
    }

    private async Task Strike(Node2D stage)
    {
        _pad.ReleaseAll();
        stage.QueueFree();
        await Ticks(3);
    }

    private static T Spawn<T>(Node parent, string scenePath, Vector2 at) where T : Node2D
    {
        var node = ResourceLoader.Load<PackedScene>(scenePath).Instantiate<T>();
        parent.AddChild(node);
        node.GlobalPosition = at;
        if (node is CharacterController body) body.SetSpawnPoint(at);
        return node;
    }

    /// <summary>A fighter on the check's pad, standing on the stage floor.</summary>
    private CharacterController SpawnFighter(Node2D stage, string scenePath, float x, float above = 0f)
    {
        var body = Spawn<CharacterController>(stage, scenePath, new Vector2(x, FloorY - 32f - above));
        body.UseControls(_pad);
        body.ShowDebugRanges = false;
        return body;
    }

    /// <summary>A target that takes hits and does nothing else — so the probe owns every hit.</summary>
    private static DestroyObjective SpawnDummy(Node2D stage, float x, float hp = 100000f)
    {
        var dummy = Spawn<DestroyObjective>(stage, DummyScene, new Vector2(x, FloorY - 22f));
        dummy.OverrideMaxHp(hp);
        return dummy;
    }

    // ---------------------------------------------------------------- 1. logic and content

    private void RunLogicSelfTests()
    {
        Absorb("FeedbackTable", FeedbackTable.SelfTest());
        Absorb("HitStopClock", HitStopClock.SelfTest());
        Absorb("FeelTable", FeelTable.SelfTest());
        Absorb("FlashLimiter", FlashLimiter.SelfTest());

        // The lookup every director uses: the owner's row, else its class's, else "any".
        var table = new CueTable<string>()
            .Add("crab", "death", "crab's own")
            .Add(CueOwners.Foe, "death", "any foe's")
            .Add(CueOwners.Any, "death", "anyone's")
            .Add(CueOwners.Any, "land", "anyone's landing");
        Expect(table.Resolve("crab", CueOwners.Foe, "death") == "crab's own", "lookup: an owner's own row must win");
        Expect(table.Resolve("seagull", CueOwners.Foe, "death") == "any foe's", "lookup: with no row of its own an owner falls back to its class");
        Expect(table.Resolve("pomegraknight", CueOwners.Fighter, "death") == "anyone's", "lookup: with no class row it falls back to 'any'");
        Expect(table.Resolve("pomegraknight", null, "land") == "anyone's landing", "lookup: a cue with no class goes straight to 'any'");
        Expect(table.Resolve("crab", CueOwners.Foe, "evolve") == null, "lookup: an action nobody has a row for resolves to nothing");

        Expect(FeedbackIds.FromTypeName("SifuPangda") == "sifu_pangda" && FeedbackIds.FromTypeName("CrabFoe") == "crab_foe",
            "ids: a class name must default to snake_case");

        var plain = new HitInfo(10f, new Vector2(5f, 0f), 0.2f);
        Expect(plain.Attacker == null && plain.CueOwner == null && plain.Weight == HitWeight.Auto && !plain.Ranged,
            "HitInfo: a hit that names nothing must carry no feedback metadata");
        HitInfo ranged = plain.FromRanged("pomegraknight", "s2.seed", null, 2, HitWeight.Light);
        Expect(ranged.Ranged && ranged.CueOwner == "pomegraknight" && ranged.CueAction == "s2.seed" && ranged.CueVariant == 2
            && ranged.Weight == HitWeight.Light && Mathf.IsEqualApprox(ranged.Damage, 10f) && Mathf.IsEqualApprox(ranged.Stun, 0.2f),
            "HitInfo: FromRanged must name the hit and leave damage, knockback and stun alone");
    }

    /// <summary>Every body states the content id its cues are raised under.</summary>
    private void CheckBodyIds()
    {
        var expected = new (string Scene, string Id, string Class)[]
        {
            ("res://Scenes/Pomegraknight.tscn", "pomegraknight", CueOwners.Fighter),
            ("res://Scenes/Cleopastar.tscn", "cleopastar", CueOwners.Fighter),
            ("res://Scenes/Pixolotl.tscn", "pixolotl", CueOwners.Fighter),
            ("res://Scenes/PumpKing.tscn", "pumpking", CueOwners.Fighter),
            ("res://Scenes/SifuPangda.tscn", "sifu_pangda", CueOwners.Fighter),
            ("res://Scenes/Bosses/BossPumpKing.tscn", "pumpking", CueOwners.Boss),
            ("res://Scenes/Foes/CrabFoe.tscn", "crab", CueOwners.Foe),
            ("res://Scenes/Foes/SeagullFoe.tscn", "seagull", CueOwners.Foe),
            ("res://Scenes/Missions/BossCrab.tscn", "boss_crab", CueOwners.Foe),
            (DummyScene, "destroy_objective", CueOwners.Objective),
        };
        foreach ((string scenePath, string id, string cls) in expected)
        {
            var scene = ResourceLoader.Load<PackedScene>(scenePath);
            Expect(scene != null, $"ids: scene '{scenePath}' does not load");
            if (scene == null) continue;
            Node node = scene.Instantiate();
            var body = node as IFeedbackBody;
            Expect(body != null && body.FeedbackId == id && body.FeedbackClass == cls,
                $"ids: '{scenePath}' must be {id} / {cls}, not {body?.FeedbackId ?? "no feedback body"} / {body?.FeedbackClass}");
            node.Free();
        }
    }

    // ---------------------------------------------------------------- 2. hits

    /// <summary>One swing through three targets: three HitCues (one each), named for the
    /// attack that threw them; the attacker held for one stop; each victim held for its own.</summary>
    private async Task ProbeHits()
    {
        Node2D stage = BuildStage(0f, 4000f);
        var player = (Pomegraknight)SpawnFighter(stage, PomeScene, 900f);
        var dummies = new[] { SpawnDummy(stage, 960f), SpawnDummy(stage, 990f), SpawnDummy(stage, 1015f) };
        await Ticks(30);

        ClearLog();
        _pad.Tap(ControlAction.Attack);
        Expect(await Until(() => _hits.Count > 0, 30), "hits: a basic attack through three targets landed nothing");
        float lightStop = FeedbackTable.VictimStop(HitWeight.Light, 15f, false, 1f);
        float attackerStop = FeedbackTable.AttackerStop(HitWeight.Light, 15f, false, 1f);

        Expect(_hits.Count == 3, $"hits: one swing through three targets raised {_hits.Count} HitCues, expected 3 — one per hit");
        var seen = new HashSet<Node2D>();
        foreach (HitCue hit in _hits)
        {
            seen.Add(hit.Victim);
            Expect(ReferenceEquals(hit.Attacker, player), "hits: a melee hit must name the body that swung");
            Expect(hit.AttackOwner == "pomegraknight" && hit.AttackAction == "ba" && hit.AttackVariant == 1 && hit.AttackClass == CueOwners.Fighter,
                $"hits: Pome's first slash must be pomegraknight/ba#1 (fighter), got {hit.AttackOwner}/{hit.AttackAction}#{hit.AttackVariant} ({hit.AttackClass})");
            Expect(hit.Weight == FeedbackTable.WeightFor(15f) && hit.Weight == HitWeight.Light, $"hits: a 15-damage slash is {hit.Weight}, expected Light");
            Expect(Mathf.IsEqualApprox(hit.Damage, 15f), $"hits: the cue's damage is {hit.Damage}, the slash deals 15");
            Expect(!hit.Ranged && !hit.Killed && !hit.VictimIsPlayer, "hits: a slash on a living target is not ranged, not a kill, not on the player");
            Expect(hit.VictimOwner == "destroy_objective" && hit.VictimClass == CueOwners.Objective, "hits: the cue must say who was hit");
            Expect(hit.Direction.X > 0.9f, $"hits: struck from its left, the blow travels right — direction {hit.Direction}");
            Expect(Mathf.Abs(hit.VictimStop - lightStop) < 0.001f, $"hits: the cue reports a {Ms(hit.VictimStop)} ms victim stop, the table says {Ms(lightStop)}");
        }
        Expect(seen.Count == 3, "hits: each target must get its own HitCue");
        Expect(Count(_cues, "pomegraknight", "ba.swing") == 1, "hits: one swing raises one ba.swing, however many it hits");

        // The attacker is held once, for one stop — not once per victim.
        bool victimsHeld = dummies[0].InHitStop && dummies[1].InHitStop && dummies[2].InHitStop;
        Expect(player.InHitStop, "hit-stop: landing a blow must hold the attacker");
        Expect(victimsHeld, "hit-stop: every body the swing hit must be held");
        int victimTicks = TicksFor(lightStop);
        int attackerTicks = await HeldTicks(player);
        Expect(attackerTicks == TicksFor(attackerStop),
            $"hit-stop: a swing through three targets held its attacker {attackerTicks} ticks; one stop is {TicksFor(attackerStop)}, three would be {TicksFor(attackerStop * 3f)}");
        int stillHeld = await HeldTicks(dummies[0]);
        Expect(attackerTicks + stillHeld == victimTicks, $"hit-stop: a Light hit held its victim {attackerTicks + stillHeld} ticks, the table says {victimTicks}");
        GD.Print($"  feel     a slash        15 damage is {HitWeight.Light}: the victim holds {Ms(lightStop)} ms ({victimTicks} ticks), the attacker {Ms(attackerStop)} ms ({attackerTicks}) — once for the swing, however many it hit");

        // A hit that is refused raises nothing.
        await Ticks(20);
        ClearLog();
        dummies[0].Invincible = true;
        float refused = dummies[0].TakeHit(new HitInfo(20f).From(player, "probe"), player.GlobalPosition);
        dummies[0].Invincible = false;
        Expect(refused == 0f && _hits.Count == 0, "hits: a hit an invincible body refuses must raise no HitCue");

        // A ranged hit: the victim is held, the owner never is.
        dummies[1].TakeHit(new HitInfo(15f).FromRanged(player, "probe.ranged", 4), player.GlobalPosition);
        Expect(_hits.Count == 1 && _hits[0].Ranged && ReferenceEquals(_hits[0].Attacker, player) && _hits[0].AttackVariant == 4,
            "hits: a ranged hit must say it is ranged and still name its owner");
        Expect(dummies[1].InHitStop && !player.InHitStop, "hit-stop: a ranged hit holds its victim and never its owner");
        // …and one with no body at hand falls back to the projectile class.
        dummies[2].TakeHit(new HitInfo(15f).FromRanged("pomegraknight", "s2.seed"), player.GlobalPosition);
        Expect(_hits.Count == 2 && _hits[1].Attacker == null && _hits[1].AttackClass == CueOwners.Projectile && _hits[1].AttackOwner == "pomegraknight",
            "hits: a projectile whose owner is gone must keep the owner's id and fall back to class 'projectile'");

        // A named weight is kept, whatever the damage.
        await Ticks(20);
        ClearLog();
        dummies[0].TakeHit(new HitInfo(200f).FromRanged(player, "probe.tick", 0, HitWeight.Light), player.GlobalPosition);
        Expect(_hits.Count == 1 && _hits[0].Weight == HitWeight.Light, "hits: a hit that names its weight must keep it");
        Expect(Mathf.Abs(_hits[0].VictimStop - FeedbackTable.VictimStop(HitWeight.Light, 200f, false, 1f)) < 0.001f
            && _hits[0].VictimStop < FeedbackTable.VictimStop(FeedbackTable.WeightFor(200f), 200f, false, 1f),
            "hit-stop: a hit named Light must be held to the Light band");

        // The kill: the cue says so, the victim is not held (it is gone), the attacker feels it.
        await Ticks(30);
        foreach (DestroyObjective dummy in dummies) dummy.QueueFree();
        DestroyObjective weak = SpawnDummy(stage, 960f, 10f);
        await Ticks(20);
        ClearLog();
        _pad.Tap(ControlAction.Attack);
        Expect(await Until(() => _hits.Count > 0, 60), "hits: the killing swing never landed");
        if (_hits.Count > 0)
        {
            Expect(_hits.Count == 1 && _hits[0].Killed && _hits[0].VictimStop == 0f, "hits: the hit that kills must say Killed and hold no victim");
            Expect(Count(_cues, "destroy_objective", "death") == 1, "cues: a body that dies raises one death");
            int killTicks = await HeldTicks(player);
            float killStop = FeedbackTable.AttackerStop(_hits[0].Weight, _hits[0].Damage, true, 1f);
            Expect(killTicks == TicksFor(killStop) && killStop >= FeedbackTable.KillAttackerMinSeconds - 0.0001f,
                $"hit-stop: the killing blow held its attacker {killTicks} ticks, the table says {TicksFor(killStop)} ({Ms(killStop)} ms)");
        }
        await Ticks(4);
        Expect(!GodotObject.IsInstanceValid(weak), "hits: a foe that dies still frees itself");

        // The player being hit: felt by what it costs, held at least 50 ms.
        DestroyObjective striker = SpawnDummy(stage, 860f);
        await Ticks(20);
        ClearLog();
        float cost = player.TakeHit(new HitInfo(18f, new Vector2(200f, -80f), 0.25f).From(striker, "contact"), striker.GlobalPosition);
        float score = FeedbackTable.ImpactScore(cost, true, player.MaxHP);
        Expect(_hits.Count == 1 && _hits[0].VictimIsPlayer && _hits[0].Weight == FeedbackTable.WeightFor(score) && ReferenceEquals(_hits[0].Attacker, striker),
            "hits: a hit on the player must say so, weighed by the share of HP it cost");
        if (_hits.Count == 1)
        {
            float playerStop = FeedbackTable.VictimStop(_hits[0].Weight, score, true, 1f);
            int playerTicks = await HeldTicks(player);
            Expect(playerTicks == TicksFor(playerStop) && playerStop >= FeedbackTable.PlayerVictimMinSeconds - 0.0001f,
                $"hit-stop: an 18-damage hit held the player {playerTicks} ticks, the table says {TicksFor(playerStop)}");
            GD.Print($"  feel     the player hit 18 of {player.MaxHP:0} HP scores {score:0.0} ({_hits[0].Weight}): held {Ms(playerStop)} ms, direction {_hits[0].Direction.X:0.00},{_hits[0].Direction.Y:0.00} away from the foe");
        }

        await Strike(stage);
    }

    // ---------------------------------------------------------------- 3. hit-stop

    private async Task ProbeHitStop()
    {
        Node2D stage = BuildStage(0f, 6000f);
        var player = (Pomegraknight)SpawnFighter(stage, PomeScene, 600f);
        await Ticks(30);

        // On the ground, running: held where it stands, then on at the speed it had.
        _pad.Hold(ControlAction.MoveRight);
        await Ticks(40);
        float before = player.GlobalPosition.X;
        await Ticks(1);
        float step = player.GlobalPosition.X - before;
        float granted = player.ApplyHitStop(0.10f);
        int holdTicks = TicksFor(0.10f);
        Vector2 heldAt = player.GlobalPosition;
        float moved = 0f;
        for (int i = 0; i < holdTicks; i++)
        {
            await Ticks(1);
            moved = Mathf.Max(moved, player.GlobalPosition.DistanceTo(heldAt));
        }
        Expect(Mathf.IsEqualApprox(granted, 0.10f), $"hit-stop: a free body asked to hold 100 ms was granted {Ms(granted)}");
        Expect(moved < 0.01f, $"hit-stop: a held body moved {moved:0.00} px in {holdTicks} ticks");
        Expect(!player.InHitStop, $"hit-stop: a 100 ms stop was still holding after {holdTicks} ticks");
        await Ticks(1);
        float resumed = player.GlobalPosition.X - heldAt.X;
        Expect(step > 3f && Mathf.Abs(resumed - step) < 0.05f, $"hit-stop: running {step:0.00} px a tick, the body came out of the stop at {resumed:0.00}");
        _pad.Hold(ControlAction.MoveRight, false);
        await Ticks(40);

        // In the air: no fall while held, and the arc goes on from the velocity it had.
        _pad.Tap(ControlAction.Jump);
        await Ticks(12);
        float y0 = player.GlobalPosition.Y;
        await Ticks(1);
        float vyBefore = player.GlobalPosition.Y - y0;
        player.ApplyHitStop(0.10f);
        Vector2 hungAt = player.GlobalPosition;
        float fell = 0f;
        for (int i = 0; i < holdTicks; i++)
        {
            await Ticks(1);
            fell = Mathf.Max(fell, player.GlobalPosition.DistanceTo(hungAt));
        }
        await Ticks(1);
        float vyAfter = player.GlobalPosition.Y - hungAt.Y;
        float gravityStep = Units.Gravity * Tick * Tick;
        Expect(fell < 0.01f, $"hit-stop: a body held in the air moved {fell:0.00} px");
        Expect(vyBefore < -3f && Mathf.Abs(vyAfter - (vyBefore + gravityStep)) < 0.05f,
            $"hit-stop: rising {-vyBefore:0.00} px a tick, the body came out of the stop rising {-vyAfter:0.00} (one tick of gravity is {gravityStep:0.00})");
        GD.Print($"  feel     hit-stop       a 100 ms stop holds {holdTicks} ticks: moved {moved:0.00} px running and {fell:0.00} px in the air · out of it at {resumed:0.00} px a tick (was {step:0.00}) and rising {-vyAfter:0.00} (was {-vyBefore:0.00}, less one tick of gravity)");
        await Until(() => player.IsOnFloor(), 120);
        await Ticks(20);

        // Stops take the max, never add; and a body just out of one is not stopped again.
        player.ApplyHitStop(0.10f);
        player.ApplyHitStop(0.05f);
        int maxTicks = await HeldTicks(player);
        Expect(maxTicks == TicksFor(0.10f), $"hit-stop: 100 ms then 50 ms held {maxTicks} ticks; the max is {TicksFor(0.10f)}, the sum {TicksFor(0.15f)}");
        float tooSoon = player.ApplyHitStop(0.05f);
        Expect(tooSoon == 0f && !player.InHitStop, "hit-stop: a body just out of a stop must not be stopped again (re-arm)");
        await Ticks(Mathf.CeilToInt(FeedbackTable.StopRearmSeconds * 60f) + 1);
        Expect(player.ApplyHitStop(0.05f) > 0f, "hit-stop: after the re-arm a body can be stopped again");
        await Ticks(30);
        player.ApplyHitStop(0.05f);
        player.ApplyHitStop(0.12f);
        int longer = await HeldTicks(player);
        Expect(longer == TicksFor(0.12f), $"hit-stop: 50 ms then 120 ms held {longer} ticks; the longer one is {TicksFor(0.12f)}");
        await Ticks(60);

        // Jump pressed during a stop is not lost.
        ClearLog();
        Vector2 ground = player.GlobalPosition;
        player.ApplyHitStop(0.10f);
        await Ticks(2);
        _pad.Tap(ControlAction.Jump);
        await Ticks(2);
        Expect(player.GlobalPosition.DistanceTo(ground) < 0.01f && Count(_cues, "pomegraknight", "jump") == 0, "hit-stop: a held body must not jump until the stop ends");
        bool jumped = await Until(() => player.GlobalPosition.Y < ground.Y - 20f, 40);
        Expect(jumped && Count(_cues, "pomegraknight", "jump") == 1, "hit-stop: Jump pressed during a stop was lost");
        await Until(() => player.IsOnFloor(), 120);
        await Ticks(30);

        // The player's setting switches it off — and the hit still lands and is still told.
        DestroyObjective dummy = SpawnDummy(stage, player.GlobalPosition.X + 70f);
        await Ticks(20);
        FeedbackSettings.HitStop = 0f;
        ClearLog();
        _pad.Tap(ControlAction.Attack);
        bool landed = await Until(() => _hits.Count > 0, 90);
        Expect(landed && !player.InHitStop && !dummy.InHitStop && _hits.Count > 0 && _hits[0].VictimStop == 0f,
            "hit-stop: with the setting at 0 a hit must land and hold nobody");
        FeedbackSettings.HitStop = 0.5f;
        await Ticks(60);
        ClearLog();
        _pad.Tap(ControlAction.Attack);
        landed = await Until(() => _hits.Count > 0, 90);
        float half = landed ? _hits[0].VictimStop : -1f;
        Expect(landed && Mathf.Abs(half - FeedbackTable.VictimStop(_hits[0].Weight, _hits[0].Damage, false, 0.5f)) < 0.001f
            && Mathf.Abs(half - 0.5f * FeedbackTable.VictimStop(_hits[0].Weight, _hits[0].Damage, false, 1f)) < 0.001f,
            $"hit-stop: at half the setting a stop is {Ms(half)} ms, not half the designed one");
        FeedbackSettings.HitStop = 1f;
        await Ticks(60);

        // Clocks keep running: a cooldown started before a stop has dropped by the stop's
        // length when it ends, and a gain-no window runs out inside it.
        _pad.Tap(ControlAction.Skill1);
        await Until(() => player.ShiftCooldown.Remaining > 0f, 30);
        float cooldown = player.ShiftCooldown.Remaining;
        player.TakeHit(new HitInfo(1f, Vector2.Zero, 0.10f), player.GlobalPosition);     // a graze: stunned 0.1 s, not stopped
        player.ApplyHitStop(0.15f);
        await Ticks(TicksFor(0.15f) + 3);
        float dropped = cooldown - player.ShiftCooldown.Remaining;
        Expect(dropped > 0.12f && dropped < 0.30f, $"hit-stop: a cooldown dropped {dropped:0.00} s across a 0.15 s stop — every clock must keep running");
        Expect((player.FeedbackState & FeedbackState.Stunned) == 0, "hit-stop: a 0.1 s gain-no window must have run out inside a 0.15 s stop");
        await Ticks(60);

        await Strike(stage);
    }

    /// <summary>Kits that move themselves must hold for a stop too.</summary>
    private async Task ProbeKitPhysics()
    {
        int holdTicks = TicksFor(0.10f);

        // Sifu Pangda's dive: a body the kit moves after the base tick.
        Node2D stage = BuildStage(0f, 4000f);
        CharacterController pangda = SpawnFighter(stage, "res://Scenes/SifuPangda.tscn", 800f);
        await Ticks(30);
        _pad.Tap(ControlAction.Jump);
        await Ticks(24);
        float high = pangda.GlobalPosition.Y;
        _pad.Tap(ControlAction.Attack);
        bool diving = await Until(() => pangda.LocksPhysicalMotion && pangda.GlobalPosition.Y > high + 20f, 40);
        Expect(diving, "kit physics: Sifu Pangda never went into his dive");
        if (diving)
        {
            float d0 = pangda.GlobalPosition.Y;
            await Ticks(1);
            float diveStep = pangda.GlobalPosition.Y - d0;
            pangda.ApplyHitStop(0.10f);
            Vector2 hungAt = pangda.GlobalPosition;
            float moved = 0f;
            for (int i = 0; i < holdTicks; i++)
            {
                await Ticks(1);
                moved = Mathf.Max(moved, pangda.GlobalPosition.DistanceTo(hungAt));
            }
            await Ticks(1);
            float after = pangda.GlobalPosition.Y - hungAt.Y;
            Expect(moved < 0.01f, $"kit physics: Sifu Pangda's dive moved {moved:0.0} px while he was held");
            Expect(diveStep > 5f && (Mathf.Abs(after - diveStep) < 0.5f || pangda.IsOnFloor()),
                $"kit physics: diving {diveStep:0.0} px a tick, he came out of the stop at {after:0.0}");
            GD.Print($"  feel     kits           Sifu Pangda's dive ({diveStep:0.0} px a tick) holds for a stop: moved {moved:0.00} px in {holdTicks} ticks, then {after:0.0}");
        }
        await Strike(stage);

        // PumpKing's Soul Form: a body that never reaches the base tick.
        stage = BuildStage(0f, 4000f);
        var pumpKing = (PumpKing)SpawnFighter(stage, "res://Scenes/PumpKing.tscn", 800f);
        await Ticks(30);
        _pad.Tap(ControlAction.Skill1);
        bool soul = await Until(() => pumpKing.CurrentState == PumpKing.PKState.Soul, 120);
        Expect(soul, "kit physics: PumpKing never entered Soul Form");
        if (soul)
        {
            _pad.Hold(ControlAction.MoveRight);
            await Ticks(10);
            float x0 = pumpKing.GlobalPosition.X;
            await Ticks(1);
            float soulStep = pumpKing.GlobalPosition.X - x0;
            pumpKing.ApplyHitStop(0.10f);
            Vector2 heldAt = pumpKing.GlobalPosition;
            float moved = 0f;
            int stillHeld = 0;
            for (int i = 0; i < holdTicks; i++)
            {
                await Ticks(1);
                moved = Mathf.Max(moved, pumpKing.GlobalPosition.DistanceTo(heldAt));
                if (pumpKing.InHitStop) stillHeld++;
            }
            await Ticks(1);
            float after = pumpKing.GlobalPosition.X - heldAt.X;
            Expect(moved < 0.01f, $"kit physics: PumpKing's soul flew {moved:0.0} px while he was held");
            Expect(stillHeld == holdTicks - 1 && !pumpKing.InHitStop, "kit physics: a stop on a soul must run out on time (it is counted in the soul's own tick)");
            Expect(soulStep > 1f && Mathf.Abs(after - soulStep) < 0.05f, $"kit physics: flying {soulStep:0.00} px a tick, the soul came out of the stop at {after:0.00}");
            GD.Print($"  feel     kits           PumpKing's Soul Form ({soulStep:0.00} px a tick) holds for a stop: moved {moved:0.00} px, then {after:0.00}");
            _pad.Hold(ControlAction.MoveRight, false);
        }
        await Strike(stage);

        // PumpKing's head: a projectile. Its hits are ranged, and the blast is one cue.
        stage = BuildStage(0f, 4000f);
        CharacterController owner = SpawnFighter(stage, "res://Scenes/PumpKing.tscn", 600f);
        DestroyObjective target = SpawnDummy(stage, 1200f);
        await Ticks(30);
        ClearLog();
        var head = Spawn<PumpKingHead>(stage, "res://Scenes/PumpKingHead.tscn", target.GlobalPosition + new Vector2(-70f, -0.1f));
        head.Init(new Vector2(190f, 0f), owner, 1f, 1f, Units.Px(3f), 50f, Units.Px(15f), null, CombatSide.Player);
        bool heldOwner = false;
        for (int i = 0; i < 120 && _hits.Count < 2; i++)
        {
            await Ticks(1);
            heldOwner |= owner.InHitStop;
        }
        Expect(_hits.Count == 2, $"kit physics: a head that touches a target lands its contact and its blast — {_hits.Count} hits");
        foreach (HitCue hit in _hits)
            Expect(hit.Ranged && hit.AttackOwner == "pumpking" && ReferenceEquals(hit.Attacker, owner) && (hit.AttackAction == "ba.head" || hit.AttackAction == "ba.explode"),
                $"kit physics: the head's hits must be ranged pumpking hits, got {hit.AttackOwner}/{hit.AttackAction} ranged={hit.Ranged}");
        Expect(!heldOwner, "hit-stop: a projectile's hits must never hold its owner");
        Expect(Find(_cues, "pumpking", "ba.explode", out Cue blast) && Mathf.IsEqualApprox(blast.Size, Units.Px(3f)) && blast.Magnitude > 0f && blast.OwnerClass == CueOwners.Projectile,
            "cues: the head's blast is one ba.explode cue carrying its real radius");
        Expect(ShakeCamera2D.Instance != null && ShakeCamera2D.Instance.Trauma > 0f, "feel: the head's blast must still shake the camera — through the cue now");
        await Strike(stage);
    }

    // ---------------------------------------------------------------- 4. body cues

    private async Task ProbeBodyCues()
    {
        Node2D stage = BuildStage(0f, 4000f);
        ClearLog();
        float drop = 240f;
        CharacterController player = SpawnFighter(stage, PomeScene, 900f, drop);
        await Until(() => Count(_cues, "pomegraknight", "land") > 0, 120);

        Expect(Count(_cues, "pomegraknight", "spawn") == 1, "cues: a body raises one spawn, on its first tick");
        Expect(Find(_cues, "pomegraknight", "spawn", out Cue spawn) && spawn.Position.DistanceTo(new Vector2(900f, FloorY - 32f - drop)) < 8f && spawn.OwnerClass == CueOwners.Fighter,
            "cues: spawn must be raised where the spawner put the body, not at the scene origin");
        float predicted = Mathf.Sqrt(2f * Units.Gravity * drop);
        Expect(Count(_cues, "pomegraknight", "land") == 1 && Find(_cues, "pomegraknight", "land", out Cue land)
            && Mathf.Abs(land.Magnitude - predicted) < predicted * 0.06f && Mathf.Abs(land.Position.Y - FloorY) < 2f,
            $"cues: a {M(drop)} m drop lands once, at the feet, at about {predicted:0} px/s");
        await Ticks(20);

        // A walk raises no landing; a ground jump is #1 with no tag; a second jump is #2, "air".
        ClearLog();
        player.MaxJumps = 2;
        _pad.Hold(ControlAction.MoveRight);
        await Ticks(30);
        _pad.Hold(ControlAction.MoveRight, false);
        Expect(Count(_cues, "pomegraknight", "land") == 0 && Count(_cues, "pomegraknight", "jump") == 0, "cues: walking on flat ground raises no land or jump");
        _pad.Tap(ControlAction.Jump);
        await Ticks(22);
        _pad.Tap(ControlAction.Jump);
        await Ticks(6);
        Expect(Find(_cues, "pomegraknight", "jump", out Cue first, 1) && first.Tag == null && Mathf.IsEqualApprox(first.Magnitude, Units.JumpSpeed)
            && Mathf.Abs(first.Position.Y - FloorY) < 2f,
            "cues: a ground jump is jump #1, untagged, at the feet, with the launch speed");
        Expect(Find(_cues, "pomegraknight", "jump", out Cue second, 2) && second.Tag == "air", "cues: a second jump before landing is jump #2, tagged 'air'");
        Expect(Count(_cues, "pomegraknight", "jump") == 2, "cues: two jumps raise two jump cues");
        await Until(() => player.IsOnFloor(), 200);
        await Ticks(2);
        Expect(Count(_cues, "pomegraknight", "land") == 1, "cues: coming down from a jump raises one land");
        player.MaxJumps = 1;

        ClearLog();
        player.Heal(12f);
        Expect(Find(_cues, "pomegraknight", "heal", out Cue heal) && Mathf.IsEqualApprox(heal.Magnitude, 12f), "cues: a heal raises heal with the amount");
        player.GainUltCharge(player.MaxUltCharge);
        player.GainUltCharge(10f);
        Expect(Count(_cues, "pomegraknight", "ult.ready") == 1, "cues: a full ult charge raises ult.ready once, not on every point after");

        // Death and respawn.
        ClearLog();
        player.TakeHit(new HitInfo(99999f), player.GlobalPosition + new Vector2(-50f, 0f));
        Expect(_hits.Count == 1 && _hits[0].Killed && _hits[0].VictimIsPlayer, "cues: the hit that kills the player is told as a kill");
        Expect(Count(_cues, "pomegraknight", "death") == 1 && player.FeedbackState == FeedbackState.Dead, "cues: a fighter that dies raises one death");
        player.Respawn();
        Expect(Count(_cues, "pomegraknight", "respawn") == 1, "cues: a respawn raises respawn");
        await Ticks(10);

        // A body the arena swaps in arrives as switch.in — not as a second spawn.
        ClearLog();
        var incoming = Spawn<CharacterController>(stage, "res://Scenes/PumpKing.tscn", new Vector2(1400f, FloorY - 32f));
        incoming.UseControls(new VirtualPad());
        player.NotifySwitchedOut();
        incoming.NotifySwitchedIn();
        await Ticks(4);
        Expect(Count(_cues, "pomegraknight", "switch.out") == 1 && Count(_cues, "pumpking", "switch.in") == 1 && Count(_cues, "pumpking", "spawn") == 0,
            "cues: a swap is switch.out then switch.in, and the incoming body raises no spawn");

        await Strike(stage);
    }

    // ---------------------------------------------------------------- status loops and hurt ticks

    private async Task ProbeStatusAndHurt()
    {
        Node2D stage = BuildStage(0f, 4000f);
        CharacterController player = SpawnFighter(stage, PomeScene, 900f);
        DestroyObjective dummy = SpawnDummy(stage, 1500f);
        await Ticks(30);

        // Each loop starts once and stops once.
        ClearLog();
        player.SetBurning(0.3f);
        player.ApplyTrapped(0.2f);
        player.TakeHit(new HitInfo(1f, Vector2.Zero, 0.2f), player.GlobalPosition);
        dummy.SetBurning(0.3f);
        dummy.ApplyTrapped(0.2f);
        await Ticks(8);
        foreach (string action in new[] { "status.burning", "status.trapped", "status.stunned" })
            Expect(Count(_starts, "pomegraknight", action) == 1 && Count(_stops, "pomegraknight", action) == 0, $"loops: {action} must start once while it lasts (fighter)");
        Expect(Count(_starts, "destroy_objective", "status.burning") == 1 && Count(_starts, "destroy_objective", "status.trapped") == 1,
            "loops: a foe's statuses start as loops too");
        Expect((player.FeedbackState & (FeedbackState.Burning | FeedbackState.Trapped)) == (FeedbackState.Burning | FeedbackState.Trapped),
            "state: FeedbackState must say what the body is under");
        await Ticks(40);
        foreach (string action in new[] { "status.burning", "status.trapped", "status.stunned" })
            Expect(Count(_starts, "pomegraknight", action) == 1 && Count(_stops, "pomegraknight", action) == 1, $"loops: {action} must stop once when it ends (fighter)");
        Expect(Count(_stops, "destroy_objective", "status.burning") == 1 && Count(_stops, "destroy_objective", "status.trapped") == 1,
            "loops: a foe's statuses stop when they end");

        // The hazard stacks, ended by death; and a status ended by leaving the tree.
        ClearLog();
        player.AddFireStack(30f);
        player.AddFrozenStack(30f);
        dummy.SetBurning(30f);
        await Ticks(8);
        Expect(Count(_starts, "pomegraknight", "status.on_fire") == 1 && Count(_starts, "pomegraknight", "status.frozen") == 1, "loops: OnFire and Frozen start as loops");
        player.TakeHit(new HitInfo(99999f), player.GlobalPosition);
        Expect(Count(_stops, "pomegraknight", "status.on_fire") == 1 && Count(_stops, "pomegraknight", "status.frozen") == 1, "loops: a body that dies stops its loops");
        dummy.QueueFree();
        await Ticks(4);
        Expect(Count(_stops, "destroy_objective", "status.burning") == 1, "loops: a body that leaves the tree stops its loops");
        player.Respawn();
        await Ticks(150);                                  // past the respawn's 2 s of invulnerability

        // A standing burn deals damage every tick and must not raise a cue every tick.
        dummy = SpawnDummy(stage, 1500f);
        await Ticks(20);
        ClearLog();
        float hpBefore = dummy.CurrentHp;
        dummy.SetBurning(2f);
        await Ticks(140);
        int burnCues = Count(_cues, "destroy_objective", HurtTickMeter.Action, "burn");
        float told = 0f;
        foreach (Cue cue in _cues) if (cue.Action == HurtTickMeter.Action && cue.Owner == "destroy_objective") told += cue.Magnitude;
        float burned = hpBefore - dummy.CurrentHp;
        int most = Mathf.CeilToInt(2f / FeedbackTable.HurtTickCueSeconds) + 1;
        Expect(burnCues >= 3 && burnCues <= most, $"hurt.tick: 2 s of burning (120 ticks of damage) raised {burnCues} cues; at most {most}");
        Expect(burned > 20f && told <= burned + 0.01f && told > burned * 0.75f, $"hurt.tick: the cues carry {told:0.0} of the {burned:0.0} damage dealt");
        Expect(_hits.Count == 0 && !dummy.InHitStop, "hurt.tick: damage over time is not a hit and stops nobody");

        // A hazard: its own tick cue at the victim, the victim's hurt.tick, no hit, no stop.
        ClearLog();
        var spikes = ResourceLoader.Load<PackedScene>("res://Scenes/SpikeHazard.tscn").Instantiate<Hazard>();
        spikes.BoxSize = new Vector2(200f, 120f);
        stage.AddChild(spikes);
        spikes.GlobalPosition = player.GlobalPosition;
        bool everHeld = false;
        for (int i = 0; i < 90; i++)
        {
            await Ticks(1);
            everHeld |= player.InHitStop;
        }
        int hazardTicks = Count(_cues, CueOwners.Hazard, "tick", "spike");
        Expect(hazardTicks >= 2 && Find(_cues, CueOwners.Hazard, "tick", out Cue tick) && tick.Position.DistanceTo(player.GlobalPosition) < 40f
            && ReferenceEquals(tick.Source, spikes) && tick.Magnitude > 0f && tick.Side == CombatSide.Player,
            $"hazard: spikes under the player raise hazard/tick tagged 'spike' at the victim ({hazardTicks} in 1.5 s)");
        int hurt = Count(_cues, "pomegraknight", HurtTickMeter.Action, "hazard");
        Expect(hurt >= 2 && hurt <= hazardTicks, $"hurt.tick: the player's hazard damage is told as hurt.tick 'hazard' ({hurt} for {hazardTicks} ticks)");
        Expect(_hits.Count == 0 && !everHeld, "hazard: a hazard tick is not a hit and stops nobody");
        var lava = ResourceLoader.Load<PackedScene>("res://Scenes/LavaHazard.tscn").Instantiate<Hazard>();
        var fire = ResourceLoader.Load<PackedScene>("res://Scenes/FireHazard.tscn").Instantiate<Hazard>();
        var freeze = ResourceLoader.Load<PackedScene>("res://Scenes/FreezeHazard.tscn").Instantiate<Hazard>();
        Expect(spikes.Kind == "spike" && lava.Kind == "lava" && fire.Kind == "fire" && freeze.Kind == "freeze",
            $"hazard: kinds must be spike / lava / fire / freeze, got {spikes.Kind} / {lava.Kind} / {fire.Kind} / {freeze.Kind}");
        lava.Free();
        fire.Free();
        freeze.Free();

        await Strike(stage);
    }

    // ---------------------------------------------------------------- the world

    private const string SeaTile = "softvolume.ln_water";

    private static SoftVolume AddSea(Node2D stage, Rect2 rect)
    {
        if (!MapCreation.Data.TileRegistry.TryGet(SeaTile, out MapCreation.Data.TileDef def)) return null;
        var box = new MapCreation.Runtime.MapCollisionSpec(
            new RectangleShape2D { Size = rect.Size }, rect.Size * 0.5f, new Rect2(Vector2.Zero, rect.Size));
        SoftVolume sea = MapCreation.Runtime.MapCollision.CreateSoftVolume(def, false, rect.Position, new[] { box }, "FeedbackCheck");
        if (sea != null) stage.AddChild(sea);
        return sea;
    }

    /// <summary>A sea laid as two tiles is entered once and left once.</summary>
    private async Task ProbeWorld()
    {
        Node2D stage = BuildStage(0f, 4000f);
        SoftVolume left = AddSea(stage, new Rect2(800f, FloorY - 400f, 300f, 400f));
        SoftVolume right = AddSea(stage, new Rect2(1100f, FloorY - 400f, 300f, 400f));
        Expect(left != null && right != null && left.MediumKind == "water", $"world: the sea tile must read as water, not '{left?.MediumKind}'");
        if (left == null || right == null) { await Strike(stage); return; }
        var plain = new SoftVolume();
        var current = new SoftVolume { IsMedium = false };
        Expect(plain.MediumKind == "foliage" && current.MediumKind == "current", "world: an untuned volume is foliage, one that only carries a current");
        plain.MediumKind = "cloud";
        Expect(plain.MediumKind == "cloud", "world: whoever builds a volume may name its kind");
        plain.Free();
        current.Free();

        ClearLog();
        CharacterController player = SpawnFighter(stage, PomeScene, 1000f, 520f);       // above the sea
        bool entered = await Until(() => Count(_cues, CueOwners.World, "soft.enter") > 0, 120);
        Expect(entered && Find(_cues, CueOwners.World, "soft.enter", out Cue enter) && enter.Tag == "water" && enter.Magnitude > 100f
            && ReferenceEquals(enter.Source, player) && enter.Direction.Y > 0.9f,
            "world: falling into the sea raises soft.enter tagged 'water', with the body, its speed and its direction");
        await Ticks(30);
        Expect((player.FeedbackState & FeedbackState.Submerged) != 0, "state: a body in the sea is Submerged");
        // It sinks to the sea bed at the sea's own 3 m/s — a touchdown too gentle to be a landing.
        bool settled = await Until(() => player.IsOnFloor(), 400);
        await Ticks(2);
        Expect(settled && Count(_cues, "pomegraknight", "land") == 0,
            $"cues: settling on the sea bed at the sink speed must raise no land (under {FeedbackTable.LandCueMinSpeed:0} px/s is not a landing) — {Count(_cues, "pomegraknight", "land")} raised");
        _pad.Hold(ControlAction.MoveRight);
        await Until(() => player.GlobalPosition.X > 1250f, 400);
        _pad.Hold(ControlAction.MoveRight, false);
        Expect(player.GlobalPosition.X > 1250f && Count(_cues, CueOwners.World, "soft.enter") == 1 && Count(_cues, CueOwners.World, "soft.exit") == 0,
            $"world: swimming across the joint of two sea tiles raised {Count(_cues, CueOwners.World, "soft.enter")} soft.enter and {Count(_cues, CueOwners.World, "soft.exit")} soft.exit — one sea is entered once");
        player.GlobalPosition = new Vector2(2400f, FloorY - 32f);
        bool left0 = await Until(() => Count(_cues, CueOwners.World, "soft.exit") > 0, 30);
        Expect(left0 && Count(_cues, CueOwners.World, "soft.exit") == 1 && Find(_cues, CueOwners.World, "soft.exit", out Cue exit) && exit.Tag == "water",
            "world: leaving the sea raises one soft.exit tagged 'water'");

        await Strike(stage);
    }

    // ---------------------------------------------------------------- foes

    private async Task ProbeFoes()
    {
        Node2D stage = BuildStage(0f, 6000f);
        CharacterController player = SpawnFighter(stage, PomeScene, 600f);
        player.Invincible = true;                                  // the foes may try; nothing lands
        await Ticks(20);

        // A crab dropped from 4 m: spawn (with its level), land; hit, it turns on the player.
        ClearLog();
        var crab = Spawn<CrabFoe>(stage, "res://Scenes/Foes/CrabFoe.tscn", new Vector2(3000f, FloorY - 22f - 128f));
        crab.Init(3);
        await Until(() => Count(_cues, "crab", "land") > 0, 120);
        Expect(Find(_cues, "crab", "spawn", out Cue spawn) && spawn.Variant == 3 && spawn.OwnerClass == CueOwners.Foe && spawn.Position.DistanceTo(new Vector2(3000f, FloorY - 150f)) < 8f,
            "foes: a foe raises spawn where it was placed, with its level");
        Expect(Count(_cues, "crab", "land") == 1, "foes: a foe that falls onto the floor raises one land");
        ClearLog();
        crab.TakeHit(new HitInfo(1f), crab.GlobalPosition + new Vector2(-40f, 0f));
        crab.TakeHit(new HitInfo(1f), crab.GlobalPosition + new Vector2(-40f, 0f));
        Expect(Count(_cues, "crab", "aggro") == 1, $"foes: a foe turning on the player raises aggro once, not on every hit ({Count(_cues, "crab", "aggro")})");

        // Evolution after 25 s in the fight.
        ClearLog();
        bool evolved = await Until(() => Count(_cues, "crab", "evolve") > 0, 60 * 27);
        Expect(evolved && Find(_cues, "crab", "evolve", out Cue evolve) && evolve.Variant == 4 && crab.CurrentLevel == 4, "foes: 25 s in, a foe evolves and says so, with its new level");
        crab.QueueFree();

        // A level-6 crab beside the player jumps to their height: jump, then land.
        // (It sees the player on its first sight check and jumps as soon as it stands.)
        ClearLog();
        var jumper = Spawn<CrabFoe>(stage, "res://Scenes/Foes/CrabFoe.tscn", new Vector2(660f, FloorY - 22f));
        jumper.Init(6);
        bool leapt = await Until(() => Count(_cues, "crab", "jump") > 0, 240);
        Expect(Count(_cues, "crab", "aggro") == 1, "foes: a foe that sees the player raises aggro");
        Expect(leapt, "foes: a crab that launches itself raises jump");
        if (leapt) Expect(await Until(() => Count(_cues, "crab", "land") > 0, 240), "foes: …and land when it comes down");
        jumper.QueueFree();
        await Ticks(4);

        // A seagull's dash: the telegraph is a loop, with the tell's length, reach and direction.
        // (A gull faces left or right at random: half the time it sees the player at once.
        // A hit turns the other half on them, so the dash is ready either way.)
        ClearLog();
        var gull = Spawn<SeagullFoe>(stage, "res://Scenes/Foes/SeagullFoe.tscn", new Vector2(900f, FloorY - 200f));
        gull.Init(6);
        await Ticks(3);
        if (Count(_starts, "seagull", "telegraph") == 0) gull.TakeHit(new HitInfo(1f), gull.GlobalPosition);
        bool told = await Until(() => Count(_starts, "seagull", "telegraph") > 0, 240);
        Expect(told, "foes: a seagull's dash must start its telegraph loop");
        if (told)
        {
            Cue tell = _starts[_starts.FindIndex(c => c.Action == "telegraph")];
            Expect(Mathf.IsEqualApprox(tell.Magnitude, 1f) && Mathf.IsEqualApprox(tell.Size, Units.Px(15f)) && tell.Direction.Dot((player.GlobalPosition - gull.GlobalPosition).Normalized()) > 0.95f,
                $"foes: the telegraph carries the tell's 1 s, the dash's 15 m and its direction (got {tell.Magnitude} s, {M(tell.Size)} m)");
            bool stopped = await Until(() => Count(_stops, "seagull", "telegraph") > 0, 120);
            Expect(stopped && Count(_starts, "seagull", "telegraph") == 1 && Count(_stops, "seagull", "telegraph") == 1, "foes: the telegraph loop stops once, when the dash begins");

            // The dash reaches the player: a blow of the gull's own body, which holds the gull.
            // Which blow is the game's business — a gull that flies through the player usually
            // lands its contact hit a tick before its dash hit, and the i-frames eat the dash.
            player.Invincible = false;
            bool struck = await Until(() => _hits.Exists(h => h.VictimIsPlayer), 60);
            Expect(struck, "foes: the seagull's dash never reached the player it was aimed at");
            if (struck)
            {
                HitCue dash = _hits.Find(h => h.VictimIsPlayer);
                Expect(dash.AttackOwner == "seagull" && (dash.AttackAction == "s2.dash" || dash.AttackAction == "contact")
                    && ReferenceEquals(dash.Attacker, gull) && !dash.Ranged && dash.AttackClass == CueOwners.Foe,
                    $"foes: a dashing gull's hit is seagull/s2.dash or seagull/contact, a blow of its body — got {dash.AttackOwner}/{dash.AttackAction}");
                Expect(gull.InHitStop && player.InHitStop, "hit-stop: a foe's own blow holds the foe and the player it hit");
                GD.Print($"  feel     foes           a seagull's dash: telegraph loop 1 s, then its {dash.AttackAction} hit ({dash.Weight}) holds the player {Ms(dash.VictimStop)} ms and the gull too");
            }
            player.Invincible = true;
        }
        gull.QueueFree();

        // A walking foe held by a stop stands exactly where it is, then walks on as it was.
        var doomed = Spawn<CrabFoe>(stage, "res://Scenes/Foes/CrabFoe.tscn", new Vector2(3000f, FloorY - 22f));
        doomed.Init(1);                                            // level 1: no sight, it only patrols
        await Ticks(20);
        float cx = doomed.GlobalPosition.X;
        await Ticks(1);
        float crabStep = doomed.GlobalPosition.X - cx;
        doomed.ApplyHitStop(0.10f);
        Vector2 crabAt = doomed.GlobalPosition;
        float crabMoved = 0f;
        int crabTicks = TicksFor(0.10f);
        for (int i = 0; i < crabTicks; i++)
        {
            await Ticks(1);
            crabMoved = Mathf.Max(crabMoved, doomed.GlobalPosition.DistanceTo(crabAt));
        }
        Expect(crabMoved < 0.01f && !doomed.InHitStop, $"hit-stop: a held foe moved {crabMoved:0.00} px in {crabTicks} ticks");
        await Ticks(1);
        float crabAfter = doomed.GlobalPosition.X - crabAt.X;
        Expect(Mathf.Abs(crabStep) > 0.5f && Mathf.Abs(crabAfter - crabStep) < 0.05f, $"hit-stop: walking {crabStep:0.00} px a tick, the foe came out of the stop at {crabAfter:0.00}");

        // A foe that dies raises death with its level; a status it was under stops.
        ClearLog();
        doomed.TakeHit(new HitInfo(99999f), doomed.GlobalPosition);
        Expect(Find(_cues, "crab", "death", out Cue death) && death.Variant == 1 && Count(_cues, "crab", "death") == 1, "foes: a foe that dies raises one death, with its level");

        await Strike(stage);
    }

    // ---------------------------------------------------------------- 5. the reference kit

    private async Task ProbePomegraknight()
    {
        Node2D stage = BuildStage(0f, 6000f);
        var player = (Pomegraknight)SpawnFighter(stage, PomeScene, 900f);
        DestroyObjective dummy = SpawnDummy(stage, 970f);
        await Ticks(30);

        // The combo: three swings, three stages, the third heavier.
        ClearLog();
        for (int stage123 = 1; stage123 <= 3; stage123++)
        {
            int swings = Count(_cues, "pomegraknight", "ba.swing");
            _pad.Tap(ControlAction.Attack);
            await Until(() => Count(_cues, "pomegraknight", "ba.swing") > swings, 30);
            await Ticks(20);
        }
        for (int stage123 = 1; stage123 <= 3; stage123++)
        {
            bool swung = Find(_cues, "pomegraknight", "ba.swing", out Cue swing, stage123);
            Expect(swung && Mathf.IsEqualApprox(swing.Size, player.SlashRange) && swing.Direction == Vector2.Right && swing.Tag == null && ReferenceEquals(swing.Source, player),
                $"pome: slash {stage123} raises ba.swing #{stage123} with the cone's reach ({player.SlashRange} px) and her facing");
            HitCue hit = _hits.Find(h => h.AttackVariant == stage123);
            float damage = stage123 == 3 ? 30f : 15f;
            Expect(hit.AttackAction == "ba" && hit.Weight == FeedbackTable.WeightFor(damage) && Mathf.IsEqualApprox(hit.Damage, damage),
                $"pome: slash {stage123}'s hit is ba#{stage123}, {FeedbackTable.WeightFor(damage)} — got {hit.AttackAction}#{hit.AttackVariant} {hit.Weight} for {hit.Damage}");
        }
        Expect(_hits.Count == 3 && Count(_cues, "pomegraknight", "ba.swing") == 3, $"pome: a three-slash combo is three swings and three hits ({Count(_cues, "pomegraknight", "ba.swing")} / {_hits.Count})");
        await Ticks(70);                                                              // the reload

        // Blush, then the next slash: burning, and the Fire Tornado as a loop with its box.
        ClearLog();
        _pad.Tap(ControlAction.Skill1);
        Expect(await Until(() => Count(_cues, "pomegraknight", "s1.cast") > 0, 30), "pome: Blush raises s1.cast");
        await Until(() => Count(_starts, "pomegraknight", "status.burning") > 0, 20);
        Expect(Count(_starts, "pomegraknight", "status.burning") == 1, "pome: Blush sets her burning — the status loop starts");
        _pad.Tap(ControlAction.Attack);
        bool spinning = await Until(() => Count(_starts, "pomegraknight", "s1.tornado") > 0, 30);
        Expect(spinning && Find(_cues, "pomegraknight", "ba.swing", out Cue burningSwing) && burningSwing.Tag == "burning", "pome: a slash while burning is tagged 'burning'");
        Expect(spinning, "pome: the slash after Blush starts the s1.tornado loop");
        if (spinning)
        {
            Cue tornado = _starts[_starts.FindIndex(c => c.Action == "s1.tornado")];
            Expect(tornado.Extents == new Vector2(player.TornadoHalfW, player.TornadoHalfH) && Mathf.IsEqualApprox(tornado.Magnitude, player.TornadoDuration),
                $"pome: the tornado loop carries its box ({player.TornadoHalfW} × {player.TornadoHalfH} px half-extents) and its length");
            bool heldByField = false;
            await Ticks(TicksFor(0.2f) + 12);                      // past the slash's own stop
            for (int i = 0; i < 60 * 3 && Count(_stops, "pomegraknight", "s1.tornado") == 0; i++)
            {
                await Ticks(1);
                heldByField |= player.InHitStop;
            }
            int ticks = Count(_cues, "pomegraknight", "s1.tornado.tick");
            Expect(Count(_stops, "pomegraknight", "s1.tornado") == 1 && Count(_starts, "pomegraknight", "s1.tornado") == 1, "pome: the tornado loop stops once, when it ends");
            // Five by design (2.5 s at 0.5 s); the tick clock runs in the process loop, so a
            // slow frame can cost the last one — that is the kit's own timing, not feedback's.
            Expect((ticks == 5 || ticks == 4) && Find(_cues, "pomegraknight", "s1.tornado.tick", out _, ticks), $"pome: a 2.5 s tornado ticks five times, each told in order — {ticks}");
            int fieldHits = 0;
            foreach (HitCue hit in _hits)
            {
                if (hit.AttackAction != "s1.tornado") continue;
                fieldHits++;
                Expect(hit.Ranged && hit.Weight == HitWeight.Light && ReferenceEquals(hit.Attacker, player), "pome: a tornado tick's hit is ranged, named Light, and hers");
            }
            Expect(fieldHits == ticks, $"pome: every tick lands on a target inside the box ({fieldHits} hits for {ticks} ticks)");
            Expect(!heldByField, "hit-stop: the tornado's ticks must never hold her — a field is ranged");
        }
        await Ticks(30);

        // Seed Eruption: one cue per wave, never one per seed; seeds hit as ranged, or expire.
        ClearLog();
        _pad.Tap(ControlAction.Skill2);
        Expect(await Until(() => Count(_cues, "pomegraknight", "s2.cast") > 0, 30), "pome: Seed Eruption raises s2.cast");
        bool heldBySeeds = false;
        for (int i = 0; i < 60 * 3 && Count(_cues, "pomegraknight", "s2.wave") < 3; i++)
        {
            await Ticks(1);
            heldBySeeds |= player.InHitStop;
        }
        int[] seeds = { 6, 8, 10 };
        for (int wave = 1; wave <= 3; wave++)
            Expect(Find(_cues, "pomegraknight", "s2.wave", out Cue fan, wave) && Mathf.IsEqualApprox(fan.Magnitude, seeds[wave - 1]) && fan.Direction == Vector2.Up,
                $"pome: wave {wave} raises s2.wave #{wave} with its {seeds[wave - 1]} seeds as the magnitude");
        Expect(Count(_cues, "pomegraknight", "s2.wave") == 3, "pome: three waves are three cues");
        int launchCues = Count(_cues, "pomegraknight", "s2.cast") + Count(_cues, "pomegraknight", "s2.wave");
        int otherHers = 0;
        foreach (Cue cue in _cues)
            if (cue.Owner == "pomegraknight" && cue.Action != "s2.cast" && cue.Action != "s2.wave" && cue.Action != "s2.seed.expire") otherHers++;
        Expect(otherHers == 0, $"pome: the eruption raised {otherHers} cues of hers that are neither s2.cast, s2.wave nor a seed's end");
        Expect(launchCues == 4, $"pome: launching 24 seeds raised {launchCues} cues — s2.cast and three waves, never one per seed");
        for (int i = 0; i < 60 * 7 && CountSeeds(stage) > 0; i++)
        {
            await Ticks(1);
            heldBySeeds |= player.InHitStop;
        }
        int seedHits = 0;
        foreach (HitCue hit in _hits)
        {
            if (hit.AttackAction != "s2.seed") continue;
            seedHits++;
            Expect(hit.Ranged && hit.AttackOwner == "pomegraknight" && ReferenceEquals(hit.Attacker, player), "pome: a seed's hit is ranged and hers");
        }
        int expired = Count(_cues, "pomegraknight", "s2.seed.expire");
        Expect(seedHits + expired == 24, $"pome: 24 seeds each end in a hit or an expiry — {seedHits} hits, {expired} expired");
        Expect(expired > 0 && Find(_cues, "pomegraknight", "s2.seed.expire", out Cue gone) && gone.Source == null && gone.OwnerClass == CueOwners.Projectile && gone.Position.Y > FloorY - 40f,
            "pome: a seed that dies on terrain raises s2.seed.expire where it died, with no source");
        Expect(!heldBySeeds, "hit-stop: her seeds must never hold her");
        GD.Print($"  feel     the reference  Pomegraknight: 3 slashes (ba.swing #1–3, the third {FeedbackTable.WeightFor(30f)}) · Blush + a 5-tick tornado loop · 24 seeds told in 3 wave cues, {seedHits} hit and {expired} expired on the floor");

        await Strike(stage);
    }

    private static int CountSeeds(Node stage)
    {
        int seeds = 0;
        foreach (Node child in stage.GetChildren()) if (child is PomeSeed) seeds++;
        return seeds;
    }

    // ---------------------------------------------------------------- 6. the feel director

    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    /// <summary>Wait in REAL time — what the flash limits count in, whatever the time scale.</summary>
    private async Task RealSeconds(float seconds)
    {
        ulong until = Time.GetTicksMsec() + (ulong)(seconds * 1000f);
        while (Time.GetTicksMsec() < until) await Frames(1);
    }

    /// <summary>Watch a zoom punch play out: its peak, and whether the view rolled meanwhile.</summary>
    private async Task<(float Peak, bool Rolled)> WatchPunch(ShakeCamera2D view, int budgetTicks = 240)
    {
        float peak = 0f;
        bool rolled = false;
        ulong until = Engine.GetPhysicsFrames() + (ulong)budgetTicks;
        while (Engine.GetPhysicsFrames() < until)
        {
            await Frames(1);
            peak = Mathf.Max(peak, view.PunchAmount);
            rolled |= !view.IgnoreRotation;
            if (peak > 0f && view.PunchAmount <= 0f) break;
        }
        return (peak, rolled);
    }

    private static ShaderMaterial FlashOf(Node body) => body.GetNode<Sprite2D>("Sprite2D").Material as ShaderMaterial;
    private static float FlashAmount(Node body) => FlashOf(body) is { } material ? material.GetShaderParameter("flash_amount").AsSingle() : 0f;

    private async Task ProbeFeel()
    {
        Node2D stage = BuildStage(0f, 8000f);
        CharacterController player = SpawnFighter(stage, PomeScene, 900f);
        var dummies = new List<DestroyObjective>();
        for (int i = 0; i < 12; i++) dummies.Add(SpawnDummy(stage, 2000f + i * 300f));
        await Ticks(30);
        ShakeCamera2D view = ShakeCamera2D.Instance;
        FeelDirector director = FeelDirector.Instance;
        Expect(view != null && view.GetParent() == player, "feel: the shake camera must be the player's");
        Expect(director != null, "feel: the FeelDirector autoload is not running — is it registered in project.godot?");
        if (view == null || director == null) { await Strike(stage); return; }
        Vector2 far = new(0f, 0f);
        FeelTable.WeightFeel light = FeelTable.For(HitWeight.Light), medium = FeelTable.For(HitWeight.Medium), heavy = FeelTable.For(HitWeight.Heavy);

        // Shake: per-tier trauma with caps. Twelve light hits in one frame stay under the cap.
        await Until(() => view.Trauma <= 0f, 120);
        await Frames(1);
        foreach (DestroyObjective dummy in dummies) dummy.TakeHit(new HitInfo(15f), far);
        float crowd = view.Trauma;
        float expected = Mathf.Min(light.ShakeCap, light.ShakeAdd + FeelTable.MaxExtraVictims * FeelTable.ExtraVictimShake);
        Expect(crowd <= light.ShakeCap + 0.0001f && Mathf.Abs(crowd - expected) < 0.001f,
            $"feel: twelve Light hits in one frame put trauma at {crowd:0.00}; the tier's cap is {light.ShakeCap:0.00} (one add of {light.ShakeAdd:0.00} and a sliver per extra victim)");
        // …and a stream of them, frame after frame, never climbs past it.
        float peak = 0f;
        for (int i = 0; i < 40; i++)
        {
            dummies[i % dummies.Count].TakeHit(new HitInfo(15f), far);
            peak = Mathf.Max(peak, view.Trauma);
            await Frames(1);
        }
        Expect(peak <= light.ShakeCap + 0.0001f, $"feel: forty Light hits in a row reached trauma {peak:0.00}; the cap is {light.ShakeCap:0.00}");
        // A heavier hit tops the frame up to its own tier and no further.
        await Until(() => view.Trauma <= 0f, 240);
        await Frames(1);
        dummies[0].TakeHit(new HitInfo(15f), far);
        dummies[1].TakeHit(new HitInfo(80f), far);
        dummies[2].TakeHit(new HitInfo(15f), far);
        Expect(Mathf.Abs(view.Trauma - (heavy.ShakeAdd + FeelTable.ExtraVictimShake)) < 0.001f && view.Trauma <= heavy.ShakeCap,
            $"feel: a Light, a Heavy and a Light in one frame is one Heavy add and a sliver ({view.Trauma:0.00}), never the sum");
        GD.Print($"  feel     shake          12 Light hits at once: trauma {crowd:0.00} (cap {light.ShakeCap:0.00}, {light.ShakeCap * light.ShakeCap * view.MaxOffset:0.0} px) · 40 in a row peak {peak:0.00} · a Heavy among them {heavy.ShakeAdd + FeelTable.ExtraVictimShake:0.00} (cap {heavy.ShakeCap:0.00}, {heavy.ShakeCap * heavy.ShakeCap * view.MaxOffset:0.0} px)");

        // The kick follows the blow; a heavy hit rolls and punches the zoom; all of it returns.
        Expect(view.KickOffset.Length() > 0f && view.RollTrauma > 0f, "feel: a Heavy hit kicks the view and adds roll");
        Vector2 restZoom = view.Zoom;
        (float punchPeak, bool rolled) = await WatchPunch(view);
        await Until(() => view.Trauma <= 0f && view.RollTrauma <= 0f && view.PunchAmount <= 0f, 600);
        await Frames(2);
        Expect(Mathf.Abs(punchPeak - heavy.Zoom) < 0.0015f, $"feel: a Heavy hit punches the zoom by {punchPeak:0.000}; the table says {heavy.Zoom:0.000}");
        Expect(rolled, "feel: a Heavy hit rolls the view a little");
        Expect(view.Zoom == restZoom && view.IgnoreRotation && view.Rotation == 0f && view.AppliedOffset == Vector2.Zero && view.KickOffset == Vector2.Zero,
            "feel: once it has played out the camera is exactly as it was — zoom, rotation, offset");

        dummies[3].TakeHit(new HitInfo(15f), dummies[3].GlobalPosition + new Vector2(-100f, 0f));
        Expect(Mathf.IsEqualApprox(view.KickOffset.Length(), light.KickPx) && view.KickOffset.X > 0f,
            $"feel: a Light hit from the left kicks the view {view.KickOffset.Length():0.0} px to the right; the table says {light.KickPx:0.0}");
        await Until(() => view.Trauma <= 0f && view.KickOffset == Vector2.Zero, 240);
        await Frames(1);

        // The camera's own guarantee, whoever calls it: a tier's adds stop at its cap, and a
        // lighter hit never changes a shake a heavier one started.
        for (int i = 0; i < 10; i++) view.AddTrauma(light.ShakeAdd, light.ShakeCap);
        float capped = view.Trauma;
        view.AddTrauma(0.60f, 0.90f);
        float heavier = view.Trauma;
        view.AddTrauma(light.ShakeAdd, light.ShakeCap);
        Expect(Mathf.IsEqualApprox(capped, light.ShakeCap) && Mathf.IsEqualApprox(heavier, 0.90f) && Mathf.IsEqualApprox(view.Trauma, heavier),
            $"feel: ten Light adds must stop at the Light cap ({capped:0.00} of {light.ShakeCap:0.00}), and a lighter add must leave a heavier shake alone ({view.Trauma:0.00} of {heavier:0.00})");
        await Until(() => view.Trauma <= 0f, 240);
        await Frames(1);

        // Being hit is felt more than hitting: the player's camera reads one tier up.
        player.TakeHit(new HitInfo(18f, new Vector2(100f, 0f), 0f), player.GlobalPosition + new Vector2(-60f, 0f));
        Expect(Mathf.Abs(view.Trauma - medium.ShakeAdd) < 0.001f, $"feel: a Light hit on the player shakes like a Medium ({view.Trauma:0.00}, the table says {medium.ShakeAdd:0.00})");
        await Ticks(60);

        // A killing hit adds a little, whatever its weight.
        await Until(() => view.Trauma <= 0f && view.PunchAmount <= 0f, 600);
        await Frames(1);
        DestroyObjective fragile = SpawnDummy(stage, 5800f, 5f);
        await Ticks(4);
        fragile.TakeHit(new HitInfo(15f), far);
        float killTrauma = view.Trauma;
        (float killPunch, _) = await WatchPunch(view);
        Expect(Mathf.Abs(killTrauma - (light.ShakeAdd + FeelTable.KillShakeAdd)) < 0.001f && Mathf.Abs(killPunch - FeelTable.KillZoom) < 0.0015f,
            $"feel: a kill adds {FeelTable.KillShakeAdd:0.00} trauma and a {FeelTable.KillZoom:0.000} zoom punch (got {killTrauma:0.00}, {killPunch:0.000})");

        // The comfort settings: no shake at 0, no punch at 0, and the tsunami's channel still works.
        await Until(() => view.Trauma <= 0f && view.PunchAmount <= 0f, 600);
        view.SetSustainedTrauma(0.5f);
        await Frames(3);
        Expect(view.Trauma == 0f && view.AppliedOffset != Vector2.Zero && view.AppliedOffset.Length() <= view.MaxOffset * 0.25f * 1.5f,
            "feel: the sustained (tsunami) channel must shake the view without touching impact trauma");
        FeedbackSettings.ScreenShake = 0f;
        await Frames(3);
        Expect(view.AppliedOffset == Vector2.Zero, "feel: with screen shake at 0 nothing moves the view — the sustained channel included");
        view.SetSustainedTrauma(0f);
        FeedbackSettings.CameraPunch = 0f;
        dummies[4].TakeHit(new HitInfo(80f), far);
        bool moved = false;
        for (int i = 0; i < 300; i++) { await Frames(1); moved |= view.AppliedOffset != Vector2.Zero || view.Zoom != restZoom || !view.IgnoreRotation; }
        Expect(!moved, "feel: with shake and punch at 0 a Heavy hit must not move or zoom the view at all");
        FeedbackSettings.ResetToDefaults();
        await Until(() => view.Trauma <= 0f && view.PunchAmount <= 0f && view.RollTrauma <= 0f, 600);

        // The smooth noise: how often the shake reverses in a second.
        FastNoiseLite noise = ShakeCamera2D.CreateShakeNoise();
        int reversals = 0;
        float last = 0f, lastDelta = 0f;
        const int samples = 6000;
        for (int i = 0; i <= samples; i++)
        {
            float x = ShakeCamera2D.ShakeDirection(noise, i / 600f, view.NoiseSpeed).X;
            float d = x - last;
            if (i > 1 && d * lastDelta < 0f) reversals++;
            if (i > 0 && d != 0f) lastDelta = d;
            last = x;
        }
        float perSecond = reversals / 10f;
        Expect(perSecond >= 12f && perSecond <= 28f, $"feel: the shake reverses {perSecond:0.0} times a second; smooth noise should land near twenty");
        GD.Print($"  feel     shake          smooth noise reverses {perSecond:0.0} times a second · a Light kick is {light.KickPx:0} px, a Heavy {heavy.KickPx:0} px with a {heavy.Zoom * 100f:0} % zoom punch · the player being hit reads one tier up");

        // The flash: applied through its own channel, restored, and nothing else touched.
        // (The limits count in real time, so each of these waits out the last flashing moment.)
        await Ticks(30);
        await RealSeconds(1.1f);
        DestroyObjective flashed = dummies[5];
        Sprite2D sprite = flashed.GetNode<Sprite2D>("Sprite2D");
        flashed.SetBurning(5f);                                   // a status tint is on: it must survive
        await Ticks(3);
        Color tintBefore = sprite.Modulate, selfBefore = sprite.SelfModulate;
        Expect(sprite.Material == null && tintBefore != Colors.White, "flash: a body at rest has no flash material, and a burning foe wears its tint");
        flashed.TakeHit(new HitInfo(15f), far);
        ShaderMaterial flash = FlashOf(flashed);
        Expect(flash != null && Mathf.IsEqualApprox(FlashAmount(flashed), 1f) && flash.GetShaderParameter("flash_color").AsColor().IsEqualApprox(FeelTable.LightFlash),
            "flash: a hit shows its victim in the flash colour at full strength (light, on a dark or unknown arena)");
        // Looked at in the same instant, before the foe's own tick can repaint its tint.
        bool untouched = sprite.Modulate == tintBefore && sprite.SelfModulate == selfBefore && sprite.Visible;
        await Ticks(1);
        Expect(untouched && sprite.Modulate == tintBefore && sprite.SelfModulate == selfBefore && sprite.Visible, "flash: a flash must not touch Modulate, SelfModulate or Visible");
        await Ticks(Mathf.CeilToInt(light.FlashSeconds * 60f) + 2);
        Expect(sprite.Material == null && sprite.Modulate == tintBefore && sprite.Visible, "flash: a few frames later the body is handed back exactly as it was, tint and all");
        // On the player too — the i-frame blink owns Visible, the flash its own channel.
        await RealSeconds(0.15f);
        Sprite2D playerSprite = player.GetNode<Sprite2D>("Sprite2D");
        Color playerTint = playerSprite.Modulate, playerSelf = playerSprite.SelfModulate;
        player.TakeHit(new HitInfo(18f, new Vector2(60f, 0f), 0f), player.GlobalPosition + new Vector2(-60f, 0f));
        Expect(FlashOf(player) != null && Mathf.IsEqualApprox(FlashAmount(player), 1f), "flash: the player's body flashes when hit");
        Expect(playerSprite.Modulate == playerTint && playerSprite.SelfModulate == playerSelf, "flash: the player's flash leaves Modulate and SelfModulate alone");
        await Ticks(12);
        Expect(FlashOf(player) == null, "flash: the player's flash ends by itself while the i-frame blink goes on");
        await Ticks(60);

        // A graze only tints; "reduce flashing" turns every flash into the tint.
        await RealSeconds(0.15f);
        dummies[6].TakeHit(new HitInfo(3f), far);
        Expect(Mathf.IsEqualApprox(FlashAmount(dummies[6]), FeelTable.TintStrength), $"flash: a graze tints ({FlashAmount(dummies[6]):0.00}), it does not flash");
        FeedbackSettings.Flash = 0f;
        dummies[7].TakeHit(new HitInfo(80f), far);
        Expect(Mathf.IsEqualApprox(FlashAmount(dummies[7]), FeelTable.TintStrength), "flash: with flashes turned down a Heavy hit still tints its target, and no more");
        FeedbackSettings.Flash = 1f;
        await Ticks(30);

        // Light arena, dark flash: the arena says how pale its backdrop is.
        CueBus.EmitAt(CueOwners.Flow, "arena.enter", Vector2.Zero, magnitude: 0.90f, tag: "BM");
        Expect(director.ArenaIsLight && director.ArenaWorld == "BM", "flash: an arena with a pale backdrop must be taken as light");
        await RealSeconds(0.15f);
        dummies[8].TakeHit(new HitInfo(15f), far);
        Expect(FlashOf(dummies[8]) != null && FlashOf(dummies[8]).GetShaderParameter("flash_color").AsColor().IsEqualApprox(FeelTable.DarkFlash),
            "flash: on a pale arena a body flashes dark — a white flash is invisible on bare paper");
        CueBus.EmitAt(CueOwners.Flow, "arena.enter", Vector2.Zero, magnitude: 0.76f, tag: "");
        Expect(!director.ArenaIsLight, "flash: a sky-blue backdrop keeps the light flash");
        CueBus.EmitAt(CueOwners.Flow, "arena.enter", Vector2.Zero, magnitude: -1f);
        Expect(!director.ArenaIsLight && director.FlashColour == FeelTable.LightFlash, "flash: with no backdrop to go by the flash is light");
        await Ticks(30);

        // The limits: at most six bodies flash at full strength in one moment.
        await RealSeconds(1.1f);
        int demotedBefore = director.FlashesDemoted;
        foreach (DestroyObjective dummy in dummies) dummy.TakeHit(new HitInfo(15f), far);
        int full = 0, tinted = 0;
        foreach (DestroyObjective dummy in dummies)
        {
            float amount = FlashAmount(dummy);
            if (amount > 0.99f) full++;
            else if (Mathf.IsEqualApprox(amount, FeelTable.TintStrength)) tinted++;
        }
        Expect(full == FeelTable.MaxFullFlashesPerMoment && tinted == dummies.Count - full && director.FlashesDemoted - demotedBefore == tinted,
            $"flash: twelve bodies hit at once — {full} flashed in full and {tinted} were tinted; the limit is {FeelTable.MaxFullFlashesPerMoment}");
        GD.Print($"  feel     flash          a hit flashes {Ms(light.FlashSeconds)}–{Ms(FeelTable.For(HitWeight.Massive).FlashSeconds)} ms · twelve at once: {full} in full, {tinted} tinted · a pale arena (0.90) flashes dark, sky blue (0.76) and unknown flash light");

        await Strike(stage);
    }

    // ---------------------------------------------------------------- 7. the bus

    private int _afterThrower;
    private static void Thrower(Cue cue) => throw new InvalidOperationException("a broken effect (FeedbackCheck threw this on purpose)");
    private static void HitThrower(HitCue hit) => throw new InvalidOperationException("a broken effect (FeedbackCheck threw this on purpose)");
    private void AfterThrower(Cue cue) => _afterThrower++;
    private void AfterHitThrower(HitCue hit) => _afterThrower++;
    private static void Quiet(Cue cue) { }
    private static void QuietHit(HitCue hit) { }

    private async Task ProbeTheBus()
    {
        Node2D stage = BuildStage(0f, 4000f);
        CharacterController player = SpawnFighter(stage, PomeScene, 900f);
        DestroyObjective dummy = SpawnDummy(stage, 1400f);
        await Ticks(30);

        // A listener that throws: the hit still lands, and the listeners after it still hear.
        GD.Print("  (the next few ERROR lines are this check's own listener, throwing on purpose)");
        CueBus.Raised += Thrower;
        CueBus.LoopStarted += Thrower;
        CueBus.LoopStopped += Thrower;
        CueBus.HitLanded += HitThrower;
        CueBus.Raised += AfterThrower;
        CueBus.LoopStarted += AfterThrower;
        CueBus.LoopStopped += AfterThrower;
        CueBus.HitLanded += AfterHitThrower;
        _afterThrower = 0;
        float hp = dummy.CurrentHp;
        float dealt = dummy.TakeHit(new HitInfo(20f, new Vector2(50f, 0f)).From(player, "probe"), player.GlobalPosition);
        dummy.SetBurning(0.05f);
        player.Heal(1f);
        await Ticks(12);
        CueBus.Raised -= Thrower;
        CueBus.LoopStarted -= Thrower;
        CueBus.LoopStopped -= Thrower;
        CueBus.HitLanded -= HitThrower;
        CueBus.Raised -= AfterThrower;
        CueBus.LoopStarted -= AfterThrower;
        CueBus.LoopStopped -= AfterThrower;
        CueBus.HitLanded -= AfterHitThrower;
        Expect(Mathf.IsEqualApprox(dealt, 20f) && Mathf.Abs(hp - dummy.CurrentHp - 20f) < 1.5f,
            $"bus: with a listener throwing, a 20-damage hit dealt {dealt} and the fight went on");
        Expect(_afterThrower >= 4, $"bus: a listener that throws must not silence the ones after it — they heard {_afterThrower} of at least 4 cues (a hit, a heal, a loop start and its stop)");

        // The master switch.
        int raised = CueBus.RaisedCount, hits = CueBus.HitCount;
        CueBus.Enabled = false;
        player.Heal(1f);
        dummy.TakeHit(new HitInfo(5f), player.GlobalPosition);
        CueBus.Enabled = true;
        Expect(CueBus.RaisedCount == raised && CueBus.HitCount == hits, "bus: switched off, nothing is raised");

        // Allocation: raising cues — with the feel director listening and acting on them —
        // must create no garbage. Measured after a warm-up, with this check's own recorder off.
        await Ticks(60);
        _recording = false;
        CueBus.Raised += Quiet;
        CueBus.LoopStarted += Quiet;
        CueBus.LoopStopped += Quiet;
        CueBus.HitLanded += QuietHit;
        var probe = new HitInfo(15f, new Vector2(40f, 0f)).From(player, "ba", 1);
        var heavy = new HitInfo(80f, new Vector2(40f, 0f)).FromRanged("pomegraknight", "s2.seed");
        for (int i = 0; i < 300; i++) RaiseOneOfEach(player, dummy, probe, heavy);
        long before = GC.GetAllocatedBytesForCurrentThread();
        const int rounds = 2000;
        for (int i = 0; i < rounds; i++) RaiseOneOfEach(player, dummy, probe, heavy);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        CueBus.Raised -= Quiet;
        CueBus.LoopStarted -= Quiet;
        CueBus.LoopStopped -= Quiet;
        CueBus.HitLanded -= QuietHit;
        _recording = true;
        Expect(allocated == 0, $"bus: raising {rounds * 7} cues allocated {allocated} bytes ({allocated / (float)(rounds * 7):0.0} a cue); steady state must allocate nothing");
        GD.Print($"  feel     the bus        {rounds * 7} cues raised with the feel director listening (shake, kick, flash): {allocated} bytes allocated · a listener that throws is logged and skipped, and the hit lands");

        await Strike(stage);
    }

    /// <summary>Seven cues: two one-shots, a loop's start and stop, a world cue, two hits.</summary>
    private static void RaiseOneOfEach(CharacterController player, DestroyObjective dummy, in HitInfo melee, in HitInfo ranged)
    {
        CueBus.Emit(player, "ba.swing", variant: 2, size: 130f, magnitude: 15f, direction: Vector2.Right, tag: "burning");
        CueBus.Emit(dummy, HurtTickMeter.Action, magnitude: 3f, tag: "burn");
        CueBus.Start(player, "s1.tornado", size: 128f, extents: new Vector2(128f, 40f));
        CueBus.Stop(player, "s1.tornado");
        CueBus.EmitAt(CueOwners.World, "soft.enter", dummy.GlobalPosition, dummy, CombatSide.Foe, magnitude: 300f, tag: "water");
        CueBus.EmitHit(new HitCue(dummy, melee, HitWeight.Light, 15f, melee.Knockback, player.GlobalPosition, false, 0.044f));
        CueBus.EmitHit(new HitCue(player, ranged, HitWeight.Heavy, 80f, ranged.Knockback, dummy.GlobalPosition, false, 0.1f));
    }

    // ---------------------------------------------------------------- 8. flow

    private static List<Cue> FlowOf(List<Cue> log)
    {
        var flow = new List<Cue>();
        foreach (Cue cue in log) if (cue.Owner == CueOwners.Flow) flow.Add(cue);
        return flow;
    }

    /// <summary>The real Arena, staged the way debug mode stages it: the stand-in boss on
    /// the legacy arena, then a second arena to lose every life in.</summary>
    private async Task ProbeFlow()
    {
        await Ticks(3);
        ClearLog();
        var arena = ResourceLoader.Load<PackedScene>("res://Scenes/Arena.tscn").Instantiate<GameManager>();
        arena.DebugMission = MissionType.Boss;
        arena.DebugNodeLevel = 4;
        arena.DebugBossId = "";
        arena.DebugCombatMapPath = "";
        AddChild(arena);

        bool opened = Find(_cues, CueOwners.Flow, "arena.enter", out Cue enter);
        Expect(opened && enter.Tag == "" && enter.Magnitude >= 0f && enter.Magnitude <= 1f && enter.Variant == 4,
            "flow: opening an arena raises arena.enter with its world ('' on the legacy arena) and its backdrop's lightness");
        bool started = await Until(() => Count(_cues, CueOwners.Flow, "mission.start") > 0, 60 * 5);
        List<Cue> flow = FlowOf(_cues);
        string order = string.Join(" ", flow.ConvertAll(c => c.Action == "countdown.tick" ? "tick" + c.Variant : c.Action));
        Expect(started && order.StartsWith("arena.enter tick3 tick2 tick1 countdown.go mission.start"),
            $"flow: an arena opens with arena.enter, a tick for 3, 2 and 1, countdown.go and mission.start — got: {order}");
        Expect(Find(_cues, CueOwners.Flow, "mission.start", out Cue start) && start.Tag == "boss", "flow: mission.start is tagged with the mission type");

        bool intro = await Until(() => Count(_cues, CueOwners.Flow, "boss.intro") > 0, 60);
        Node bossNode = GetTree().GetFirstNodeInGroup("foe");
        Expect(intro && Find(_cues, CueOwners.Flow, "boss.intro", out Cue introCue) && introCue.Tag == "boss_crab" && introCue.Source is BossCrab,
            "flow: staging the boss raises boss.intro, tagged with which boss and carrying it");
        CharacterController player = CharacterController.LocalPlayer;
        Expect(player != null && Count(_cues, player?.FeedbackId ?? "", "spawn") == 1, "flow: the arena's fighter raised its spawn");
        Expect(FeelDirector.Instance != null && FeelDirector.Instance.ArenaIsLight == (enter.Magnitude >= FeelTable.LightArenaLuminance) && FeelDirector.Instance.ArenaWorld == "",
            "flow: the feel director takes the arena's world and backdrop from arena.enter");

        if (player != null)
        {
            // The player goes down (a debug arena has lives): player.down, then a respawn.
            player.TakeHit(new HitInfo(99999f), player.GlobalPosition);
            Expect(Count(_cues, CueOwners.Flow, "player.down") == 1, "flow: the player dying raises player.down");
            Expect(ShakeCamera2D.Instance != null && ShakeCamera2D.Instance.Trauma >= FeelTable.ByCue.Resolve(CueOwners.Flow, null, "player.down").ShakeAdd - 0.001f,
                "feel: player.down is a beat the camera feels");
            Expect(await Until(() => Count(_cues, player.FeedbackId, "respawn") > 0, 60 * 3), "flow: with a life left the arena brings the player back — respawn");
        }

        // The boss falls: boss.defeated, then mission.success.
        ClearLog();
        if (bossNode is BossCrab boss)
        {
            Vector2 where = boss.GlobalPosition;
            boss.TakeHit(new HitInfo(1e7f), where);
            bool resolved = await Until(() => Count(_cues, CueOwners.Flow, "mission.success") > 0, 30);
            Expect(Find(_cues, CueOwners.Flow, "boss.defeated", out Cue fell) && fell.Tag == "boss_crab" && fell.Position.DistanceTo(where) < 64f,
                "flow: a boss that falls raises boss.defeated where it stood");
            Expect(resolved && Find(_cues, CueOwners.Flow, "mission.success", out Cue won) && won.Tag == "boss" && Count(_cues, CueOwners.Flow, "mission.fail") == 0,
                "flow: the mission resolving raises mission.success, tagged with its type");
            float beat = FeelTable.ByCue.Resolve(CueOwners.Flow, null, "boss.defeated").Zoom;
            GetTree().Paused = false;                              // a debug arena pauses under its banner
            float punch = ShakeCamera2D.Instance != null ? (await WatchPunch(ShakeCamera2D.Instance, 600)).Peak : 0f;
            Expect(Mathf.Abs(punch - beat) < 0.003f, $"feel: boss.defeated is the biggest beat — a zoom punch of {beat:0.00} (saw {punch:0.000})");
        }
        else Expect(false, "flow: the Boss mission staged no boss");

        GetTree().Paused = false;
        arena.QueueFree();
        await Ticks(4);

        // A second arena: every life lost ends the run.
        ClearLog();
        arena = ResourceLoader.Load<PackedScene>("res://Scenes/Arena.tscn").Instantiate<GameManager>();
        arena.DebugMission = MissionType.Slaughter;
        arena.PreCombatCountdownSeconds = 0.2f;
        arena.RespawnDelay = 0.1f;
        AddChild(arena);
        await Ticks(30);
        for (int life = 0; life < 6 && Count(_cues, CueOwners.Flow, "run.over") == 0; life++)
        {
            CharacterController body = CharacterController.LocalPlayer;
            if (body == null) break;
            body.Invincible = false;
            await Until(() => !body.IsDefeated && (body.FeedbackState & FeedbackState.Invulnerable) == 0, 60 * 4);
            body.TakeHit(new HitInfo(99999f), body.GlobalPosition);
            await Ticks(20);
            GetTree().Paused = false;
        }
        Expect(Count(_cues, CueOwners.Flow, "player.down") == 3 && Find(_cues, CueOwners.Flow, "run.over", out Cue over) && over.Tag == "death" && Count(_cues, CueOwners.Flow, "run.over") == 1,
            $"flow: three lives lost are three player.down and one run.over tagged 'death' ({Count(_cues, CueOwners.Flow, "player.down")} / {Count(_cues, CueOwners.Flow, "run.over")})");
        Expect(Find(_cues, CueOwners.Flow, "mission.start", out Cue slaughter) && slaughter.Tag == "slaughter", "flow: a Slaughter mission starts tagged 'slaughter'");
        GD.Print($"  feel     flow           arena.enter (legacy backdrop {enter.Magnitude:0.00}) → tick 3, 2, 1 → countdown.go → mission.start (boss) → boss.intro (boss_crab) → player.down → respawn → boss.defeated → mission.success · three lives lost: run.over (death)");

        GetTree().Paused = false;
        arena.QueueFree();
        await Ticks(4);
    }
}
