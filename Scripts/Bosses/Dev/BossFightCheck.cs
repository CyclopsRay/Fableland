namespace Fableland.Bosses.Dev;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using Fableland.Data;
using Fableland.Missions;
using Fableland.Run;

/// <summary>
/// Headless check for the boss-fight system (40-QA.md §2, FOES.gdd §8):
///
///     Godot --headless --path . res://Scenes/Bosses/BossFightCheck.tscn      # exit 0 = clean
///
/// 1. Pure logic — the virtual pad's edges, the rule scheduler, the boss table.
/// 2. Content — every boss row's scene is a Foe-side boss fighter with no camera.
/// 3. Physics, on a stage built from plain colliders: PumpKing's head bounces, rolls and
///    stops on the flat and runs a slope at its roll cap; a fighter walking a slope keeps its
///    feet on it; a boss is hit by the player's side and hits the player's side, and only
///    those; in a sea each side swims on its own controller only — the player's Up / Down
///    leave a boss sinking, and his pad's leave the player sinking (v0.11.1); and each of
///    the boss's rules does its own job in the one situation it is for
///    (back to his perch on foot and by jumping, facing the player, setting his head off
///    beside them, calling it back when it has rolled past).
/// 4. The fight — the real Arena scene, staged the way debug mode stages it, on the map the
///    run's own selection deals: the boss opens on its perch, bowls its head down the slope
///    at a player who stands still, and beating it resolves the mission and recruits it.
/// 5. Zone 6's own kits, and PumpKing's ultimate, the Black Prison (PumpKing.gdd): its second
///    of warning touches nobody; for four seconds a foe inside walks at half speed, jumps
///    half as high, starts no skill and is blind past one cell, while one outside is
///    untouched; a pumpkin every half second; the recast blinks him into the mouth for his
///    head's biggest blast; the cast range holds; and on either side — a BOSS PumpKing's
///    prison holds the player (silenced, slowed, low jumps, the view darkened), the player's
///    holds a boss (silenced, and his brain loses the player).
///
/// Like the map content check it drives the game's own input actions and reads positions.
/// Each failure prints on its own `FAIL` line; measured numbers print as `boss` lines and
/// are the ones to quote in a design doc.
/// </summary>
public partial class BossFightCheck : Node
{
    private readonly List<string> _problems = new();
    private int _checks;

    /// <summary>The simulation runs this many times faster than real time; each physics
    /// tick still advances exactly 1/60 s of game time (KNOWLEDGE v0.10.9).</summary>
    private const int SpeedUp = 8;
    private const float Tick = 1f / 60f;

    /// <summary>Pads a probe presses that no boss brain steps (a player-side fighter's): this
    /// node runs before the stage it owns, so a tap is seen by the body that same frame.</summary>
    private readonly List<VirtualPad> _pads = new();

    public override void _Process(double delta)
    {
        foreach (VirtualPad pad in _pads) pad.StepProcess();
    }

    public override void _PhysicsProcess(double delta)
    {
        foreach (VirtualPad pad in _pads) pad.StepPhysics();
    }

