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
}