    public override async void _Ready()
    {
        int ticksPerSecond = Engine.PhysicsTicksPerSecond;
        double timeScale = Engine.TimeScale;
        try
        {
            RunLogicSelfTests();
            CheckBossContent();

            Engine.PhysicsTicksPerSecond = ticksPerSecond * SpeedUp;
            Engine.TimeScale = SpeedUp;
            await Ticks(1);          // every stage starts at the same point of a tick
            await ProbeHeadOnTheFlat();
            await ProbeHeadOnASlope();
            await ProbeWalkingASlope();
            await ProbeSides();
            await ProbeSwimmingTakesSides();
            await ProbeHisRules();
            await ProbeTheFight();
            await ProbeTheFerryman();
            await ProbeTheAsura();
            await ProbeMara();
            await ProbeTheBlackPrison();
            await ProbeBlackPrisonOnFoes();
            await ProbePrisonPumpkins();
            await ProbePrisonTakesSides();
        }
        catch (Exception e)
        {
            _problems.Add("boss check threw: " + e);
        }
        finally
        {
            Engine.PhysicsTicksPerSecond = ticksPerSecond;
            Engine.TimeScale = timeScale;
            foreach (StringName action in new StringName[] { "move_left", "move_right", InputDefaults.MoveUp, "move_down", "jump", "attack" })
                if (InputMap.HasAction(action)) Input.ActionRelease(action);
        }

        foreach (string problem in _problems) GD.Print("FAIL  " + problem);
        GD.Print(_problems.Count == 0
            ? $"BossFightCheck: OK ({_checks} checks, {BossTable.All.Count} boss(es))"
            : $"BossFightCheck: {_problems.Count} problem(s) in {_checks} checks");
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

    private static string M(float px) => (px / Units.PixelsPerMeter).ToString("0.0");

    /// <summary>"2.5 m/s up" / "3.0 m/s down", for a vertical speed in px/s (down positive).</summary>
    private static string Vertical(float down) => $"{M(Mathf.Abs(down))} m/s {(down < 0f ? "up" : "down")}";

    // ---------------------------------------------------------------- 1. logic

    private void RunLogicSelfTests()
    {
        Absorb("VirtualPad", VirtualPad.SelfTest());
        Absorb("BossRuleSet", BossRuleSet.SelfTest());
        Absorb("BossTable", BossTable.Validate());
    }

    // ---------------------------------------------------------------- 2. content

    private void CheckBossContent()
    {
        foreach (BossDef def in BossTable.All)
        {
            var scene = ResourceLoader.Load<PackedScene>(def.ScenePath);
            Expect(scene != null, $"{def.Id}: scene '{def.ScenePath}' does not load");
            if (scene == null) continue;

            Node node = scene.Instantiate();
            Expect(node is CharacterController { Side: CombatSide.Foe }, $"{def.Id}: its scene is not rooted in a Foe-side fighter");
            Expect(node is IBossFighter, $"{def.Id}: its scene's root does not implement IBossFighter");
            // A realm boss recruits its protagonist; a zone-6 boss (Docs/Zone6.gdd) recruits nobody.
            if (def.Recruits)
                Expect(Debug.ProtagonistRoster.GetScene(def.ProtagonistId) != null,
                    $"{def.Id}: recruits '{def.ProtagonistId}', which is not a protagonist");
            else
                Expect(def.Role != BossRole.Realm && string.IsNullOrEmpty(def.ProtagonistId),
                    $"{def.Id}: a boss that recruits nobody must be a zone-6 boss with no protagonist (role {def.Role}, '{def.ProtagonistId}')");
            node.Free();
        }
    }

    // ---------------------------------------------------------------- stage

    private const float FloorY = 600f;

    /// <summary>A stage of plain colliders on the Ground layer: a floor from x0 to x1 at
    /// <see cref="FloorY"/>, and optionally a ramp rising to the right at 1:2 — the grade
    /// of the slope tile — from (rampX, FloorY) up by `rampRise`, with a plateau after it.</summary>
    private Node2D BuildStage(float x0, float x1, float rampX = 0f, float rampRise = 0f, float plateau = 0f)
    {
        var stage = new Node2D { Name = "Stage" };
        AddChild(stage);
        AddBox(stage, new Rect2(x0, FloorY, x1 - x0, 200f));
        AddBox(stage, new Rect2(x0 - 40f, FloorY - 2000f, 40f, 2200f));     // walls: nothing leaves the stage
        AddBox(stage, new Rect2(x1, FloorY - 2000f, 40f, 2200f));
        if (rampRise > 0f)
        {
            float run = rampRise * 2f;
            var ramp = new StaticBody2D { CollisionLayer = 0, CollisionMask = 0 };
            ramp.SetCollisionLayerValue(Units.LayerGround, true);
            ramp.AddChild(new CollisionShape2D
            {
                Shape = new ConvexPolygonShape2D
                {
                    Points = new[] { new Vector2(rampX, FloorY), new Vector2(rampX + run, FloorY - rampRise), new Vector2(rampX + run, FloorY) },
                },
            });
            stage.AddChild(ramp);
            if (plateau > 0f) AddBox(stage, new Rect2(rampX + run, FloorY - rampRise, plateau, rampRise));
        }
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
        stage.QueueFree();
        await Ticks(2);
    }

    private static T Spawn<T>(Node parent, string scenePath, Vector2 at) where T : Node2D
    {
        var node = ResourceLoader.Load<PackedScene>(scenePath).Instantiate<T>();
        parent.AddChild(node);
        node.GlobalPosition = at;
        if (node is CharacterController body) body.SetSpawnPoint(at);
        return node;
    }

    private const string HeadScene = "res://Scenes/PumpKingHead.tscn";
    private const string PlayerScene = "res://Scenes/Pomegraknight.tscn";

    private static PumpKingHead LaunchHead(Node parent, Vector2 at, Vector2 velocity, CombatSide side = CombatSide.Player)
    {
        PumpKingHead head = Spawn<PumpKingHead>(parent, HeadScene, at);
        head.Init(velocity, null, 1f, 1f, Units.Px(3f), 50f, Units.Px(15f), null, side);
        return head;
    }

    // ---------------------------------------------------------------- 3a. the head on level ground

    /// <summary>Thrown level along flat ground the head must bounce, settle into a roll that
    /// the roll friction slows at 3 m/s², stop, and then go off — not die where it lands.</summary>
    private async Task ProbeHeadOnTheFlat()
    {
        Node2D stage = BuildStage(0f, 4000f);
        float neck = Units.Px(1.5f);

        // A level throw at the kit's launch speed: bounce, then whatever is left rolls.
        PumpKingHead thrown = LaunchHead(stage, new Vector2(400f, FloorY - neck), new Vector2(Units.Px(16f), 0f));
        float startX = thrown.GlobalPosition.X;
        int alive = 0;
        float lastX = startX;
        while (GodotObject.IsInstanceValid(thrown) && !thrown.Exploded && alive < 60 * 12)
        {
            lastX = thrown.GlobalPosition.X;
            await Ticks(1);
            alive++;
        }
        float thrownTravel = lastX - startX;
        Expect(alive < 60 * 12, "head on the flat: a thrown head never went off (it must come to rest and explode)");
        Expect(thrownTravel > Units.Px(3f) && thrownTravel < Units.Px(14f),
            $"head on the flat: a level 16 m/s throw travelled {M(thrownTravel)} m; expected a few metres of flight and bounce");
        await Ticks(60);

        // Set down already rolling at the roll cap: it must cover ground, slowing at the roll
        // friction, instead of losing 15% of its speed every tick (the pre-v0.10.11 bug).
        float roll = Units.Px(6f);
        // Resting on the floor (radius 22 + a hair): dropped from any height it would
        // bounce first, and a bounce keeps only 85% of its speed.
        PumpKingHead rolling = LaunchHead(stage, new Vector2(400f, FloorY - 22.1f), new Vector2(roll, 0f));
        await Ticks(2);
        float from = rolling.GlobalPosition.X;
        await Ticks(30);
        float halfSecond = rolling.GlobalPosition.X - from;
        // v0 = 6 m/s, a = −3 m/s²: 0.5 s covers 6·0.5 − ½·3·0.25 = 2.6 m.
        float expected = roll * 0.5f - 0.5f * Units.Px(3f) * 0.25f;
        Expect(Mathf.Abs(halfSecond - expected) < Units.Px(0.5f),
            $"head on the flat: rolling at 6 m/s it covered {M(halfSecond)} m in half a second; the roll friction (3 m/s²) gives {M(expected)} m");
        int rolledTicks = 32;
        while (GodotObject.IsInstanceValid(rolling) && !rolling.Exploded && rolledTicks < 60 * 12)
        {
            lastX = rolling.GlobalPosition.X;
            await Ticks(1);
            rolledTicks++;
        }
        float rolled = lastX - (from - roll * 2f * Tick);
        // 6 m/s against 3 m/s² stops in 2 s after 6 m, then sits still 0.5 s before going off.
        Expect(Mathf.Abs(rolled - Units.Px(6f)) < Units.Px(1f), $"head on the flat: a 6 m/s roll ran {M(rolled)} m before stopping; expected about 6 m");
        Expect(rolledTicks > 60 * 2 && rolledTicks < 60 * 4, $"head on the flat: a 6 m/s roll went off after {rolledTicks * Tick:0.0} s; expected about 2.5 s");

        GD.Print($"  boss     head, flat     a level 16 m/s throw carries {M(thrownTravel)} m · a 6 m/s roll runs {M(rolled)} m and goes off after {rolledTicks * Tick:0.0} s");
        await Strike(stage);
    }

    // ---------------------------------------------------------------- 3b. the head on a slope

    /// <summary>Set down at rest at the top of a 1:2 slope the head must run all of it at
    /// its roll cap — the reason PumpKing's castle stands at the top of one.</summary>
    private async Task ProbeHeadOnASlope()
    {
        float rise = Units.Px(16f);                       // 16 m up over 32 m: the castle slope's grade
        Node2D stage = BuildStage(0f, 4000f, rampX: 800f, rampRise: rise, plateau: 600f);
        float topX = 800f + rise * 2f;

        // On the plateau, a nudge toward the slope: it must roll over the lip and down.
        PumpKingHead head = LaunchHead(stage, new Vector2(topX + 40f, FloorY - rise - 22.1f), new Vector2(-Units.Px(3f), 0f));
        var samples = new List<Vector2>();
        int ticks = 0;
        while (GodotObject.IsInstanceValid(head) && !head.Exploded && ticks < 60 * 11)
        {
            samples.Add(head.GlobalPosition);
            await Ticks(1);
            ticks++;
        }

        float reachedX = samples.Count > 0 ? samples[^1].X : topX;
        float lowestY = FloorY - rise;
        foreach (Vector2 p in samples) lowestY = Mathf.Max(lowestY, p.Y);
        float descended = lowestY - (FloorY - rise - 23f);
        Expect(descended > rise * 0.9f, $"head on a slope: it came down only {M(descended)} m of a {M(rise)} m slope");

        // Speed on the slope, well after it has got going: the horizontal roll cap, 6 m/s.
        float capSpeed = 0f;
        for (int i = 60; i + 30 < samples.Count; i += 30)
        {
            if (samples[i].X > topX - 64f || samples[i + 30].X < 800f + 64f) continue;   // only while on the ramp
            capSpeed = Mathf.Max(capSpeed, Mathf.Abs(samples[i + 30].X - samples[i].X) / (30f * Tick));
        }
        Expect(Mathf.Abs(capSpeed - Units.Px(6f)) < Units.Px(0.15f),
            $"head on a slope: it ran at {M(capSpeed)} m/s across; the roll cap is 6.0");

        float slopeSeconds = 0f;
        for (int i = 0; i < samples.Count; i++)
            if (samples[i].X < topX && samples[i].X > 800f) slopeSeconds += Tick;
        GD.Print($"  boss     head, slope    rolls a 1:2 slope at {M(capSpeed)} m/s across · {M(rise * 2f)} m of slope in {slopeSeconds:0.0} s · came to x={M(reachedX - 800f)} m past its foot, alive {ticks * Tick:0.0} s");
        await Strike(stage);
    }

    // ---------------------------------------------------------------- 3c. walking a slope

    /// <summary>A fighter running down a 1:2 slope must stay on it every tick (floor snap),
    /// climbing must cost speed and descending must not, and a jump from a slope must still
    /// be a full jump.</summary>
    private async Task ProbeWalkingASlope()
    {
        float rise = Units.Px(12f);
        Node2D stage = BuildStage(0f, 4000f, rampX: 800f, rampRise: rise, plateau: 600f);
        float topX = 800f + rise * 2f;

        CharacterController walker = Spawn<CharacterController>(stage, PlayerScene, new Vector2(topX + 200f, FloorY - rise - 32f));
        await Ticks(20);                                                    // settle on the plateau
        Expect(walker.IsOnFloor(), "walking a slope: the fighter did not settle on the plateau");
        float runSpeed = walker.MoveSpeed;

        Input.ActionPress("move_left");
        int offFloor = 0, onSlope = 0;
        float downFrom = 0f, downTicks = 0f;
        for (int i = 0; i < 60 * 6 && walker.GlobalPosition.X > 800f + 40f; i++)
        {
            float before = walker.GlobalPosition.X;
            await Ticks(1);
            bool overRamp = walker.GlobalPosition.X < topX - 40f && walker.GlobalPosition.X > 800f + 40f;
            if (!overRamp) continue;
            onSlope++;
            if (!walker.IsOnFloor()) offFloor++;
            if (onSlope > 20) { downFrom += before - walker.GlobalPosition.X; downTicks++; }
        }
        Input.ActionRelease("move_left");
        float downSpeed = downTicks > 0 ? downFrom / (downTicks * Tick) : 0f;
        Expect(onSlope > 30, "walking a slope: the fighter never walked the slope");
        Expect(offFloor == 0, $"walking a slope: running downhill the fighter left the floor on {offFloor} of {onSlope} ticks (floor snap)");
        Expect(Mathf.Abs(downSpeed - runSpeed) < runSpeed * 0.08f,
            $"walking a slope: downhill it covered {M(downSpeed)} m/s across; its run speed is {M(runSpeed)}");

        await Ticks(30);
        Input.ActionPress("move_right");
        float upFrom = 0f, upTicks = 0f;
        int upSlope = 0, upOff = 0;
        for (int i = 0; i < 60 * 8 && walker.GlobalPosition.X < topX - 40f; i++)
        {
            float before = walker.GlobalPosition.X;
            await Ticks(1);
            bool overRamp = walker.GlobalPosition.X < topX - 40f && walker.GlobalPosition.X > 800f + 40f;
            if (!overRamp) continue;
            upSlope++;
            if (!walker.IsOnFloor()) upOff++;
            if (upSlope > 20) { upFrom += walker.GlobalPosition.X - before; upTicks++; }
        }
        Input.ActionRelease("move_right");
        float upSpeed = upTicks > 0 ? upFrom / (upTicks * Tick) : 0f;
        Expect(upOff == 0, $"walking a slope: climbing, the fighter left the floor on {upOff} of {upSlope} ticks");
        Expect(upSpeed > runSpeed * 0.6f && upSpeed < runSpeed * 0.97f,
            $"walking a slope: uphill it covered {M(upSpeed)} m/s across against {M(runSpeed)} on the flat; a 1:2 climb should cost some speed, not most of it");

        // A jump from the slope is still the standard jump.
        await Ticks(20);
        float startY = walker.GlobalPosition.Y, apex = startY;
        Input.ActionPress("jump");
        await Ticks(1);
        Input.ActionRelease("jump");
        for (int i = 0; i < 70; i++)
        {
            await Ticks(1);
            apex = Mathf.Min(apex, walker.GlobalPosition.Y);
        }
        float jump = startY - apex;
        Expect(Mathf.Abs(jump - Units.Px(8f)) < Units.Px(0.6f), $"walking a slope: a jump from the slope rose {M(jump)} m; the standard jump is 8 m");

        GD.Print($"  boss     fighter, slope runs {M(runSpeed)} m/s on the flat · {M(downSpeed)} down a 1:2 slope · {M(upSpeed)} up it · never leaves the floor · jumps {M(jump)} m from it");
        await Strike(stage);
    }

    // ---------------------------------------------------------------- 3d. sides

    /// <summary>The same kit on two sides: who is in which group, who owns the view, who
    /// reads the keyboard, and whose attacks land on whom.</summary>
    private async Task ProbeSides()
    {
        Node2D stage = BuildStage(0f, 4000f);
        BossDef def = BossTable.PumpKing;

        CharacterController player = Spawn<CharacterController>(stage, PlayerScene, new Vector2(900f, FloorY - 32f));
        CharacterController boss = Spawn<CharacterController>(stage, def.ScenePath, new Vector2(1500f, FloorY - 32f));
        var fighter = (IBossFighter)boss;
        Expect(boss.Controls is VirtualPad, "sides: a Foe-side fighter must not read the keyboard even before a brain is attached");
        fighter.BeginFight(def, 1, new BossArena(new[] { boss.GlobalPosition }, 0));
        await Ticks(10);

        Expect(player.IsInGroup(CombatTargets.PlayerGroup) && !player.IsInGroup(CombatTargets.EnemyGroup), "sides: the player's body must be in 'player' only");
        Expect(boss.IsInGroup(CombatTargets.EnemyGroup) && !boss.IsInGroup(CombatTargets.PlayerGroup) && !boss.IsInGroup("foe"),
            "sides: a boss must be in 'enemy' (the player's attacks iterate it) and in neither 'player' nor 'foe' (the spawn cap)");
        Expect(CharacterController.LocalPlayer == player, "sides: a boss must never become CharacterController.LocalPlayer");
        Expect(boss.GetNodeOrNull("Camera2D") == null, "sides: a boss must not keep its protagonist scene's camera");
        Expect(ShakeCamera2D.Instance != null && ShakeCamera2D.Instance.GetParent() == player, "sides: the shake camera must still be the player's");
        Expect(boss.Controls is VirtualPad && player.Controls is PlayerControls, "sides: a boss reads a virtual pad, the player the keyboard");
        Expect(boss.GetCollisionLayerValue(Units.LayerFoes) && !boss.GetCollisionLayerValue(Units.LayerPlayer), "sides: a boss sits on the Foes collision layer");
        Expect(Mathf.IsEqualApprox(boss.MaxHP, def.BaseHp) && Mathf.IsEqualApprox(boss.CurrentHP, def.BaseHp),
            $"sides: at foe level 1 the boss has its row's HP ({def.BaseHp}), not {boss.MaxHP}");

        var seenByPlayer = new List<ICombatTarget>(CombatTargets.HostileTo(GetTree(), CombatSide.Player));
        var seenByBoss = new List<ICombatTarget>(CombatTargets.HostileTo(GetTree(), CombatSide.Foe));
        Expect(seenByPlayer.Count == 1 && ReferenceEquals(seenByPlayer[0], boss), "sides: the player's side must see exactly the boss as a target");
        Expect(seenByBoss.Count == 1 && ReferenceEquals(seenByBoss[0], player), "sides: the boss's side must see exactly the player as a target");
        // Groups say where to look; the side says whom to hit. A body in the wrong group
        // must still never be a target of its own side.
        player.AddToGroup(CombatTargets.EnemyGroup);
        var withStray = new List<ICombatTarget>(CombatTargets.HostileTo(GetTree(), CombatSide.Player));
        player.RemoveFromGroup(CombatTargets.EnemyGroup);
        Expect(withStray.Count == 1 && ReferenceEquals(withStray[0], boss), "sides: a side must never be offered one of its own as a target, whatever group it is in");

        // From here on this probe counts HP hit by hit, so nothing may be thrown that it did
        // not throw itself: his brain goes to sleep (the pad stays his), and any head it
        // already bowled is allowed to finish. What the brain does is ProbeHisRules' subject.
        boss.GetNode<BossBrain>("BossBrain").Sleep();
        for (int i = 0; i < 60 * 12 && ((PumpKing)boss).HasLiveHead; i++) await Ticks(1);
        Expect(!((PumpKing)boss).HasLiveHead, "sides: a head the boss threw before his brain slept never went off");

        // The player's keyboard must not move the boss.
        float bossX = boss.GlobalPosition.X, playerX = player.GlobalPosition.X;
        Input.ActionPress("move_right");
        await Ticks(30);
        Input.ActionRelease("move_right");
        Expect(player.GlobalPosition.X > playerX + 40f, "sides: the player did not move on its own keys");
        Expect(Mathf.Abs(boss.GlobalPosition.X - bossX) < 2f, "sides: the boss moved when the PLAYER's key was held");
        await Ticks(20);

        // The boss takes every hit (no i-frames), with poise; the player keeps its i-frames.
        ICombatTarget target = boss;
        float hp = boss.CurrentHP + boss.Shield;
        float first = target.TakeHit(new HitInfo(20f, new Vector2(300f, 0f)), player.GlobalPosition);
        float second = target.TakeHit(new HitInfo(20f, new Vector2(300f, 0f)), player.GlobalPosition);
        Expect(Mathf.IsEqualApprox(first, 20f) && Mathf.IsEqualApprox(second, 20f), $"sides: two hits in one frame must both land on a boss (dealt {first} and {second})");
        Expect(Mathf.IsEqualApprox(hp - (boss.CurrentHP + boss.Shield), 40f), "sides: the boss's HP + shell did not drop by the damage dealt");
        float pushed = boss.Velocity.X;
        // Hit-stop (v0.11.2): the boss holds still for the stop those hits earned and the
        // shove carries it when the stop lets go — so the speed is read then, not on the
        // tick after the hit. The bound is unchanged.
        for (int i = 0; i < 30 && boss.InHitStop; i++) await Ticks(1);
        await Ticks(1);
        pushed = Mathf.Max(pushed, boss.Velocity.X);
        Expect(pushed > 0f && pushed < 600f * def.KnockbackTaken + 60f, $"sides: two 300 px/s shoves moved the boss at {pushed:0} px/s; its poise takes {def.KnockbackTaken:0.0#} of a shove");

        // His head and the player's head are the same scene on different sides. Each head
        // is set down rolling (at rest height, so it does not bounce first) toward its
        // target, which may be walking away at a crab's pace.
        var crab = Spawn<CrabFoe>(stage, "res://Scenes/Foes/CrabFoe.tscn", new Vector2(2400f, FloorY - 22f));
        crab.Init(1);
        await Ticks(2);
        float crabHp = crab.CurrentHp;
        LaunchHead(stage, crab.GlobalPosition + new Vector2(-70f, -0.1f), new Vector2(190f, 0f), CombatSide.Foe);
        await Ticks(120);
        Expect(GodotObject.IsInstanceValid(crab) && Mathf.IsEqualApprox(crab.CurrentHp, crabHp), "sides: a BOSS's head must pass a foe by — it fights for the same side");
        if (GodotObject.IsInstanceValid(crab))
        {
            LaunchHead(stage, crab.GlobalPosition + new Vector2(-70f, -0.1f), new Vector2(190f, 0f), CombatSide.Player);
            for (int i = 0; i < 120 && GodotObject.IsInstanceValid(crab) && crab.CurrentHp >= crabHp; i++) await Ticks(1);
            await Ticks(2);
            Expect(!GodotObject.IsInstanceValid(crab) || crab.CurrentHp < crabHp - 60f,
                "sides: the PLAYER's head must still strike a foe (30 on contact, 50 from the blast)");
        }
        // A boss's head thrown at the player lands; the player's own head rolls past them.
        float beforeHead = player.CurrentHP;
        LaunchHead(stage, player.GlobalPosition + new Vector2(90f, 9.9f), new Vector2(-190f, 0f), CombatSide.Player);
        await Ticks(120);
        Expect(Mathf.IsEqualApprox(player.CurrentHP, beforeHead), "sides: the player's own head must not hurt the player");
        LaunchHead(stage, player.GlobalPosition + new Vector2(90f, 9.9f), new Vector2(-190f, 0f), CombatSide.Foe);
        for (int i = 0; i < 120 && player.CurrentHP >= beforeHead; i++) await Ticks(1);
        Expect(player.CurrentHP < beforeHead, "sides: a BOSS's head must strike the player");
        // What a touch costs is a design number worth knowing (FOES.gdd §8.3): the head deals
        // its contact damage and then goes off, and the player takes BOTH — a hit with no
        // knockback starts no i-frames, and the contact hit carries none. (Measured, because
        // the opposite looked obvious from the code: 80 here, not 30.)
        GD.Print($"  boss     sides          a head that touches the player costs them {beforeHead - player.CurrentHP:0} at ×1: contact and blast both land (a hit with no knockback starts no i-frames)");
        await Ticks(70);                                                       // past the i-frames that hit left

        ICombatTarget victim = player;
        float playerHp = player.CurrentHP;
        float hitA = victim.TakeHit(new HitInfo(10f, new Vector2(-200f, 0f)), boss.GlobalPosition);
        float hitB = victim.TakeHit(new HitInfo(10f, new Vector2(-200f, 0f)), boss.GlobalPosition);
        Expect(hitA > 0f && hitB == 0f && Mathf.IsEqualApprox(playerHp - player.CurrentHP, hitA),
            "sides: the player's post-hit i-frames must still swallow a second hit in the same window");

        // The killing blow usually comes from a projectile, inside a physics callback. The
        // player's own head finishes him here, and he must still fade and free himself.
        target.TakeHit(new HitInfo(boss.CurrentHP + boss.Shield - 10f), player.GlobalPosition);
        LaunchHead(stage, boss.GlobalPosition + new Vector2(-80f, 9.9f), new Vector2(190f, 0f), CombatSide.Player);
        for (int i = 0; i < 180 && GodotObject.IsInstanceValid(boss) && !boss.IsDefeated; i++) await Ticks(1);
        Expect(!GodotObject.IsInstanceValid(boss) || boss.IsDefeated, "sides: the player's head must be able to finish a boss");
        for (int i = 0; i < 150 && GodotObject.IsInstanceValid(boss); i++) await Ticks(1);
        Expect(!GodotObject.IsInstanceValid(boss), "sides: a boss finished by a projectile must fade and free itself");

        await Strike(stage);
        Expect(CharacterController.LocalPlayer == null, "sides: LocalPlayer must clear when the player's body leaves");
    }

    // ---------------------------------------------------------------- 3e. swimming takes sides

    private const string SeaTile = "softvolume.ln_water";

    /// <summary>A body of the shipped sea, built by the factory the arena and the Playtest
    /// build theirs with — the game's own volume scene, collision mask included.</summary>
    private SoftVolume AddSea(Node2D stage, Rect2 rect)
    {
        if (!MapCreation.Data.TileRegistry.TryGet(SeaTile, out MapCreation.Data.TileDef def)) return null;
        var box = new MapCreation.Runtime.MapCollisionSpec(
            new RectangleShape2D { Size = rect.Size }, rect.Size * 0.5f, new Rect2(Vector2.Zero, rect.Size));
        SoftVolume sea = MapCreation.Runtime.MapCollision.CreateSoftVolume(def, false, rect.Position, new[] { box }, "BossFightCheck");
        if (sea != null) stage.AddChild(sea);
        return sea;
    }

    /// <summary>How fast two bodies moved downward over the same `ticks`, in px/s, after
    /// `settle` ticks for whatever was just pressed to take hold.</summary>
    private async Task<(float A, float B)> Sinking(Node2D a, Node2D b, int settle = 20, int ticks = 20)
    {
        await Ticks(settle);
        float fromA = a.GlobalPosition.Y, fromB = b.GlobalPosition.Y;
        await Ticks(ticks);
        return ((a.GlobalPosition.Y - fromA) / (ticks * Tick), (b.GlobalPosition.Y - fromB) / (ticks * Tick));
    }

    /// <summary>
    /// A fighter swims in every soft volume — Up and Down steer it there (v0.11.1, the
    /// v0.10.10 work) — and a BOSS is a fighter. So swimming has to take sides like every
    /// other button: it is read from the body's own <see cref="CharacterController.Controls"/>.
    /// The swimming work was written before sides existed and read the keyboard, which is
    /// exactly how the player's W and S would have swum the boss.
    ///
    /// One sea, the player and a boss side by side in it, his brain asleep so that nothing
    /// is pressed that this probe does not press. Each side's Up and Down must swim that
    /// side — at the speeds the sea's tuning promises — and leave the other sinking at the
    /// sea's drop, as a fighter does when nothing is held.
    /// </summary>
    private async Task ProbeSwimmingTakesSides()
    {
        BossDef def = BossTable.PumpKing;
        Node2D stage = BuildStage(0f, 4000f);
        const float depth = 1700f;
        SoftVolume sea = AddSea(stage, new Rect2(400f, FloorY - depth, 3200f, depth));
        Expect(sea is { IsMedium: true, Swimmable: true } && sea.SinkSpeed > 0f,
            $"swimming: the shipped sea ('{SeaTile}') did not build as a medium with a drop that fighters swim in");
        if (sea == null) { await Strike(stage); return; }

        float middle = FloorY - depth * 0.5f;
        CharacterController player = Spawn<CharacterController>(stage, PlayerScene, new Vector2(1200f, middle));
        CharacterController boss = Spawn<CharacterController>(stage, def.ScenePath, new Vector2(2600f, middle));
        ((IBossFighter)boss).BeginFight(def, 1, new BossArena(new[] { boss.GlobalPosition }, 0));
        BossBrain brain = boss.GetNode<BossBrain>("BossBrain");
        brain.Sleep();                       // before its first decision: he throws nothing, holds nothing
        VirtualPad pad = brain.Pad;          // …and stays on this pad, which the sleeping brain still steps
        Expect(ReferenceEquals(boss.Controls, pad) && player.Controls is PlayerControls,
            "swimming: the boss must read its brain's pad and the player the keyboard");

        // The game registers move_up itself (InputDefaults); this check never does, so a
        // player's controls that stopped registering it fail here, by name.
        var unknown = new List<string>();
        foreach (ControlAction action in Enum.GetValues<ControlAction>())
            if (!InputMap.HasAction(PlayerControls.ActionName(action))) unknown.Add($"{action} ('{PlayerControls.ActionName(action)}')");
        Expect(unknown.Count == 0,
            $"swimming: the player's controls read {string.Join(", ", unknown)}, which the InputMap does not have — the game registers move_up itself (InputDefaults.Ensure)");

        float drop = sea.SinkSpeed;
        float playerSwim = sea.StagnationIndex * player.MoveSpeed, bossSwim = sea.StagnationIndex * boss.MoveSpeed;
        float near = Units.Px(0.3f);
        bool Is(float measured, float expected) => Mathf.Abs(measured - expected) <= near;

        // Nothing held: both float down at the sea's drop — a boss is in the water as the player is.
        (float playerIdle, float bossIdle) = await Sinking(player, boss);
        Expect(Is(playerIdle, drop), $"swimming: left alone in the sea the player moves {Vertical(playerIdle)}; it must float down at the sea's drop, {M(drop)} m/s");
        Expect(Is(bossIdle, drop), $"swimming: left alone in the sea a boss moves {Vertical(bossIdle)}; a fighter on either side floats down at the sea's drop, {M(drop)} m/s");

        // The PLAYER's Up, then Down: the player swims, the boss must go on sinking.
        if (unknown.Count == 0) Input.ActionPress(InputDefaults.MoveUp);
        (float playerUp, float bossUnderPlayersUp) = await Sinking(player, boss);
        if (unknown.Count == 0) Input.ActionRelease(InputDefaults.MoveUp);
        Expect(Is(playerUp, drop - playerSwim), $"swimming: holding Up in the sea the player moves {Vertical(playerUp)}; its swimming speed less the drop is {Vertical(drop - playerSwim)}");
        Expect(Is(bossUnderPlayersUp, drop),
            $"swimming: the boss swam when the PLAYER's Up key was held — he moves {Vertical(bossUnderPlayersUp)} where a body left alone sinks {M(drop)} m/s (a fighter reads Controls, never Input)");

        Input.ActionPress("move_down");
        (float playerDown, float bossUnderPlayersDown) = await Sinking(player, boss);
        Input.ActionRelease("move_down");
        Expect(Is(playerDown, drop + playerSwim), $"swimming: holding Down in the sea the player moves {Vertical(playerDown)}; its swimming speed plus the drop is {Vertical(drop + playerSwim)}");
        Expect(Is(bossUnderPlayersDown, drop),
            $"swimming: the boss dived when the PLAYER's Down key was held — he moves {Vertical(bossUnderPlayersDown)} where a body left alone sinks {M(drop)} m/s (a fighter reads Controls, never Input)");

        // The boss's own pad, Up then Down: he swims, and now the player must go on sinking.
        pad.Hold(ControlAction.MoveUp);
        (float playerUnderHisUp, float bossUp) = await Sinking(player, boss);
        pad.Hold(ControlAction.MoveUp, false);
        Expect(Is(bossUp, drop - bossSwim),
            $"swimming: a boss whose pad holds Up must swim up — he moves {Vertical(bossUp)}; his swimming speed less the drop is {Vertical(drop - bossSwim)}");
        Expect(Is(playerUnderHisUp, drop), $"swimming: the player swam when the BOSS's pad held Up — it moves {Vertical(playerUnderHisUp)} where a body left alone sinks {M(drop)} m/s");

        pad.Hold(ControlAction.MoveDown);
        (float playerUnderHisDown, float bossDown) = await Sinking(player, boss);
        pad.Hold(ControlAction.MoveDown, false);
        Expect(Is(bossDown, drop + bossSwim),
            $"swimming: a boss whose pad holds Down must swim down — he moves {Vertical(bossDown)}; his swimming speed plus the drop is {Vertical(drop + bossSwim)}");
        Expect(Is(playerUnderHisDown, drop), $"swimming: the player dived when the BOSS's pad held Down — it moves {Vertical(playerUnderHisDown)} where a body left alone sinks {M(drop)} m/s");

        GD.Print($"  boss     swimming       in the sea (drop {M(drop)} m/s) the player's Up / Down swim the player {M(-playerUp)} m/s up / {M(playerDown)} down and leave a boss sinking {M(bossUnderPlayersUp)} / {M(bossUnderPlayersDown)} · " +
                 $"his own pad swims him {M(-bossUp)} m/s up / {M(bossDown)} down and leaves the player sinking {M(playerUnderHisUp)} / {M(playerUnderHisDown)}");
        await Strike(stage);
    }

    // ---------------------------------------------------------------- 3f. his rules, one at a time

    private static BossRule Rule(Node boss, string name)
    {
        foreach (BossRule rule in boss.GetNode<BossBrain>("BossBrain").Rules.Rules)
            if (rule.Name == name) return rule;
        return null;
    }

    // ---------------------------------------------------------------- 5. zone 6's own kits (v0.12.1)

    /// <summary>A zone-6 boss on its own kit, its brain asleep: the check presses its buttons.</summary>
    private (CharacterController Boss, VirtualPad Pad) StageAsleep(Node2D stage, BossDef def, Vector2 at)
    {
        CharacterController boss = StageBoss(stage, def, at);
        boss.GetNode<BossBrain>("BossBrain").Sleep();
        return (boss, (VirtualPad)boss.Controls);
    }

    /// <summary>A player that stands where it is put (its own pad, never pressed).</summary>
    private static CharacterController StillPlayer(Node2D stage, float x)
    {
        CharacterController player = Spawn<CharacterController>(stage, PlayerScene, new Vector2(x, FloorY - 32f));
        player.UseControls(new VirtualPad());
        return player;
    }

    /// <summary>
    /// The Ferryman (Zone6.gdd §4.1) on his own kit: the Undertow drags a still player toward
    /// him at its speed and he sweeps at its end; an obol takes its toll of ult charge; the
    /// Skiff Ram waits out its telegraph, rides at its speed and throws up what it hits.
    /// </summary>
    private async Task ProbeTheFerryman()
    {
        BossDef def = BossTable.Ferryman;
        Node2D stage = BuildStage(0f, 6000f);
        (CharacterController boss, VirtualPad pad) = StageAsleep(stage, def, new Vector2(3000f, FloorY - 32f));
        Expect(boss is Ferryman && def.ScenePath.EndsWith("Ferryman.tscn"), "ferryman: the row does not field the Ferryman's own kit");
        var ferryman = boss as Ferryman;
        if (ferryman == null) { await Strike(stage); return; }
        CharacterController player = StillPlayer(stage, 3000f - Units.Px(11f));
        await Ticks(20);

        // Undertow: 2 s of pull measured on a player who does not move.
        float hp0 = player.CurrentHP;
        pad.Facing = -1f;
        pad.Tap(ControlAction.Skill2);
        // A button is read on the body's process frame, which need not fall in the next tick.
        for (int i = 0; i < 30 && !ferryman.Undertowing; i++) await Ticks(1);
        Expect(ferryman.Undertowing, "ferryman: Skill2 did not start the Undertow");
        float x0 = player.GlobalPosition.X;
        await Ticks(120);
        float pulled = (player.GlobalPosition.X - x0) / Units.PixelsPerMeter;
        float expected = Ferryman.UndertowPullMps * 2f;
        Expect(Mathf.Abs(pulled - expected) <= expected * 0.15f, $"ferryman: the Undertow dragged a still player {pulled:0.0} m in 2 s; at {Ferryman.UndertowPullMps} m/s it is {expected:0.0} m");
        for (int i = 0; i < 60 && ferryman.Undertowing; i++) await Ticks(1);
        await Ticks(3);
        Expect(!ferryman.Undertowing && player.CurrentHP < hp0, $"ferryman: the Undertow's end must deliver the player to his oar (HP {hp0:0} → {player.CurrentHP:0})");
        GD.Print($"  boss     ferryman       the Undertow drags a still player {pulled:0.0} m in 2 s (design {expected:0.0}) and ends in a sweep that lands");
        await Strike(stage);

        // The toll: an obol that lands takes 150 of the player's ult charge.
        stage = BuildStage(0f, 6000f);
        (boss, pad) = StageAsleep(stage, def, new Vector2(3000f, FloorY - 32f));
        ferryman = (Ferryman)boss;
        player = StillPlayer(stage, 3000f - Units.Px(8f));
        await Ticks(20);
        player.GainUltCharge(600f);
        float charge = player.UltCharge;
        // His senses feed the throw's reach; the brain is asleep, so take one reading by hand.
        pad.Facing = -1f;
        pad.Aim = player.GlobalPosition;
        boss.GetNode<BossBrain>("BossBrain").Sense?.Invoke(0.1f);
        pad.Tap(ControlAction.Skill1);
        for (int i = 0; i < 120 && Mathf.IsEqualApprox(player.UltCharge, charge); i++) await Ticks(1);
        float toll = charge - player.UltCharge;
        Expect(toll >= Ferryman.ObolUltToll - 0.5f && Mathf.Abs(toll % Ferryman.ObolUltToll) < 0.5f,
            $"ferryman: an obol took {toll:0} ult charge from a player 8 m away; the toll is {Ferryman.ObolUltToll:0} a coin");
        GD.Print($"  boss     ferryman       the obols (a fan at 45°, aimed by range under the place's gravity) take {toll:0} ult charge from a player 8 m away");
        await Strike(stage);

        // The Skiff Ram: below half HP; still through the telegraph, then a ride at its speed.
        stage = BuildStage(0f, 8000f);
        (boss, pad) = StageAsleep(stage, def, new Vector2(2000f, FloorY - 32f));
        ferryman = (Ferryman)boss;
        player = StillPlayer(stage, 2000f + Units.Px(9f));
        await Ticks(20);
        boss.TakeHit(new HitInfo(boss.MaxHP * 0.6f), boss.GlobalPosition + new Vector2(-50f, 0f));
        Expect(ferryman.InPhaseTwo, "ferryman: at 40% HP he is not in phase two");
        boss.GetNode<BossBrain>("BossBrain").Sense?.Invoke(0.1f);
        float startX = boss.GlobalPosition.X;
        hp0 = player.CurrentHP;
        ferryman.BeginRam();
        await Ticks(Mathf.FloorToInt(Ferryman.RamTelegraph * 60f) - 4);
        float duringTelegraph = boss.GlobalPosition.X - startX;
        float speed = 0f, knockUp = 0f;
        for (int i = 0; i < 90 && ferryman.Ramming; i++)
        {
            await Ticks(1);
            speed = Mathf.Max(speed, boss.Velocity.X);
            knockUp = Mathf.Min(knockUp, player.Velocity.Y);
        }
        Expect(Mathf.Abs(duringTelegraph) < Units.Px(0.5f), $"ferryman: he moved {duringTelegraph / Units.PixelsPerMeter:0.0} m during the ram's telegraph");
        Expect(Mathf.Abs(speed / Units.PixelsPerMeter - Ferryman.RamSpeedMps) <= 1.5f, $"ferryman: the skiff rode at {speed / Units.PixelsPerMeter:0.0} m/s; the design is {Ferryman.RamSpeedMps} m/s");
        Expect(player.CurrentHP < hp0 && knockUp < -Units.Px(8f), $"ferryman: the ram must hit the player in its path and throw it up (HP {hp0:0} → {player.CurrentHP:0}, up {-knockUp / Units.PixelsPerMeter:0.0} m/s)");
        GD.Print($"  boss     ferryman       the Skiff Ram holds still through its {Ferryman.RamTelegraph} s telegraph, rides at {speed / Units.PixelsPerMeter:0.0} m/s, throws the player up at {-knockUp / Units.PixelsPerMeter:0.0} m/s");
        await Strike(stage);
    }

    /// <summary>
    /// The Asura of Wrath (Zone6.gdd §5.1) on its own kit: Rising Wrath scales its cooldowns
    /// and attack interval with the HP it has lost; below half it is enraged — the cue, and a
    /// fourth arm in the flurry; the vajra strikes on its way out and again on its way back.
    /// </summary>
    private async Task ProbeTheAsura()
    {
        BossDef def = BossTable.Asura;
        Node2D stage = BuildStage(0f, 6000f);
        (CharacterController boss, VirtualPad pad) = StageAsleep(stage, def, new Vector2(3000f, FloorY - 32f));
        Expect(boss is AsuraWrath && def.ScenePath.EndsWith("AsuraWrath.tscn"), "asura: the row does not field the Asura's own kit");
        var asura = boss as AsuraWrath;
        if (asura == null) { await Strike(stage); return; }
        CharacterController player = StillPlayer(stage, 3000f - Units.Px(2.5f));
        var enrages = new List<Cue>();
        void Heard(Cue cue) { if (cue.Owner == "asura_wrath" && cue.Action == "passive.wrath.enrage") enrages.Add(cue); }
        CueBus.Raised += Heard;
        try
        {
            await Ticks(10);
            float interval = asura.AttackIntervalSeconds;
            Expect(Mathf.IsEqualApprox(boss.CooldownScale, 1f) && !asura.Enraged, "asura: at full HP its wrath must scale nothing");

            // 51%: wrath ×0.804, not yet enraged. A flurry is three arms.
            boss.TakeHit(new HitInfo(boss.MaxHP * 0.49f), boss.GlobalPosition + new Vector2(50f, 0f));
            await Ticks(2);
            Expect(Mathf.IsEqualApprox(boss.CooldownScale, 1f - 0.4f * 0.49f, 1e-3f) && Mathf.IsEqualApprox(asura.AttackIntervalSeconds, interval * (1f - 0.4f * 0.49f), 1e-3f),
                $"asura: at 51% HP Rising Wrath must scale cooldowns and the attack interval ×{1f - 0.4f * 0.49f:0.000} (×{boss.CooldownScale:0.000}, {asura.AttackIntervalSeconds:0.00} s of {interval:0.00} s)");
            Expect(!asura.Enraged && enrages.Count == 0, "asura: enraged above half HP");
            await Ticks(45);                      // past the stun of a blow that size
            pad.Facing = -1f;
            float beforeFlurry = player.CurrentHP;
            pad.Tap(ControlAction.Attack);
            await Ticks(40);
            Expect(asura.LastFlurryHits == AsuraWrath.FlurryHits, $"asura: a flurry at 51% HP was {asura.LastFlurryHits} arms; it is {AsuraWrath.FlurryHits}");
            float flurried = beforeFlurry - player.CurrentHP;
            float flurryWants = AsuraWrath.FlurryHits * AsuraWrath.FlurryDamage * boss.DamageDealtScale;
            Expect(Mathf.IsEqualApprox(flurried, flurryWants, 0.05f),
                $"asura: a flurry dealt {flurried:0.#}; every arm must land ({AsuraWrath.FlurryHits} × {AsuraWrath.FlurryDamage} × {boss.DamageDealtScale:0.00} = {flurryWants:0.#}) — only the last arm shoves (KNOWLEDGE v0.12.3)");

            // 49%: enraged, once; four arms.
            boss.TakeHit(new HitInfo(boss.MaxHP * 0.02f), boss.GlobalPosition + new Vector2(50f, 0f));
            await Ticks(2);
            Expect(asura.Enraged && enrages.Count == 1, $"asura: below half HP it must be enraged and say so once ({enrages.Count} cue(s))");
            await Ticks(Mathf.CeilToInt(asura.AttackIntervalSeconds * 60f) + 20);
            pad.Tap(ControlAction.Attack);
            await Ticks(40);
            Expect(asura.LastFlurryHits == AsuraWrath.EnragedFlurryHits, $"asura: an enraged flurry was {asura.LastFlurryHits} arms; it is {AsuraWrath.EnragedFlurryHits}");
            boss.TakeHit(new HitInfo(boss.MaxHP * 0.24f), boss.GlobalPosition + new Vector2(50f, 0f));
            await Ticks(2);
            Expect(Mathf.IsEqualApprox(boss.CooldownScale, 0.7f, 2e-3f) && enrages.Count == 1, $"asura: at 25% HP wrath must be ×0.70 (×{boss.CooldownScale:0.000}) and the enrage not said again");
            GD.Print($"  boss     asura          Rising Wrath ×1.00 → ×{1f - 0.4f * 0.49f:0.00} at 51% → ×0.70 at 25% on cooldowns and the attack interval · enraged once below 50%: the flurry goes from {AsuraWrath.FlurryHits} arms to {asura.LastFlurryHits}");
        }
        finally
        {
            CueBus.Raised -= Heard;
        }
        await Strike(stage);

        // The vajra: out 12 m and back; a player 4 m away is struck going and coming (the two
        // blows land more than the player's 0.8 s of post-hit grace apart).
        stage = BuildStage(0f, 6000f);
        (boss, pad) = StageAsleep(stage, def, new Vector2(3000f, FloorY - 32f));
        asura = (AsuraWrath)boss;
        player = StillPlayer(stage, 3000f - Units.Px(4f));
        await Ticks(20);
        float hp0 = player.CurrentHP;
        pad.Facing = -1f;
        pad.Tap(ControlAction.Skill2);
        Vajra vajra = null;
        for (int i = 0; i < 10 && vajra == null; i++)
        {
            await Ticks(1);
            foreach (Node child in stage.GetChildren()) if (child is Vajra thrown) vajra = thrown;
        }
        int hitsOut = 0, hitsBack = 0;
        bool turned = false;
        for (int i = 0; i < 180 && vajra != null && GodotObject.IsInstanceValid(vajra); i++)
        {
            hitsOut = vajra.HitsOut;
            hitsBack = vajra.HitsBack;
            turned |= vajra.Returning;
            await Ticks(1);
        }
        Expect(vajra != null && turned, "asura: the vajra was not thrown, or never turned back");
        Expect(hitsOut == 1 && hitsBack == 1, $"asura: the vajra struck a player 4 m away {hitsOut} time(s) going and {hitsBack} coming back; once each way");
        Expect(player.CurrentHP < hp0, "asura: the vajra's blows did no damage");
        GD.Print($"  boss     asura          the vajra flies {AsuraWrath.VajraReachM} m and back: {hitsOut} blow going, {hitsBack} coming back, {hp0 - player.CurrentHP:0} damage");
        await Strike(stage);
    }

    /// <summary>
    /// Māra (Mara.gdd §5–§6) on his own kit, the wheel's realms pushed by hand as the wheel
    /// would push them: Claw Rake's two swipes after its tell, three under Wrath, every one
    /// landing; the Noose yanks and bites a fighter on the floor and passes under one a metre up;
    /// the Hand slams where the player stood and throws them up; Doubt doubles a tell and a
    /// Hand, only one of them real; each realm's arrival — the leap to the throne and back, Wrath
    /// Fire, the Gorge's pull and bite, the Chain Hooks, the Blind Charge and its two images,
    /// which pop without hurting him and go when the realm does.
    /// </summary>
    private async Task ProbeMara()
    {
        BossDef def = BossTable.Mara;
        Node2D stage = BuildStage(0f, 8000f);
        (CharacterController boss, VirtualPad pad) = StageAsleep(stage, def, new Vector2(4000f, FloorY - 32f));
        Expect(boss is Mara && def.ScenePath.EndsWith("Mara.tscn") && def.Signature == WheelTable.Signature, "māra: the row does not field Māra's own kit and his wheel");
        var mara = boss as Mara;
        if (mara == null) { await Strike(stage); return; }
        CharacterController player = StillPlayer(stage, 4000f - Units.Px(3f));
        await Ticks(20);
        float scale = boss.DamageDealtScale;

        // Claw Rake: a tell, then two swipes; only the last shoves, so both land.
        float hp0 = player.CurrentHP;
        pad.Facing = -1f;
        pad.Tap(ControlAction.Attack);
        for (int i = 0; i < 30 && !mara.TellShowing; i++) await Ticks(1);
        bool told = mara.TellShowing && !mara.TellDoubled;
        await Ticks(Mathf.CeilToInt((Mara.ClawTell + Mara.ClawGap) * 60f) + 6);
        float raked = hp0 - player.CurrentHP;
        Expect(told && mara.LastSwipes == Mara.ClawSwipes && Mathf.IsEqualApprox(raked, Mara.ClawSwipes * Mara.ClawDamage * scale, 0.05f),
            $"māra: Claw Rake must show a single tell, then {Mara.ClawSwipes} swipes that both land ({mara.LastSwipes} swipe(s), {raked:0.#} damage; {Mara.ClawSwipes} × {Mara.ClawDamage} × {scale:0.00})");
        // Under Wrath, three.
        mara.RealmLanded(WheelRealm.Asuras, arrival: false);
        await Ticks(Mathf.CeilToInt(boss.AttackIntervalSeconds * 60f) + 40);
        pad.Tap(ControlAction.Attack);
        await Ticks(Mathf.CeilToInt((Mara.ClawTell + 2f * Mara.ClawGap) * 60f) + 10);
        Expect(mara.LastSwipes == Mara.WrathClawSwipes, $"māra: under Wrath a rake must be {Mara.WrathClawSwipes} swipes, not {mara.LastSwipes}");
        GD.Print($"  boss     māra           Claw Rake: a {Mara.ClawTell} s tell, {Mara.ClawSwipes} swipes for {raked:0.#} at level 1 (both land: only the last shoves) · {mara.LastSwipes} under Wrath");
        await Strike(stage);

        // The Noose: on the floor it catches, yanks and bites; a metre up it passes under.
        stage = BuildStage(0f, 8000f);
        (boss, pad) = StageAsleep(stage, def, new Vector2(4000f, FloorY - 32f));
        mara = (Mara)boss;
        player = StillPlayer(stage, 4000f - Units.Px(9f));
        AddBox(stage, new Rect2(4000f + Units.Px(8f), FloorY - Units.Px(1f), Units.Px(2f), Units.Px(1f)));
        CharacterController above = StillPlayer(stage, 4000f + Units.Px(9f));
        above.GlobalPosition = new Vector2(4000f + Units.Px(9f), FloorY - Units.Px(1f) - 32f);
        await Ticks(20);
        hp0 = player.CurrentHP;
        float aboveHp = above.CurrentHP;
        float x0 = player.GlobalPosition.X;
        pad.Facing = -1f;
        pad.Tap(ControlAction.Skill1);
        bool yanked = false;
        for (int i = 0; i < 120 && player.CurrentHP >= hp0 - 0.01f; i++)
        {
            await Ticks(1);
            yanked |= mara.Yanking;
        }
        float pulled = (player.GlobalPosition.X - x0) / Units.PixelsPerMeter;
        Expect(yanked && pulled > 4f && Mathf.IsEqualApprox(hp0 - player.CurrentHP, Mara.NooseDamage * scale, 0.05f),
            $"māra: the Noose must catch a fighter on the floor 9 m away, yank them to his claws and bite for {Mara.NooseDamage * scale:0.#} (yanked {yanked}, pulled {pulled:0.0} m, {hp0 - player.CurrentHP:0.#} damage)");
        // The other way, at the fighter a metre up: the noose passes under.
        await Ticks(Mathf.CeilToInt(Mara.NooseCooldown * 60f) + 10);
        pad.Facing = 1f;
        pad.Tap(ControlAction.Skill1);
        MaraNoose thrown = null;
        for (int i = 0; i < 60 && thrown == null; i++)
        {
            await Ticks(1);
            foreach (Node child in stage.GetChildren()) if (child is MaraNoose noose && noose.Position.X > 4000f) thrown = noose;
        }
        for (int i = 0; i < 90 && thrown != null && GodotObject.IsInstanceValid(thrown); i++) await Ticks(1);
        Expect(thrown != null && Mathf.IsEqualApprox(above.CurrentHP, aboveHp) && !mara.Yanking, $"māra: a fighter a metre up must clear the Noose ({aboveHp:0} → {above.CurrentHP:0})");
        GD.Print($"  boss     māra           the Noose catches a fighter 9 m away on the floor, drags them {pulled:0.0} m in {Mara.NooseYankSeconds} s and bites for {hp0 - player.CurrentHP:0.#}; a metre up it passes under");
        await Strike(stage);

        // The Hand of the Wheel: where the player stood, after a 1 s shadow — a slam and a throw up.
        stage = BuildStage(0f, 8000f);
        (boss, pad) = StageAsleep(stage, def, new Vector2(4000f, FloorY - 32f));
        mara = (Mara)boss;
        player = StillPlayer(stage, 4000f - Units.Px(7f));
        await Ticks(20);
        mara.RealmLanded(WheelRealm.Animals, arrival: false);
        mara.RealmLanded(WheelRealm.Hell, arrival: false);     // Hell, as the fight opens: Doubt holds
        mara.RealmLanded(WheelRealm.Asuras, arrival: false);   // …and then not: one plain Hand
        hp0 = player.CurrentHP;
        pad.Tap(ControlAction.Skill2);
        MaraStrike hand = null;
        for (int i = 0; i < 30 && hand == null; i++)
        {
            await Ticks(1);
            foreach (Node child in stage.GetChildren()) if (child is MaraStrike { Kind: MaraStrikeKind.Hand } strike) hand = strike;
        }
        bool offWheel = mara.HandOffWheel;
        float handX = hand != null ? hand.GlobalPosition.X : 0f;
        float knockUp = 0f;
        for (int i = 0; i < 120 && player.CurrentHP >= hp0 - 0.01f; i++)
        {
            await Ticks(1);
            knockUp = Mathf.Min(knockUp, player.Velocity.Y);
        }
        for (int i = 0; i < 10; i++) { await Ticks(1); knockUp = Mathf.Min(knockUp, player.Velocity.Y); }
        Expect(hand != null && offWheel && Mathf.Abs(handX - (4000f - Units.Px(7f))) < 2f && Mathf.IsEqualApprox(hp0 - player.CurrentHP, Mara.HandDamage * scale, 0.05f)
               && knockUp < -Units.Px(8f),
            $"māra: the Hand must fall where the player stood, hold the wheel, deal {Mara.HandDamage * scale:0.#} and throw them up (dealt {hp0 - player.CurrentHP:0.#}, up {-knockUp / Units.PixelsPerMeter:0.0} m/s)");

        // Doubt: a tell and a Hand each come in a pair; only one of the pair is real.
        mara.RealmLanded(WheelRealm.Hell, arrival: false);
        await Ticks(Mathf.CeilToInt(boss.AttackIntervalSeconds * 60f) + 30);
        pad.Facing = -1f;
        pad.Tap(ControlAction.Attack);
        for (int i = 0; i < 30 && !mara.TellShowing; i++) await Ticks(1);
        bool doubledTell = mara.TellShowing && mara.TellDoubled;
        await Ticks(60);
        for (int i = 0; i < 900 && mara.ESkillCooldown.Remaining > 0f; i++) await Ticks(1);   // the first Hand's cooldown
        pad.Tap(ControlAction.Skill2);
        var pair = new List<MaraStrike>();
        for (int i = 0; i < 30 && pair.Count < 2; i++)
        {
            await Ticks(1);
            pair.Clear();
            foreach (Node child in stage.GetChildren()) if (child is MaraStrike { Kind: MaraStrikeKind.Hand } strike && !strike.Struck) pair.Add(strike);
        }
        int real = pair.FindAll(strike => strike.Real).Count;
        MaraStrike falseHand = pair.Find(strike => !strike.Real);
        await Ticks(Mathf.CeilToInt(Mara.HandTell * 60f) + 6);
        Expect(doubledTell && pair.Count == 2 && real == 1 && (falseHand == null || !GodotObject.IsInstanceValid(falseHand)),
            $"māra: under Doubt a tell must come doubled ({doubledTell}) and the Hand in a pair, one real ({pair.Count} hands, {real} real), the false one gone before the slam");
        GD.Print($"  boss     māra           the Hand of the Wheel falls where the player stood after {Mara.HandTell} s: {Mara.HandDamage * scale:0.#} and a throw up at {-knockUp / Units.PixelsPerMeter:0.0} m/s · under Doubt a tell and a Hand come in pairs, one real");
        await Strike(stage);

        // The realms' arrivals.
        stage = BuildStage(0f, 8000f);
        var perch = new Vector2(4000f, FloorY - 32f);
        (boss, pad) = StageAsleep(stage, def, perch);
        mara = (Mara)boss;
        player = StillPlayer(stage, 4000f - Units.Px(8f));
        boss.GlobalPosition = perch + new Vector2(Units.Px(6f), 0f);
        await Ticks(20);
        Vector2 floorSpot = boss.GlobalPosition;
        mara.RealmLanded(WheelRealm.Gods, arrival: true);
        bool leapt = mara.Leaping;
        await Ticks(Mathf.CeilToInt(Mara.ThroneLeapSeconds * 60f) + 8);
        bool throned = mara.OnThrone && boss.GlobalPosition.DistanceTo(perch) < 6f;
        mara.RealmLanded(WheelRealm.Asuras, arrival: true);
        int waves = 0;
        foreach (Node child in stage.GetChildren()) if (child is WrathFireWave) waves++;
        await Ticks(Mathf.CeilToInt(Mara.ThroneLeapSeconds * 60f) + 8);
        bool down = !mara.OnThrone && boss.GlobalPosition.DistanceTo(floorSpot) < 6f;
        Expect(leapt && throned && down, $"māra: the gods' realm must take him onto the throne (his perch) and the next realm back down (leapt {leapt}, throned {throned}, back {down})");
        Expect(waves == 2, $"māra: the asuras' arrival must be Wrath Fire, two waves ({waves})");

        // Gorge: a player 8 m off is pulled in and bitten.
        await Ticks(90);
        player.GlobalPosition = new Vector2(boss.GlobalPosition.X - Units.Px(8f), FloorY - 32f);
        await Ticks(5);
        hp0 = player.CurrentHP;
        x0 = player.GlobalPosition.X;
        mara.RealmLanded(WheelRealm.HungryGhosts, arrival: true);
        bool gorging = mara.Gorging;
        await Ticks(Mathf.CeilToInt(Mara.GorgeSeconds * 60f) + 6);
        float drawn = (player.GlobalPosition.X - x0) / Units.PixelsPerMeter;
        Expect(gorging && drawn > 5f && player.CurrentHP < hp0, $"māra: the Gorge must pull a player 8 m off toward him and bite (pulled {drawn:0.0} m, HP {hp0:0} → {player.CurrentHP:0})");

        // Chain Hooks: two, one real.
        await Ticks(60);
        mara.RealmLanded(WheelRealm.Hell, arrival: true);
        var hooks = new List<ChainHook>();
        foreach (Node child in stage.GetChildren()) if (child is ChainHook hook) hooks.Add(hook);
        Expect(hooks.Count == 2 && hooks.FindAll(hook => hook.Real).Count == 1, $"māra: hell's arrival must be two Chain Hooks, one real ({hooks.Count}, {hooks.FindAll(hook => hook.Real).Count} real)");

        // Blind Charge: he charges with two images; they stay, pop without hurting him, and go with the realm.
        await Ticks(90);
        mara.RealmLanded(WheelRealm.Animals, arrival: true);
        bool charging = mara.Charging;
        float chargeSpeed = 0f;
        for (int i = 0; i < 30; i++)
        {
            await Ticks(1);
            chargeSpeed = Mathf.Max(chargeSpeed, Mathf.Abs(boss.Velocity.X));
        }
        for (int i = 0; i < 120 && mara.Charging; i++) await Ticks(1);
        int images = 0;
        foreach (MaraImage image in mara.Images) if (GodotObject.IsInstanceValid(image) && !image.Popped) images++;
        MaraImage first = mara.Images.Count > 0 ? mara.Images[0] : null;
        float bossHp = boss.CurrentHP;
        float onImage = first != null ? first.TakeHit(new HitInfo(500f), first.GlobalPosition) : -1f;
        bool popped = first != null && first.Popped;
        Expect(charging && Mathf.Abs(chargeSpeed / Units.PixelsPerMeter - Mara.ChargeSpeedMps) <= 1.5f && images == Mara.ImageCount,
            $"māra: the animals' arrival must be a Blind Charge at {Mara.ChargeSpeedMps} m/s ({chargeSpeed / Units.PixelsPerMeter:0.0}) leaving {Mara.ImageCount} images ({images})");
        Expect(onImage == 0f && popped && Mathf.IsEqualApprox(boss.CurrentHP, bossHp), $"māra: a blow on an image must pop it and hurt nobody (dealt {onImage}, popped {popped})");
        await Ticks(Mathf.CeilToInt(Mara.ImageReform * 60f) + 10);
        bool reformed = first != null && GodotObject.IsInstanceValid(first) && !first.Popped;
        mara.RealmLanded(WheelRealm.Humans, arrival: true);
        await Ticks(2);
        Expect(reformed && mara.Images.Count == 0, $"māra: a popped image must reform after {Mara.ImageReform} s ({reformed}) and the images go with the animals' realm ({mara.Images.Count} left)");
        GD.Print($"  boss     māra           arrivals: onto the throne and back · Wrath Fire ×{waves} · the Gorge pulls {drawn:0.0} m and bites · {hooks.Count} Chain Hooks, one real · a Blind Charge at {chargeSpeed / Units.PixelsPerMeter:0.0} m/s with {images} images that pop and reform");
        await Strike(stage);
    }

    private static CharacterController StageBoss(Node2D stage, BossDef def, Vector2 perch)
    {
        CharacterController boss = Spawn<CharacterController>(stage, def.ScenePath, perch);
        ((IBossFighter)boss).BeginFight(def, 1, new BossArena(new[] { perch }, 0));
        return boss;
    }

    /// <summary>A thin one-way ledge, like the balcony: stood on from above, jumped through
    /// from below.</summary>
    private static void AddLedge(Node2D stage, Rect2 rect)
    {
        var body = new StaticBody2D { Position = rect.GetCenter(), CollisionLayer = 0, CollisionMask = 0 };
        body.SetCollisionLayerValue(Units.LayerPlatform, true);
        body.AddChild(new CollisionShape2D { Shape = new RectangleShape2D { Size = rect.Size }, OneWayCollision = true });
        stage.AddChild(body);
    }

    /// <summary>
    /// BOSS PumpKing's rules, each given the one situation it exists for. The fight probe
    /// shows them working together on his own map, where a rule that never fires can hide
    /// behind another that happens to cover for it; here each has to do its own job:
    /// his legs take him back to his perch (on the level, and up onto a ledge he was knocked
    /// off), he turns to face the player, sets his head off when it is beside them, and
    /// sets it off when it has rolled on past them so that he gets it back.
    /// </summary>
    private async Task ProbeHisRules()
    {
        BossDef def = BossTable.PumpKing;
        var perch = new Vector2(3000f, FloorY - 32f);

        // --- hold perch, on the level: set down 5 m away he walks back and stays.
        Node2D stage = BuildStage(0f, 6000f);
        CharacterController boss = StageBoss(stage, def, perch);
        await Ticks(20);
        boss.GlobalPosition = perch + new Vector2(-Units.Px(5f), 0f);
        int walked = -1;
        for (int i = 0; i < 60 * 4 && walked < 0; i++)
        {
            await Ticks(1);
            if (Mathf.Abs(boss.GlobalPosition.X - perch.X) < Units.Px(1f)) walked = i + 1;
        }
        Expect(walked >= 0, $"his rules: set down 5 m from his perch the boss did not walk back (he stands {M(Mathf.Abs(boss.GlobalPosition.X - perch.X))} m off it)");
        await Ticks(60);
        float wander = 0f;
        for (int i = 0; i < 120; i++)
        {
            await Ticks(1);
            wander = Mathf.Max(wander, Mathf.Abs(boss.GlobalPosition.X - perch.X));
        }
        Expect(wander < Units.Px(1.5f), $"his rules: back on his perch the boss kept wandering {M(wander)} m off it; he must stand to throw");
        await Strike(stage);

        // --- hold perch, from below: knocked off a ledge 5 m up, he walks under it and jumps back on.
        stage = BuildStage(0f, 6000f);
        float ledgeTop = FloorY - Units.Px(5f);
        AddLedge(stage, new Rect2(perch.X - Units.Px(3f), ledgeTop, Units.Px(6f), 12f));
        var high = new Vector2(perch.X, ledgeTop - 32f);
        boss = StageBoss(stage, def, high);
        await Ticks(20);
        Expect(boss.IsOnFloor() && boss.GlobalPosition.DistanceTo(high) < Units.Px(0.5f), "his rules: the boss does not stand on a one-way ledge");
        boss.GlobalPosition = new Vector2(high.X - Units.Px(6f), FloorY - 32f);
        int climbed = -1;
        for (int i = 0; i < 60 * 6 && climbed < 0; i++)
        {
            await Ticks(1);
            if (boss.IsOnFloor() && boss.GlobalPosition.DistanceTo(high) < Units.Px(1f)) climbed = i + 1;
        }
        Expect(climbed >= 0, $"his rules: knocked 5 m down off his perch the boss did not get back onto it (he is {M(boss.GlobalPosition.DistanceTo(high))} m from it)");
        await Strike(stage);

        // --- detonate: a head that is beside the player, and cannot touch them, he sets off.
        // A wall stands at the player's shoulder, between them: nothing he throws reaches
        // their body, so the only way that head hurts them is the button.
        stage = BuildStage(0f, 6000f);
        Vector2 stand = perch + new Vector2(-Units.Px(6f), 0f);
        AddBox(stage, new Rect2(stand.X + 28f, FloorY - Units.Px(6f), 12f, Units.Px(6f)));
        CharacterController player = Spawn<CharacterController>(stage, PlayerScene, stand);
        boss = StageBoss(stage, def, perch);
        var pk = (PumpKing)boss;
        await Ticks(20);
        Expect(boss.FacingDirection.X < 0f, "his rules: on his perch the boss must turn to face a player on his left");

        BossRule detonate = Rule(boss, "detonate");
        float hp = player.CurrentHP;
        Vector2 headAt = boss.GlobalPosition;
        bool setOff = false;
        for (int i = 0; i < 60 * 12 && !setOff; i++)
        {
            if (pk.HasLiveHead) headAt = pk.LiveHeadPosition;
            await Ticks(1);
            setOff = detonate != null && detonate.Starts > 0;
        }
        Expect(setOff, "his rules: his head lay beside the player and the boss never set it off (the 'detonate' rule)");
        for (int i = 0; i < 3 && pk.HasLiveHead; i++) await Ticks(1);
        Expect(!setOff || !pk.HasLiveHead, "his rules: the boss pressed the button and his head did not go off");
        float cost = hp - player.CurrentHP;
        Expect(!setOff || cost > 0f, $"his rules: the head he set off {M(headAt.DistanceTo(player.GlobalPosition))} m from the player did not hurt them");
        float setOffAt = headAt.DistanceTo(player.GlobalPosition);
        await Strike(stage);

        // --- call back: the player gets behind him while his head is on its way; a head
        // rolling away from them is no use, so he sets it off and has it back sooner.
        stage = BuildStage(0f, 6000f);
        player = Spawn<CharacterController>(stage, PlayerScene, perch + new Vector2(-Units.Px(12f), 0f));
        boss = StageBoss(stage, def, perch);
        pk = (PumpKing)boss;
        BossRule callBack = Rule(boss, "call back");
        for (int i = 0; i < 60 * 8 && !pk.HasLiveHead; i++) await Ticks(1);
        Expect(pk.HasLiveHead, "his rules: with the player 12 m away the boss never threw his head");

        player.GlobalPosition = perch + new Vector2(Units.Px(7f), 0f);
        hp = player.CurrentHP;
        int away = 0;
        bool called = false;
        for (; away < 60 * 8 && pk.HasLiveHead && !called; away++)
        {
            await Ticks(1);
            called = callBack != null && callBack.Starts > 0;
        }
        bool wentByItself = !called && !pk.HasLiveHead;
        Expect(called, wentByItself
            ? $"his rules: his head rolled away from the player for {away * Tick:0.0} s and went off by itself; he must call it back (the 'call back' rule)"
            : "his rules: his head rolled away from the player and the boss never called it back (the 'call back' rule)");
        for (int i = 0; i < 3 && pk.HasLiveHead; i++) await Ticks(1);
        Expect(!called || !pk.HasLiveHead, "his rules: the boss called his head back and it did not go off");
        Expect(Mathf.IsEqualApprox(player.CurrentHP, hp), "his rules: a head called back from the far side of the boss must not hurt the player");
        Expect(boss.FacingDirection.X > 0f, "his rules: the boss must turn to face a player who got behind him");
        player.Invincible = true;                                           // what he throws next is not this probe's subject
        int reload = 0;
        for (; reload < 60 * 4 && !pk.HeadReady; reload++) await Ticks(1);
        Expect(pk.HeadReady, "his rules: a head called back must reload");
        await Strike(stage);

        GD.Print($"  boss     his rules      walks 5 m back to his perch in {walked * Tick:0.0} s and stands within {M(wander)} m of it · jumps back onto a ledge 5 m up in {climbed * Tick:0.0} s · sets his head off {M(setOffAt)} m from a player it cannot touch, costing them {cost:0} HP · calls back a head rolling away after {away * Tick:0.0} s and has it again {reload * Tick:0.0} s later");
    }

    /// <summary>The map a run's selection deals this boss's 4-1, from the shipped maps
    /// alone — like the content check's coverage walk, this check's answer must not depend
    /// on what happens to sit in one developer's <c>user://maps</c>.</summary>
    private static MapCreation.Runtime.CombatMapSelection ShippedArena(BossDef def)
    {
        List<MapCreation.Runtime.CombatMapSelection> dealt = MapCreation.Runtime.CombatMapCatalog.CandidatesFor(
            def.WorldAbbr, 4, MissionType.Boss, MapCreation.Data.CombatMapTerrain.SeaLevel, includeUserMaps: false);
        return dealt.Count > 0 ? dealt[0] : null;
    }

    /// <summary>Where the boss's arena slopes: the columns (px) its ramp tiles span, read
    /// from the same map the run's selection deals the boss.</summary>
    private static (float From, float To) SlopeSpan(BossDef def)
    {
        MapCreation.Runtime.CombatMapSelection map = ShippedArena(def);
        MapCreation.Data.MapLayerData field = MapCreation.Data.MapDocumentQueries.Battlefield(map?.Document);
        float from = float.MaxValue, to = float.MinValue;
        if (field?.Tiles == null) return (0f, 0f);
        foreach (MapCreation.Data.PlacedTile tile in field.Tiles)
        {
            if (!MapCreation.Data.TileRegistry.TryGet(tile.DefId, out MapCreation.Data.TileDef kind)) continue;
            if (kind.Category != MapCreation.Data.TileCategory.Ground || kind.EffectArea?.Kind != MapCreation.Data.ShapeDef.KindPolygon) continue;
            from = Mathf.Min(from, tile.X * MapCreation.Data.MapGrid.PixelsPerCell);
            to = Mathf.Max(to, (tile.X + kind.FootprintW) * MapCreation.Data.MapGrid.PixelsPerCell);
        }
        return to > from ? (from, to) : (0f, 0f);
    }

    // ---------------------------------------------------------------- 4. the fight

    /// <summary>The whole thing, through the real Arena: staged the way debug mode stages a
    /// boss, on the map the run's selection deals to that boss's world.</summary>
    private async Task ProbeTheFight()
    {
        BossDef def = BossTable.PumpKing;

        // What a run would deal his 4-1: his own arena, ahead of the realm's ordinary map.
        MapCreation.Runtime.CombatMapSelection dealt = ShippedArena(def);
        Expect(dealt != null && dealt.Document.Goals.Contains(MapCreation.Data.CombatMapGoals.Boss),
            $"the fight: a Boss mission in {def.WorldAbbr} must be dealt a boss arena (goal 'boss'), not '{dealt?.Document.Name ?? "nothing"}'");
        foreach (MissionType ordinary in new[] { MissionType.Collection, MissionType.Protect, MissionType.Destroy, MissionType.Slaughter })
        {
            foreach (MapCreation.Runtime.CombatMapSelection map in MapCreation.Runtime.CombatMapCatalog.CandidatesFor(
                         def.WorldAbbr, 4, ordinary, MapCreation.Data.CombatMapTerrain.SeaLevel, includeUserMaps: false))
                Expect(!map.Document.Goals.Contains(MapCreation.Data.CombatMapGoals.Boss) || map.Document.Goals.Count > 1,
                    $"the fight: a {ordinary} mission must never be dealt the boss arena '{map.Document.Name}'");
        }
        if (dealt == null) return;

        // The Arena's own debug knobs (the ones `PumpKingBossFight.tscn` presets), with the
        // dealt map named so the fight does not read this machine's user://maps.
        var arena = ResourceLoader.Load<PackedScene>("res://Scenes/Arena.tscn").Instantiate<GameManager>();
        arena.DebugMission = MissionType.Boss;
        arena.DebugNodeLevel = 4;
        arena.DebugBossId = def.Id;
        arena.DebugCombatMapPath = dealt.AbsolutePath;
        arena.PreCombatCountdownSeconds = 0.2f;
        AddChild(arena);

        await Ticks(60);
        CharacterController boss = null;
        foreach (Node node in GetTree().GetNodesInGroup(CombatTargets.EnemyGroup))
            if (node is CharacterController { Side: CombatSide.Foe } found) boss = found;
        CharacterController player = CharacterController.LocalPlayer;

        Expect(arena.BossId == def.Id, $"the fight: the arena fields '{arena.BossId}', not the staged boss");
        Expect(arena.LevelGoalPoints.Count >= 2, $"the fight: the boss arena has {arena.LevelGoalPoints.Count} perch(es); the boss needs two to change ground");
        Expect(boss != null, "the fight: no boss was staged (was the placeholder fielded? is there a boss arena for its world?)");
        Expect(player != null, "the fight: the arena has no player");
        if (boss == null || player == null)
        {
            arena.QueueFree();
            await Ticks(2);
            return;
        }

        // He opens on the perch farthest from the player, above them.
        Vector2 open = boss.GlobalPosition;
        float farthest = 0f;
        foreach (Vector2 perch in arena.LevelGoalPoints) farthest = Mathf.Max(farthest, perch.DistanceTo(player.GlobalPosition));
        Expect(Mathf.Abs(open.DistanceTo(player.GlobalPosition) - farthest) < Units.Px(1.5f), "the fight: the boss must open on the perch farthest from the player");
        float above = player.GlobalPosition.Y - open.Y;
        Expect(above > Units.Px(6f),
            $"the fight: the boss opens {M(above)} m above the player; his arena is meant to give him the high ground");

        // The player stands still at the foot of the slope. The boss must pump, bowl, and the
        // head must come down the slope and hurt them.
        float playerHp = player.CurrentHP;
        int throws = 0, maxStacks = 0;
        bool headOut = false;
        float headLowest = open.Y, headFarthest = 0f;
        var pk = (PumpKing)boss;
        for (int i = 0; i < 60 * 30 && player.CurrentHP >= playerHp; i++)
        {
            await Ticks(1);
            if (!GodotObject.IsInstanceValid(boss) || !GodotObject.IsInstanceValid(player)) break;
            maxStacks = Mathf.Max(maxStacks, pk.PumpStacks);
            if (pk.HasLiveHead)
            {
                if (!headOut) throws++;
                headOut = true;
                headLowest = Mathf.Max(headLowest, pk.LiveHeadPosition.Y);
                headFarthest = Mathf.Max(headFarthest, Mathf.Abs(pk.LiveHeadPosition.X - open.X));
            }
            else headOut = false;
        }
        Expect(throws >= 1, "the fight: in 30 s the boss never threw his head");
        Expect(maxStacks >= 1, "the fight: with the player far away the boss never pumped up");
        Expect(headFarthest > Units.Px(20f), $"the fight: his head got {M(headFarthest)} m from his perch; down the slope it must go far beyond a flat throw");
        Expect(headLowest > open.Y + Units.Px(8f), $"the fight: his head came down {M(headLowest - open.Y)} m; it must run the slope");
        Expect(GodotObject.IsInstanceValid(player) && player.CurrentHP < playerHp, "the fight: a player standing still at the foot of the slope was never hurt by a head");
        float hurt = GodotObject.IsInstanceValid(player) ? playerHp - player.CurrentHP : 0f;

        // Walk the arena's real slope — tile blocks, joints and all — from its foot to its
        // top: every joint must be walked over without a stumble, at the climbing speed.
        player.Invincible = true;                                           // heads keep coming; this is about feet
        (float slopeFrom, float slopeTo) = SlopeSpan(def);
        Expect(slopeTo > slopeFrom, "the fight: the boss arena has no slope (a Ground tile with a ramp for its top)");
        if (slopeTo > slopeFrom)
        {
            player.GlobalPosition = new Vector2(slopeFrom - 96f, player.GlobalPosition.Y);
            await Ticks(20);
            Input.ActionPress("move_right");
            int climbTicks = 0, climbOff = 0;
            for (int i = 0; i < 60 * 20 && player.GlobalPosition.X < slopeTo + 32f; i++)
            {
                await Ticks(1);
                if (player.GlobalPosition.X < slopeFrom + 48f || player.GlobalPosition.X > slopeTo - 48f) continue;
                climbTicks++;
                if (!player.IsOnFloor()) climbOff++;
            }
            Input.ActionRelease("move_right");
            float climbSpeed = climbTicks > 0 ? (slopeTo - slopeFrom - 96f) / (climbTicks * Tick) : 0f;
            Expect(player.GlobalPosition.X >= slopeTo, "the fight: the player could not walk up the arena's slope");
            Expect(climbOff == 0, $"the fight: climbing the tile slope the player left the floor on {climbOff} of {climbTicks} ticks (a joint between blocks?)");
            Expect(Mathf.Abs(climbSpeed - player.MoveSpeed * 0.8f) < player.MoveSpeed * 0.06f,
                $"the fight: the player climbed the tile slope at {M(climbSpeed)} m/s across; a 1:2 ramp gives {M(player.MoveSpeed * 0.8f)}");
            GD.Print($"  boss     the slope      {M(slopeTo - slopeFrom)} m of tile slope climbed at {M(climbSpeed)} m/s across, feet on it every tick");
        }

        // Get in his face on the balcony: he must leave in Soul Form for his other perch,
        // land on it and go on fighting from there; pressed again, he comes back.
        var home = boss.GlobalPosition;
        float flights = 0f;
        for (int trip = 0; trip < 2 && GodotObject.IsInstanceValid(boss); trip++)
        {
            Vector2 from = boss.GlobalPosition;
            bool flew = false, landed = false;
            int ticks = 0;
            for (; ticks < 60 * 22; ticks++)
            {
                // Stay beside him until he takes off, then stand still and watch.
                if (!flew) player.GlobalPosition = boss.GlobalPosition + new Vector2(40f, 0f);
                await Ticks(1);
                if (pk.CurrentState == PumpKing.PKState.Soul) flew = true;
                if (flew && pk.CurrentState != PumpKing.PKState.Soul && boss.IsOnFloor())
                {
                    landed = true;
                    break;
                }
            }
            float moved = boss.GlobalPosition.DistanceTo(from);
            float offPerch = float.MaxValue;
            foreach (Vector2 perch in arena.LevelGoalPoints) offPerch = Mathf.Min(offPerch, boss.GlobalPosition.DistanceTo(perch));
            Expect(flew, $"the fight: with the player beside him the boss never took to Soul Form (trip {trip + 1})");
            Expect(landed && moved > Units.Px(10f), $"the fight: his Soul Form flight moved him {M(moved)} m; he must change perch (trip {trip + 1})");
            Expect(landed && offPerch < Units.Px(1.5f), $"the fight: after Soul Form he stood {M(offPerch)} m from the nearest perch (trip {trip + 1})");
            flights = moved;
            if (!flew || !landed) break;
            await Ticks(40);
        }
        Expect(boss.GlobalPosition.DistanceTo(home) < Units.Px(1.5f), "the fight: pressed off his second perch, the boss must come back to the first");
        GD.Print($"  boss     soul form      pressed on his perch he flies {M(flights)} m to the other and lands on it · pressed there, he flies back");
        player.Invincible = false;

        // Beating him resolves the mission and recruits him.
        ICombatTarget target = boss;
        for (int i = 0; i < 400 && GodotObject.IsInstanceValid(boss) && !boss.IsDefeated; i++)
        {
            target.TakeHit(new HitInfo(60f), player.GlobalPosition);
            await Ticks(2);
        }
        Expect(!GodotObject.IsInstanceValid(boss) || boss.IsDefeated, "the fight: the boss could not be beaten");
        await Ticks(30);
        // A debug arena pauses the tree under its win banner; a run does not. Let the
        // boss's own exit play out as it would in a run.
        GetTree().Paused = false;
        var mission = arena.CurrentMission as BossMission;
        Expect(mission != null && mission.Status == MissionStatus.Succeeded, "the fight: beating the boss did not resolve the mission");
        Expect(mission != null && mission.FieldsRealBoss && mission.Reward().ProtagonistGrants.Contains(def.ProtagonistId),
            $"the fight: beating the boss must recruit {def.ProtagonistId}");
        await Ticks(90);
        Expect(!GodotObject.IsInstanceValid(boss), "the fight: a beaten boss must fade and free itself");

        GD.Print($"  boss     the fight      opens {M(above)} m above the player on perch 1 of {arena.LevelGoalPoints.Count} · pumped to {maxStacks} · {throws} head(s) in the time it took to land one · head ran {M(headFarthest)} m out and {M(headLowest - open.Y)} m down · first hit cost the player {hurt:0} HP");

        GetTree().Paused = false;
        arena.QueueFree();
        await Ticks(2);
    }

    // ---------------------------------------------------------------- 6. the Black Prison (PumpKing's ultimate)

    private const string PumpKingScene = "res://Scenes/PumpKing.tscn";
    private const string CrabScene = "res://Scenes/Foes/CrabFoe.tscn";

    /// <summary>PumpKing on the player's side, on a pad this check presses.</summary>
    private (PumpKing King, VirtualPad Pad) PlayerPumpKing(Node2D stage, float x)
    {
        PumpKing king = Spawn<PumpKing>(stage, PumpKingScene, new Vector2(x, FloorY - 32f));
        var pad = new VirtualPad();
        king.UseControls(pad);
        _pads.Add(pad);
        return (king, pad);
    }

    /// <summary>The player's usual body on a pad this check presses.</summary>
    private CharacterController PadPlayer(Node2D stage, float x, out VirtualPad pad)
    {
        CharacterController player = Spawn<CharacterController>(stage, PlayerScene, new Vector2(x, FloorY - 32f));
        pad = new VirtualPad();
        player.UseControls(pad);
        _pads.Add(pad);
        return player;
    }

    /// <summary>A crab at rest on the floor whose first step is seeded (its Rng picks it in
    /// _Ready), sturdy enough that no blow of a probe ends it.</summary>
    private static CrabFoe Crab(Node2D stage, float x, int level, ulong seed)
    {
        var crab = ResourceLoader.Load<PackedScene>(CrabScene).Instantiate<CrabFoe>();
        crab.Rng = new RandomNumberGenerator { Seed = seed };
        stage.AddChild(crab);
        crab.GlobalPosition = new Vector2(x, FloorY - 22f);
        crab.Init(level);
        crab.OverrideMaxHp(5000f);
        return crab;
    }

    /// <summary>Two walls standing on the floor, so a walker stays where it is measured.</summary>
    private static void Pen(Node2D stage, float left, float right)
    {
        AddBox(stage, new Rect2(left - 20f, FloorY - 120f, 20f, 120f));
        AddBox(stage, new Rect2(right, FloorY - 120f, 20f, 120f));
    }

    /// <summary>The fastest a body went across over the next ticks, px/s. A crab turning at a
    /// wall or held by hit-stop is slower for a tick; the top is its walking speed.</summary>
    private async Task<float> TopSpeedX(CharacterBody2D body, int ticks)
    {
        float top = 0f;
        for (int i = 0; i < ticks && GodotObject.IsInstanceValid(body); i++)
        {
            await Ticks(1);
            top = Mathf.Max(top, Mathf.Abs(body.Velocity.X));
        }
        return top;
    }

    /// <summary>Tap Jump on a fighter standing on the floor: how high it rose (px), measured
    /// once it is down again.</summary>
    private async Task<float> JumpRise(CharacterController body, VirtualPad pad)
    {
        float y0 = body.GlobalPosition.Y, top = y0;
        bool left = false;
        pad.Tap(ControlAction.Jump);
        for (int i = 0; i < 150; i++)
        {
            await Ticks(1);
            top = Mathf.Min(top, body.GlobalPosition.Y);
            if (!body.IsOnFloor()) left = true;
            else if (left) break;
        }
        await Ticks(2);
        return y0 - top;
    }

    /// <summary>Q on a full ring, aimed at <paramref name="aim"/>: the prison, once it exists (a
    /// press is read on the body's process frame — wait for its effect, KNOWLEDGE v0.12.1).</summary>
    private async Task<BlackPrison> CastPrison(PumpKing caster, VirtualPad pad, Vector2 aim)
    {
        caster.GainUltCharge(caster.MaxUltCharge);
        pad.Aim = aim;
        pad.Tap(ControlAction.Ult);
        for (int i = 0; i < 30 && caster.Prison == null; i++) await Ticks(1);
        return caster.Prison;
    }

    private async Task WaitForStage(BlackPrison prison, BlackPrison.Phase stage, int budget)
    {
        for (int i = 0; i < budget && GodotObject.IsInstanceValid(prison) && prison.Stage != stage; i++) await Ticks(1);
    }

    private static bool HeldBy(float move, float jump, bool silenced, float sight) =>
        Mathf.IsEqualApprox(move, BlackPrison.MoveScale) && Mathf.IsEqualApprox(jump, BlackPrison.JumpScale) && silenced
        && Mathf.IsEqualApprox(sight, Units.Px(BlackPrison.SightMetres));
    private static bool FreeOf(float move, float jump, bool silenced, float sight) =>
        Mathf.IsEqualApprox(move, 1f) && Mathf.IsEqualApprox(jump, 1f) && !silenced && float.IsPositiveInfinity(sight);
    private static string RulesOf(float move, float jump, bool silenced, float sight) =>
        $"move ×{move:0.##}, jump ×{jump:0.##}, {(silenced ? "silenced" : "not silenced")}, sight {(float.IsPositiveInfinity(sight) ? "∞" : M(sight) + " m")}";
    private static bool Held(BaseFoe f) => HeldBy(f.MoveSpeedScale, f.JumpHeightScale, f.Silenced, f.SightLimitPx);
    private static bool Held(CharacterController c) => HeldBy(c.MoveSpeedScale, c.JumpHeightScale, c.Silenced, c.SightLimitPx);
    private static bool Free(BaseFoe f) => FreeOf(f.MoveSpeedScale, f.JumpHeightScale, f.Silenced, f.SightLimitPx);
    private static bool Free(CharacterController c) => FreeOf(c.MoveSpeedScale, c.JumpHeightScale, c.Silenced, c.SightLimitPx);
    private static string Rules(BaseFoe f) => RulesOf(f.MoveSpeedScale, f.JumpHeightScale, f.Silenced, f.SightLimitPx);
    private static string Rules(CharacterController c) => RulesOf(c.MoveSpeedScale, c.JumpHeightScale, c.Silenced, c.SightLimitPx);
    private static string Held4 => $"move ×{BlackPrison.MoveScale}, jump ×{BlackPrison.JumpScale}, silenced, sight {BlackPrison.SightMetres} m";

    /// <summary>
    /// The Black Prison cast by the player's PumpKing on a crab penned at its centre. Q on an
    /// empty ring opens nothing; on a full one it spends the ring and opens where he aims. For a
    /// second it only warns — nobody touched, nothing grown, a recast refused. Then the crab
    /// at its centre walks at half speed and is held (jump, silence, sight), as is one 6 m off,
    /// while a crab past its 8 m rim is untouched. The recast, free and once, blinks him to the
    /// nearest spot at the mouth his body fits (the centre is at a crab's height, in the floor for
    /// him) and his head's biggest blast lands on the crab at the centre — its heart — and not on
    /// the one 6 m off or the far one. It holds four seconds, then lets go.
    /// </summary>
    private async Task ProbeTheBlackPrison()
    {
        Node2D stage = BuildStage(0f, 6000f);
        var blasts = new List<HitCue>();
        var blinks = new List<Cue>();
        var recasts = new List<Cue>();
        void Landed(HitCue hit) { if (hit.AttackOwner == "pumpking" && hit.AttackAction == "ult.blast") blasts.Add(hit); }
        void Heard(Cue cue)
        {
            if (cue.Owner != "pumpking") return;
            if (cue.Action == "ult.blink") blinks.Add(cue);
            else if (cue.Action == "ult.recast") recasts.Add(cue);
        }
        CueBus.HitLanded += Landed;
        CueBus.Raised += Heard;
        try { await TheBlackPrison(stage, blasts, blinks, recasts); }
        finally
        {
            CueBus.HitLanded -= Landed;
            CueBus.Raised -= Heard;
            _pads.Clear();
        }
        await Strike(stage);
    }

    private async Task TheBlackPrison(Node2D stage, List<HitCue> blasts, List<Cue> blinks, List<Cue> recasts)
    {
        // The owner's numbers (PumpKing.gdd): changing one is a design decision, not a tune.
        Expect(Mathf.IsEqualApprox(BlackPrison.WarnSeconds, 1f) && Mathf.IsEqualApprox(BlackPrison.HoldSeconds, 4f)
               && Mathf.IsEqualApprox(BlackPrison.MoveScale, 0.5f) && Mathf.IsEqualApprox(BlackPrison.JumpScale, 0.5f)
               && Mathf.IsEqualApprox(BlackPrison.PumpkinEvery, 0.5f) && Mathf.IsEqualApprox(BlackPrison.SightMetres, 3f),
            "black prison: the owner's numbers moved (1 s warning, 4 s held, half speed, half jump, a pumpkin every 0.5 s, sight one cell = 3 m)");
        // The owner's number for its size (v0.12.15).
        Expect(Mathf.IsEqualApprox(BlackPrison.RadiusM, 8f), $"black prison: its radius is {BlackPrison.RadiusM} m; the owner's is 8 m");

        (PumpKing king, VirtualPad pad) = PlayerPumpKing(stage, 2000f);
        // The blink lands him beside the penned crab, whose body blow stuns him for 0.25 s — and
        // a stunned body reads no button, so a press there is simply lost. His poise is the
        // probe's: no gain-no window, so every Q pressed below reaches the kit.
        king.SetStunTakenScale("probe.no_stun", 0f);
        // 7 m off (inside his 8 m), at a crab's height: level with what it holds — a blast from
        // above a crab would meet its Soft Shell and count double.
        Vector2 centre = new(2000f + Units.Px(7f), FloorY - 22f);
        Pen(stage, centre.X - 70f, centre.X + 70f);
        CrabFoe walker = Crab(stage, centre.X, 1, 3);
        // 6 m off: inside the 8 m prison, outside its heart (his head's 3 m blast) wherever the
        // crab walks in its pen (±1.4 m).
        float midX = centre.X + Units.Px(6f);
        Pen(stage, midX - 70f, midX + 70f);
        CrabFoe mid = Crab(stage, midX, 1, 7);
        // 6 m past its rim.
        float farX = centre.X + Units.Px(BlackPrison.RadiusM + 6f);
        Pen(stage, farX - 70f, farX + 70f);
        CrabFoe far = Crab(stage, farX, 1, 5);
        await Ticks(20);
        float walk = await TopSpeedX(walker, 30);
        Expect(walk > 60f, $"black prison: the penned crab walks {M(walk)} m/s before the cast; the probe needs it walking");

        pad.Aim = centre;
        pad.Tap(ControlAction.Ult);
        await Ticks(10);
        Expect(king.Prison == null, "black prison: Q on an empty ring opened a prison");

        BlackPrison prison = await CastPrison(king, pad, centre);
        Expect(prison != null, "black prison: Q on a full ring opened no prison");
        if (prison == null) return;
        Expect(king.UltCharge < 1f, $"black prison: opening it must spend the whole ring ({king.UltCharge:0} of {king.MaxUltCharge:0} left)");
        Expect(prison.Centre.DistanceTo(centre) < 0.5f, $"black prison: it opened at {prison.Centre}, not where he aimed ({centre}), 7 m off and inside his {PumpKing.PrisonCastRangeM} m");
        Expect(Mathf.IsEqualApprox(prison.Radius, Units.Px(BlackPrison.RadiusM)) && Mathf.IsEqualApprox(prison.Heart, king.ExplosionRadius),
            $"black prison: it opened {M(prison.Radius)} m wide with a {M(prison.Heart)} m heart; the owner's is {BlackPrison.RadiusM:0.0} m, its heart his head's blast ({M(king.ExplosionRadius)} m)");

        // The warning: nobody touched, nothing grown, a recast refused.
        bool early = false;
        for (int i = 0; i < 90 && prison.Stage == BlackPrison.Phase.Warning; i++)
        {
            if (!Free(walker) || prison.PumpkinsGrown > 0) early = true;
            if (i == 10) pad.Tap(ControlAction.Ult);
            await Ticks(1);
        }
        Expect(!early, "black prison: in its warning second it already held the crab inside or grew a pumpkin");
        Expect(!prison.RecastUsed, "black prison: a recast pressed in the warning was taken; it must wait for the prison to form");
        Expect(prison.Standing && Mathf.Abs(prison.FormedAt - BlackPrison.WarnSeconds) <= 1.5f * Tick,
            $"black prison: it began to hold after {prison.FormedAt:0.000} s; the warning is {BlackPrison.WarnSeconds} s");

        Expect(Held(walker) && Held(mid), $"black prison: the crabs inside — at its centre and 6 m off — must be held ({Rules(walker)} · {Rules(mid)}; expected {Held4})");
        Expect(Free(far), $"black prison: a crab past its rim must be untouched ({Rules(far)})");
        float slowed = await TopSpeedX(walker, 30);
        Expect(Mathf.Abs(slowed - walk * BlackPrison.MoveScale) <= walk * 0.06f,
            $"black prison: the crab inside walks {M(slowed)} m/s; half its {M(walk)} m/s is {M(walk * BlackPrison.MoveScale)}");

        // The recast: free, once.
        float charge = king.UltCharge;
        pad.Tap(ControlAction.Ult);
        for (int i = 0; i < 60 && !prison.Swallowed; i++) await Ticks(1);
        Expect(prison.RecastUsed && prison.Swallowed, "black prison: Q while it stood did not blink him into it");
        // Where his body is, not where the cue says (it lands him just above the floor, and he
        // may have dropped those few px by the time this runs).
        Vector2 landed = king.GlobalPosition;
        // Both: where the blink put him (the cue — clear of the floor, whatever the physics does
        // next) and where his body is.
        Expect(blinks.Count == 1 && blinks[0].Position.Y + 32f <= FloorY + 0.5f && landed.DistanceTo(centre) <= 24f && landed.Y + 32f <= FloorY + 0.5f,
            $"black prison: the recast must land him at the mouth, on the nearest spot his body fits (he is at {landed}; the centre {centre} is {M(FloorY - centre.Y)} m over the floor)");
        Expect(king.UltCharge >= charge - 0.01f, $"black prison: the recast must be free (ring {charge:0} → {king.UltCharge:0})");
        float blastWants = king.ExplosionDamage * PumpKing.UltBlastDamageScale * king.DamageDealtScale;
        Expect(blasts.Exists(h => ReferenceEquals(h.Victim, walker) && Mathf.IsEqualApprox(h.Damage, blastWants, 0.01f) && h.Knockback == Vector2.Zero),
            $"black prison: his head's biggest blast must land on the crab inside for {blastWants:0}, shoving nobody ({blasts.Count} blast hit(s))");
        Expect(!blasts.Exists(h => ReferenceEquals(h.Victim, far) || ReferenceEquals(h.Victim, mid)),
            "black prison: the recast's blast reached a crab 6 m off or past the rim — it is his head's (the prison's heart), not the prison's");
        int blastCount = blasts.Count;
        await Ticks(20);
        for (int i = 0; i < 8; i++)
        {
            pad.Tap(ControlAction.Ult);
            await Ticks(6);
        }
        await Ticks(40);
        Expect(recasts.Count == 1 && blinks.Count == 1 && blasts.Count == blastCount,
            $"black prison: Q pressed again after the recast opened {recasts.Count - 1} more mouth(s) and blinked him {blinks.Count - 1} more time(s); the recast is once a prison");

        for (int i = 0; i < 60 * 5 && !prison.Over; i++) await Ticks(1);
        Expect(prison.Over && Mathf.Abs(prison.EndedAt - prison.FormedAt - BlackPrison.HoldSeconds) <= 1.5f * Tick,
            $"black prison: it held {prison.EndedAt - prison.FormedAt:0.000} s; it holds {BlackPrison.HoldSeconds} s");
        Expect(Free(walker), $"black prison: when it ends the crab must be let go ({Rules(walker)})");
        float after = await TopSpeedX(walker, 30);
        Expect(Mathf.Abs(after - walk) <= walk * 0.06f, $"black prison: let go, the crab walks {M(after)} m/s; it walked {M(walk)}");
        GD.Print($"  boss     black prison   {M(prison.Radius)} m wide · {prison.FormedAt:0.00} s of warning, then held {prison.EndedAt - prison.FormedAt:0.00} s · a crab inside walks {M(walk)} → {M(slowed)} → {M(after)} m/s · " +
                 $"the recast lands him {M(landed.DistanceTo(centre))} m above a centre in the floor · his biggest blast {blastWants:0} over its {M(prison.Heart)} m heart, no shove");
    }

    /// <summary>
    /// What the prison does to a foe's mind. A level-6 crab beside him, inside it — one that
    /// would jump at once — starts no skill until it lets go, then jumps. A level-3 crab inside it
    /// 5 m from him, who would see him, cannot (blind past one cell) and does not turn on him
    /// even when struck, until it lets go.
    /// </summary>
    private async Task ProbeBlackPrisonOnFoes()
    {
        Node2D stage = BuildStage(0f, 6000f);
        try { await BlackPrisonOnFoes(stage); }
        finally { _pads.Clear(); }
        await Strike(stage);
    }

    private async Task BlackPrisonOnFoes(Node2D stage)
    {
        const float x = 2000f;
        (PumpKing king, VirtualPad pad) = PlayerPumpKing(stage, x);
        Pen(stage, x + 48f, x + 108f);     // the jumper, 2.2–2.7 m from him: inside its jump trigger and its sight
        Pen(stage, x + 128f, x + 198f);    // the watcher, 4.7–5.5 m from him: inside its sight, past the prison's cell
        // The watcher turns on him first: blinded, it must lose him at once, not after three
        // more looks (FOES §6's rule for a player who slipped out of its sight shape).
        CrabFoe watcher = Crab(stage, x + 163f, 3, 13);
        for (int i = 0; i < 90 && !watcher.Aggroed; i++) await Ticks(1);
        Expect(watcher.Aggroed, "black prison on foes: the level-3 crab 5 m off never saw him — the probe needs it to");
        BlackPrison prison = await CastPrison(king, pad, new Vector2(x + 120f, FloorY - 22f));
        Expect(prison != null, "black prison on foes: no prison opened");
        if (prison == null) return;
        bool sawHimInTheWarning = true;
        for (int i = 0; i < 90 && prison.Stage == BlackPrison.Phase.Warning; i++)
        {
            if (!watcher.Aggroed) sawHimInTheWarning = false;
            await Ticks(1);
        }
        int lostAfter = -1;
        for (int i = 0; i < 30; i++)
        {
            if (!watcher.Aggroed) { lostAfter = i; break; }
            await Ticks(1);
        }
        Expect(sawHimInTheWarning && lostAfter >= 0 && lostAfter <= 3,
            $"black prison on foes: a crab that had turned on him must keep him through the warning and lose him the moment it is blinded ({(lostAfter < 0 ? "still on him half a second later" : $"lost after {lostAfter} tick(s)")})");

        // The jumper is placed once it holds, so its very first look is already a prisoner's.
        CrabFoe jumper = Crab(stage, x + 78f, 6, 11);
        bool jumped = false, turned = false, struck = false;
        for (int i = 0; i < 60 * 5 && !prison.Over; i++)
        {
            await Ticks(1);
            // Only while it still holds: on the tick it lets go a crab may jump in the same frame.
            if (prison.Over) break;
            if (jumper.Velocity.Y < -200f) jumped = true;
            if (watcher.Aggroed) turned = true;
            if (i == 60) { watcher.TakeHit(new HitInfo(1f), king.GlobalPosition); struck = true; }
        }
        Expect(prison.Over && struck, "black prison on foes: it never ended");
        Expect(Free(jumper) && Free(watcher), $"black prison on foes: let go, a crab kept a rule ({Rules(jumper)} · {Rules(watcher)})");
        Expect(!jumped, "black prison on foes: a silenced crab beside him jumped (its Jump is a skill)");
        Expect(!turned, "black prison on foes: a crab blind past one cell turned on him from 5 m — or when struck");

        bool jumpsAfter = false, turnsAfter = false;
        for (int i = 0; i < 120 && !(jumpsAfter && turnsAfter); i++)
        {
            await Ticks(1);
            if (jumper.Velocity.Y < -200f) jumpsAfter = true;
            if (watcher.Aggroed) turnsAfter = true;
        }
        Expect(jumpsAfter, "black prison on foes: let go, the crab beside him never jumped — the probe's crab must want to");
        Expect(turnsAfter, "black prison on foes: let go, the crab 5 m off never saw him — the probe's crab must be able to");
        GD.Print($"  boss     black prison   on foes: a level-6 crab beside him starts no Jump while held, jumps when let go · a level-3 crab 5 m off is blind to him (sight {BlackPrison.SightMetres} m), struck or not, and turns on him when let go");
    }

    /// <summary>
    /// The cast range and the pumpkins. The range rule alone, then a real cast aimed 30 m off:
    /// it opens 8 m along the same line. Eight crabs stand rooted across it and round it; it grows
    /// a pumpkin as it forms and every half second after — eight — and they hurt what they land on.
    /// The prison's draws are seeded so the throws are the same every run.
    /// </summary>
    private async Task ProbePrisonPumpkins()
    {
        Vector2 from = new(100f, 100f);
        float range = Units.Px(PumpKing.PrisonCastRangeM);
        Vector2 near = from + new Vector2(50f, -20f);
        Expect(PumpKing.ClampPrisonCentre(from, near, range) == near, "black prison: an aim inside the cast range must be kept");
        Vector2 diagonal = PumpKing.ClampPrisonCentre(from, from + new Vector2(-900f, -900f), range) - from;
        Expect(Mathf.Abs(diagonal.Length() - range) < 0.01f && diagonal.Normalized().Dot(new Vector2(-1f, -1f).Normalized()) > 0.9999f,
            $"black prison: an aim 40 m up and behind must come to {PumpKing.PrisonCastRangeM} m along the same line (got {M(diagonal.Length())} m)");

        Node2D stage = BuildStage(0f, 6000f);
        var thrown = new List<int>();
        var hits = new List<HitCue>();
        int tick = 0;
        void Heard(Cue cue) { if (cue.Owner == "pumpking" && cue.Action == "ult.pumpkin") thrown.Add(tick); }
        void Landed(HitCue hit) { if (hit.AttackOwner == "pumpking" && hit.AttackAction == "ult.pumpkin") hits.Add(hit); }
        CueBus.Raised += Heard;
        CueBus.HitLanded += Landed;
        try
        {
            (PumpKing king, VirtualPad pad) = PlayerPumpKing(stage, 2000f);
            float cx = 2000f + range;
            foreach (float dx in new[] { -200f, -140f, -80f, -30f, 30f, 80f, 140f, 200f })
                Crab(stage, cx + dx, 1, 17).ApplyTrapped(60f);
            await Ticks(10);
            Vector2 at = king.GlobalPosition;
            Vector2 aim = new(at.X + Units.Px(30f), FloorY - 22f);
            BlackPrison prison = await CastPrison(king, pad, aim);
            Expect(prison != null, "black prison: Q aimed 30 m off opened no prison");
            if (prison != null)
            {
                prison.Rng = new RandomNumberGenerator { Seed = 20261127 };   // before it forms: nothing drawn yet
                Vector2 off = prison.Centre - at;
                Expect(Mathf.Abs(off.Length() - range) < 0.5f && off.Normalized().Dot((aim - at).Normalized()) > 0.9999f,
                    $"black prison: aimed 30 m off it must open {PumpKing.PrisonCastRangeM} m along the aim (opened {M(off.Length())} m off)");
                for (; tick < 60 * 8 && !(prison.Over && tick > 60 * 7); tick++) await Ticks(1);

                Expect(prison.PumpkinsGrown == BlackPrison.PumpkinCount && thrown.Count == BlackPrison.PumpkinCount,
                    $"black prison: it grew {prison.PumpkinsGrown} pumpkin(s) and threw {thrown.Count}; one as it forms and every {BlackPrison.PumpkinEvery} s of its {BlackPrison.HoldSeconds} s is {BlackPrison.PumpkinCount}");
                bool even = thrown.Count > 1;
                for (int i = 1; i < thrown.Count; i++)
                    if (Mathf.Abs(thrown[i] - thrown[i - 1] - BlackPrison.PumpkinEvery * 60f) > 1.01f) even = false;
                Expect(even, $"black prison: pumpkins must come every {BlackPrison.PumpkinEvery} s (at ticks {string.Join(", ", thrown)})");
                float lo = BlackPrison.PumpkinMinDamage * king.DamageDealtScale - 0.01f, hi = BlackPrison.PumpkinMaxDamage * king.DamageDealtScale + 0.01f;
                Expect(hits.Count > 0, "black prison: not one pumpkin of eight hurt a crab standing in or round the prison");
                // A crab's Soft Shell doubles a blow from above (FOES §3), and a pumpkin often
                // comes down on one: a hit is a pumpkin's damage, or twice it.
                bool Sized(float d) => (d >= lo && d <= hi) || (d >= 2f * lo && d <= 2f * hi);
                Expect(hits.TrueForAll(h => Sized(h.Damage) && h.Ranged && h.Knockback == Vector2.Zero),
                    $"black prison: a pumpkin's hit must be ranged, shove nobody and deal {lo:0}–{hi:0} (twice that through a crab's shell): {string.Join("/", hits.ConvertAll(h => h.Damage.ToString("0")))}");
                Expect(hits.Exists(h => h.Damage >= lo && h.Damage <= hi), "black prison: no pumpkin landed a plain hit, from the side");
                GD.Print($"  boss     black prison   aimed 30 m off it opens {M(off.Length())} m away · {thrown.Count} pumpkins, every {BlackPrison.PumpkinEvery} s · {hits.Count} hit a crab, for {string.Join("/", hits.ConvertAll(h => h.Damage.ToString("0")))}");
            }
        }
        finally
        {
            CueBus.Raised -= Heard;
            CueBus.HitLanded -= Landed;
            _pads.Clear();
        }
        await Strike(stage);
    }

    /// <summary>
    /// The prison fights for whoever opens it. A BOSS PumpKing's holds the player: Skill 1 is
    /// refused while the basic attack still swings, the run is half speed, a jump half as high,
    /// and the view is dark past one cell — and it all comes back when it ends. The player's holds
    /// a boss: Pump Up is refused while his head still flies, and his brain cannot find a player
    /// 6 m away until it lets go.
    /// </summary>
    private async Task ProbePrisonTakesSides()
    {
        Node2D stage = BuildStage(0f, 6000f);
        var swings = new List<Cue>();
        void Heard(Cue cue) { if (cue.Owner == "pomegraknight" && cue.Action == "ba.swing") swings.Add(cue); }
        CueBus.Raised += Heard;
        try { await PrisonOnThePlayer(stage, swings); }
        finally
        {
            CueBus.Raised -= Heard;
            _pads.Clear();
        }
        await Strike(stage);

        stage = BuildStage(0f, 6000f);
        try { await PrisonOnABoss(stage); }
        finally { _pads.Clear(); }
        await Strike(stage);

        stage = BuildStage(0f, 6000f);
        try { await PrisonSlowsASoul(stage); }
        finally { _pads.Clear(); }
        await Strike(stage);

        stage = BuildStage(0f, 6000f);
        try { await PrisonWalkOut(stage); }
        finally { _pads.Clear(); }
        await Strike(stage);
    }

    /// <summary>
    /// The size and the slow, walked: a 10 m/s fighter caught 4 m from the centre of the 8 m
    /// prison runs straight out and is free — hit circle clear of the rim — after
    /// (8 + 0.9 − 4) m at half speed: about 1 s. (From the centre the walk is 1.8 s.)
    /// </summary>
    private async Task PrisonWalkOut(Node2D stage)
    {
        (CharacterController boss, VirtualPad bossPad) = StageAsleep(stage, BossTable.PumpKing, new Vector2(3000f, FloorY - 32f));
        Vector2 centre = new(3000f + Units.Px(PumpKing.PrisonCastRangeM), FloorY - 32f);
        CharacterController player = PadPlayer(stage, centre.X + Units.Px(4f), out VirtualPad pad);
        await Ticks(20);

        BlackPrison prison = await CastPrison((PumpKing)boss, bossPad, centre);
        Expect(prison != null, "black prison walk-out: a BOSS PumpKing's Q on a full ring opened no prison");
        if (prison == null) return;
        await WaitForStage(prison, BlackPrison.Phase.Standing, 90);
        Expect(prison.Holds(player), $"black prison walk-out: a fighter 4 m from the centre of a {M(prison.Radius)} m prison must be held");
        float from = player.GlobalPosition.DistanceTo(prison.Centre);
        pad.Hold(ControlAction.MoveRight);
        int ticks = 0;
        for (; ticks < 60 * 5 && prison.Holds(player) && !prison.Over; ticks++) await Ticks(1);
        pad.Hold(ControlAction.MoveRight, false);
        float seconds = ticks * Tick;
        float slowed = player.MoveSpeed * BlackPrison.MoveScale;
        float expected = (prison.Radius + player.HitRadius - from) / slowed;
        Expect(!prison.Over && Mathf.Abs(seconds - expected) <= 4f * Tick,
            $"black prison walk-out: caught {M(from)} m from the centre, a slowed fighter walked out in {seconds:0.00} s; at {M(slowed)} m/s it is {expected:0.00} s");
        float fromCentre = (prison.Radius + player.HitRadius) / slowed;
        GD.Print($"  boss     black prison   {M(prison.Radius)} m: a {M(player.MoveSpeed)} m/s fighter caught {M(from)} m off its centre walks out in {seconds:0.00} s; from the centre, {fromCentre:0.00} s of its {BlackPrison.HoldSeconds} s");
    }

    /// <summary>A soul flies too: a BOSS PumpKing already in Soul Form when the player's prison
    /// forms on him flies at half his soul's speed (a silence cannot stop a Soul Form already cast).</summary>
    private async Task PrisonSlowsASoul(Node2D stage)
    {
        (CharacterController boss, VirtualPad bossPad) = StageAsleep(stage, BossTable.PumpKing, new Vector2(3000f, FloorY - 32f));
        var soul = (PumpKing)boss;
        (PumpKing king, VirtualPad pad) = PlayerPumpKing(stage, 3000f - Units.Px(7f));
        await Ticks(20);
        bossPad.Tap(ControlAction.Skill1);
        for (int i = 0; i < 90 && soul.CurrentState != PumpKing.PKState.Soul; i++) await Ticks(1);
        Expect(soul.CurrentState == PumpKing.PKState.Soul, "prison takes sides: the boss never went into Soul Form");
        bossPad.Hold(ControlAction.MoveLeft);
        float free = await TopSpeedX(boss, 15);
        bossPad.Hold(ControlAction.MoveLeft, false);
        BlackPrison prison = await CastPrison(king, pad, boss.GlobalPosition);
        Expect(prison != null, "prison takes sides: no prison opened on the soul");
        if (prison == null) return;
        await WaitForStage(prison, BlackPrison.Phase.Standing, 90);
        bossPad.Hold(ControlAction.MoveRight);
        float held = await TopSpeedX(boss, 15);
        bossPad.Hold(ControlAction.MoveRight, false);
        Expect(soul.CurrentState == PumpKing.PKState.Soul && Held(boss), $"prison takes sides: the soul must be held ({Rules(boss)})");
        Expect(Mathf.Abs(free - soul.SoulMoveSpeed) <= soul.SoulMoveSpeed * 0.05f && Mathf.Abs(held - free * BlackPrison.MoveScale) <= free * 0.05f,
            $"prison takes sides: a soul flies {M(free)} m/s, and {M(held)} m/s in the prison; half is {M(free * BlackPrison.MoveScale)}");
        GD.Print($"  boss     black prison   a soul already cast flies {M(free)} → {M(held)} m/s in the prison");
    }

    private async Task PrisonOnThePlayer(Node2D stage, List<Cue> swings)
    {
        (CharacterController boss, VirtualPad bossPad) = StageAsleep(stage, BossTable.PumpKing, new Vector2(3000f, FloorY - 32f));
        // He opens it as far to his right as he may (8 m); the player stands 5 m further right:
        // inside the 8 m prison, near enough its rim to run out (right, away from him) while it stands.
        Vector2 centre = new(3000f + Units.Px(PumpKing.PrisonCastRangeM), FloorY - 32f);
        CharacterController player = PadPlayer(stage, centre.X + Units.Px(5f), out VirtualPad pad);
        await Ticks(20);
        float rise = await JumpRise(player, pad);
        Expect(Mathf.Abs(rise - Units.Px(8f)) < Units.Px(0.6f), $"prison takes sides: before it, the player's jump rises {M(rise)} m; the standard jump is 8 m");

        BlackPrison prison = await CastPrison((PumpKing)boss, bossPad, centre);
        Expect(prison != null, "prison takes sides: a BOSS PumpKing's Q on a full ring opened no prison");
        if (prison == null) return;
        await WaitForStage(prison, BlackPrison.Phase.Standing, 90);
        Expect(Held(player), $"prison takes sides: a BOSS PumpKing's prison must hold the player ({Rules(player)}; expected {Held4})");
        Expect(prison.HeldCount == 1 && !prison.Holds(boss), "prison takes sides: a prison must hold the other side only");
        Expect(prison.VeilShown, "prison takes sides: the player held by it must see only the cell around them");

        float low = await JumpRise(player, pad);
        Expect(Mathf.Abs(low - Units.Px(8f) * BlackPrison.JumpScale) < Units.Px(0.6f),
            $"prison takes sides: held, the player's jump rises {M(low)} m; {BlackPrison.JumpScale} of 8 m is {8f * BlackPrison.JumpScale:0.0} m");
        pad.Tap(ControlAction.Skill1);
        await Ticks(10);
        Expect(player.ShiftCooldown.Remaining <= 0f, "prison takes sides: a silenced player's Skill 1 went off");
        int before = swings.Count;
        pad.Tap(ControlAction.Attack);
        for (int i = 0; i < 20 && swings.Count == before; i++) await Ticks(1);
        Expect(swings.Count > before, "prison takes sides: a silenced player's basic attack must still swing");
        pad.Hold(ControlAction.MoveRight);
        float run = await TopSpeedX(player, 30);
        Expect(Mathf.Abs(run - player.MoveSpeed * BlackPrison.MoveScale) <= player.MoveSpeed * 0.05f,
            $"prison takes sides: held, the player runs {M(run)} m/s; {BlackPrison.MoveScale} of {M(player.MoveSpeed)} m/s is {M(player.MoveSpeed * BlackPrison.MoveScale)}");
        // The border is not a wall: running on out of it frees at once, and coming back catches.
        for (int i = 0; i < 90 && !Free(player); i++) await Ticks(1);
        bool outWhileItStands = Free(player) && !prison.Over && !prison.VeilShown;
        pad.Hold(ControlAction.MoveRight, false);
        pad.Hold(ControlAction.MoveLeft);
        for (int i = 0; i < 90 && !Held(player); i++) await Ticks(1);
        pad.Hold(ControlAction.MoveLeft, false);
        Expect(outWhileItStands, $"prison takes sides: a player who runs out of it must be let go at once ({Rules(player)})");
        Expect(Held(player) && prison.VeilShown && !prison.Over, $"prison takes sides: a player who comes back in must be caught again ({Rules(player)})");

        for (int i = 0; i < 60 * 5 && !prison.Over; i++) await Ticks(1);
        Expect(prison.Over && Free(player) && !prison.VeilShown, $"prison takes sides: when it ends the player must be let go and see again ({Rules(player)})");
        await Ticks(30);
        pad.Tap(ControlAction.Skill1);
        for (int i = 0; i < 20 && player.ShiftCooldown.Remaining <= 0f; i++) await Ticks(1);
        Expect(player.ShiftCooldown.Remaining > 0f, "prison takes sides: let go, the player's Skill 1 must work again");
        GD.Print($"  boss     black prison   a BOSS PumpKing's on the player: jump {M(rise)} → {M(low)} m · run → {M(run)} m/s of {M(player.MoveSpeed)} · Skill 1 refused, the swing allowed · the view dark past {BlackPrison.SightMetres} m");
    }

    private async Task PrisonOnABoss(Node2D stage)
    {
        (CharacterController boss, VirtualPad bossPad) = StageAsleep(stage, BossTable.PumpKing, new Vector2(3000f, FloorY - 32f));
        var bossKing = (PumpKing)boss;
        (PumpKing king, VirtualPad pad) = PlayerPumpKing(stage, 3000f - Units.Px(6f));
        await Ticks(20);
        var senses = new BossSenses();
        senses.Sample(boss, 0.1f);
        Expect(senses.HasTarget, "prison takes sides: before it, the boss's senses must find the player 6 m off");

        BlackPrison prison = await CastPrison(king, pad, boss.GlobalPosition);
        Expect(prison != null, "prison takes sides: the player's Q on a full ring opened no prison on the boss");
        if (prison == null) return;
        await WaitForStage(prison, BlackPrison.Phase.Standing, 90);
        Expect(Held(boss), $"prison takes sides: the player's prison must hold a boss ({Rules(boss)}; expected {Held4})");
        Expect(!prison.VeilShown, "prison takes sides: the player is not in their own prison; their view must not darken");
        senses.Sample(boss, 0.1f);
        Expect(!senses.HasTarget, "prison takes sides: a boss blind past one cell must not find the player 6 m off");

        bossPad.Tap(ControlAction.Skill2);                 // Pump Up: a skill
        await Ticks(15);
        Expect(bossKing.PumpStacks == 0, "prison takes sides: a silenced boss pumped up (Skill 2)");
        bossPad.Aim = king.GlobalPosition;
        bossPad.Tap(ControlAction.Attack);                 // his head: the basic attack
        for (int i = 0; i < 20 && !bossKing.HasLiveHead; i++) await Ticks(1);
        Expect(bossKing.HasLiveHead, "prison takes sides: a silenced boss's basic attack (his head) must still fly");
        bossPad.Tap(ControlAction.Attack);                 // set it off, so Pump Up is his to press again
        for (int i = 0; i < 30 && bossKing.HasLiveHead; i++) await Ticks(1);

        for (int i = 0; i < 60 * 5 && !prison.Over; i++) await Ticks(1);
        Expect(prison.Over && Free(boss), $"prison takes sides: when it ends the boss must be let go ({Rules(boss)})");
        senses.Sample(boss, 0.1f);
        Expect(senses.HasTarget, "prison takes sides: let go, the boss's senses must find the player again");
        bossPad.Tap(ControlAction.Skill2);
        for (int i = 0; i < 20 && bossKing.PumpStacks == 0; i++) await Ticks(1);
        Expect(bossKing.PumpStacks == 1, "prison takes sides: let go, the boss must pump up again");
        GD.Print("  boss     black prison   the player's on a boss: his brain loses a player 6 m off · Pump Up refused while his head still flies · all back when it ends");
    }
}
