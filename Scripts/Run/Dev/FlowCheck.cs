namespace Fableland.Run.Dev;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using Fableland.Bosses;
using Fableland.Data;
using Fableland.Items;

/// <summary>
/// Headless check for the gaps a player would hit (Docs/Research/AUDIT-v0.11.1.md Part 2) —
/// each fixed one reproduced on real bodies, so it stays fixed (40-QA.md §2):
///
///     Godot --headless --path . res://Scenes/Run/FlowCheck.tscn      # exit 0 = clean
///
/// • #13 — a resolved mission's invincibility survives Pixolotl's Shift.
/// • #16 — a shelter screen always has a way out (every case of its exits).
/// • #12 — Yukai's Rope lets go at the wall it pulled you to.
/// • #17 — a kit volume does not refill jumps, so Cleopastar's star jump fires.
/// • #14 — Sifu Pangda's blows are melee to a held item; Cleopastar's stars and PumpKing's
///   head credit their thrower with what they deal.
/// • #15 — the Fire Tornado replaces Slash 1, spins in place, burns nothing.
/// • #11 — a seagull's puddle hits each body once.
///
/// Each failure prints on its own `FAIL` line.
/// </summary>
public partial class FlowCheck : Node
{
    private readonly List<string> _problems = new();
    private int _checks;

    private const int SpeedUp = 8;
    private const float Tick = 1f / 60f;
    private const float FloorY = 600f;

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
            CheckShelterExits();
            CheckRewardsNamed();
            await CheckHudFaces();
            Engine.PhysicsTicksPerSecond = ticksPerSecond * SpeedUp;
            Engine.TimeScale = SpeedUp;
            await Ticks(1);
            await ProbeResolvedInvincibility();
            await ProbeYukaisRope();
            await ProbeKitVolumeJumps();
            await ProbeStarJump();
            await ProbeDamageCredit();
            await ProbeFireTornado();
            await ProbePoopPuddle();
        }
        catch (Exception e)
        {
            _problems.Add("flow check threw: " + e);
        }
        finally
        {
            Engine.PhysicsTicksPerSecond = ticksPerSecond;
            Engine.TimeScale = timeScale;
        }
        foreach (string problem in _problems) GD.Print("FAIL  " + problem);
        GD.Print(_problems.Count == 0 ? $"FlowCheck: OK ({_checks} checks)" : $"FlowCheck: {_problems.Count} problem(s) in {_checks} checks");
        GetTree().Quit(_problems.Count == 0 ? 0 : 1);
    }

    private void Expect(bool ok, string what)
    {
        _checks++;
        if (!ok) _problems.Add(what);
    }

    private async Task Ticks(int count)
    {
        for (int i = 0; i < count; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }

    private static string M(float px) => (px / Units.PixelsPerMeter).ToString("0.0");

    // ---------------------------------------------------------------- stage

    private Node2D BuildStage(float x0, float x1)
    {
        var stage = new Node2D { Name = "Stage" };
        AddChild(stage);
        AddBox(stage, new Rect2(x0, FloorY, x1 - x0, 200f));
        AddBox(stage, new Rect2(x0 - 40f, FloorY - 2000f, 40f, 2200f));
        AddBox(stage, new Rect2(x1, FloorY - 2000f, 40f, 2200f));
        return stage;
    }

    private static void AddBox(Node2D stage, Rect2 rect)
    {
        var body = new StaticBody2D { Position = rect.GetCenter(), CollisionLayer = 0, CollisionMask = 0 };
        body.SetCollisionLayerValue(Units.LayerGround, true);
        body.AddChild(new CollisionShape2D { Shape = new RectangleShape2D { Size = rect.Size } });
        stage.AddChild(body);
    }

    private static string SceneOf(string id)
    {
        foreach ((string entry, string path) in Debug.ProtagonistRoster.Entries)
            if (entry == id) return path;
        return null;
    }

    /// <summary>A protagonist on its own pad, which this check steps each frame.</summary>
    private CharacterController Fighter(Node2D stage, string id, Vector2 at, out VirtualPad pad)
    {
        var body = ResourceLoader.Load<PackedScene>(SceneOf(id)).Instantiate<CharacterController>();
        stage.AddChild(body);
        body.GlobalPosition = at;
        body.SetSpawnPoint(at);
        pad = new VirtualPad();
        body.UseControls(pad);
        _pads.Add(pad);
        return body;
    }

    /// <summary>A target that stands still and takes hits: BOSS PumpKing with his brain asleep.</summary>
    private static CharacterController Dummy(Node2D stage, Vector2 at)
    {
        BossDef def = BossTable.PumpKing;
        var boss = ResourceLoader.Load<PackedScene>(def.ScenePath).Instantiate<CharacterController>();
        stage.AddChild(boss);
        boss.GlobalPosition = at;
        boss.SetSpawnPoint(at);
        ((IBossFighter)boss).BeginFight(def, 1, new BossArena(new[] { at }, 0));
        boss.GetNode<BossBrain>("BossBrain").Sleep();
        return boss;
    }

    private async Task Strike(Node2D stage)
    {
        _pads.Clear();
        stage.QueueFree();
        await Ticks(2);
    }

    // ---------------------------------------------------------------- #16 the shelter's exits

    private void CheckShelterExits()
    {
        // An Eidolon Shelter: its Blessing is the way out while it can carry you across.
        var crossing = ShelterController.Exits(eidolon: true, blessed: true, canCross: true, stamina: 3);
        Expect(!crossing.Back && !crossing.Finish && crossing.Blessings, "shelter: a blessed Eidolon Shelter offers its Blessing as the only exit");
        foreach ((bool blessed, bool canCross, string what) in new[] { (false, false, "spent"), (true, false, "blocked (far shore devoured)") })
            foreach (int stamina in new[] { 0, 3 })
            {
                var exits = ShelterController.Exits(eidolon: true, blessed, canCross, stamina);
                Expect(exits.Back && !exits.Blessings, $"shelter: an Eidolon Shelter whose crossing is {what} must open Back (stamina {stamina}) — it was a screen with no exit (#16)");
            }
        var hub = ShelterController.Exits(eidolon: false, blessed: true, canCross: false, stamina: 0);
        Expect(hub.Finish && !hub.Back && hub.Blessings, "shelter: a hub with no stamina keeps Finish the Day and its Blessing");
        GD.Print("  flow     shelter        an Eidolon Shelter opens Back once its crossing is spent or blocked, at any stamina; hubs unchanged");
    }

    // ---------------------------------------------------------------- #2, #6 what the player is told

    private void CheckRewardsNamed()
    {
        var bundle = new RewardBundle { WonderCores = 10, AtkBonus = 10 };
        bundle.ItemDefIds.Add("fanchens_heart");
        string said = bundle.Describe();
        Expect(said == $"+10 wonder cores · {Items.ItemCatalog.DisplayName("fanchens_heart")} · +10 ATK",
            $"rewards: a goal's reward must be named in the toast — '{said}' (#2)");
        Expect(new RewardBundle().Describe() == "" && new RewardBundle { WonderCores = 1 }.Describe() == "+1 wonder core",
            "rewards: an empty bundle says nothing, and one core is a core");
        GD.Print($"  flow     rewards        a goal's toast names what it paid: \"{said}\"");
    }

    /// <summary>The HUD shows each fighter's own face, in the portrait and in the Switch slot —
    /// Cleopastar's slot used to vanish whenever she was next (#6).</summary>
    private async Task CheckHudFaces()
    {
        var hud = ResourceLoader.Load<PackedScene>("res://Scenes/Hud.tscn").Instantiate<global::Hud>();
        AddChild(hud);
        await Ticks(1);
        var missing = new List<string>();
        foreach ((string id, string _) in Debug.ProtagonistRoster.Entries)
        {
            hud.SetNextProtagonist(id);
            hud.SetPortrait(id);
            hud.SetHp(50f, 100f);
            string path = $"res://Assets/Sprites/Protagonists/{id}/UI/Mugshot.png";
            if (!hud.SwitchSlotShown || hud.SwitchFace?.ResourcePath != path || hud.PortraitFace?.ResourcePath != path) missing.Add(id);
        }
        Expect(missing.Count == 0, $"hud: every fighter's own face must show in the portrait and the Switch slot — not for {string.Join(", ", missing)} (#6)");
        hud.QueueFree();
        await Ticks(1);
        GD.Print($"  flow     hud            all {Debug.ProtagonistRoster.Entries.Length} fighters show their own face in the portrait and the Switch slot");
    }

    // ---------------------------------------------------------------- #13 resolved invincibility

    private async Task ProbeResolvedInvincibility()
    {
        Node2D stage = BuildStage(0f, 4000f);
        CharacterController pixo = Fighter(stage, "Pixolotl", new Vector2(1000f, FloorY - 32f), out VirtualPad pad);
        pad.Hold(ControlAction.MoveRight);
        await Ticks(40);                      // walk a little: frames for the Shift to rewind
        pad.ReleaseAll();
        await Ticks(10);
        pixo.SetInvincible(CharacterController.ResolvedInvincibleSource, true);   // the mission resolved
        pad.Tap(ControlAction.Skill1);
        bool shifted = false;
        for (int i = 0; i < 30 && !shifted; i++)
        {
            await Ticks(1);
            shifted = pixo.ControlsLocked;
        }
        for (int i = 0; i < 300 && pixo.ControlsLocked; i++) await Ticks(1);
        await Ticks(5);
        Expect(shifted, "resolution: Pixolotl's Shift did not start (no frames to rewind?)");
        Expect(pixo.Invincible && pixo.IsInvincibleFrom(CharacterController.ResolvedInvincibleSource),
            "resolution: after Pixolotl's Shift the end-of-mission invincibility is gone — foes and hazards can still kill her (#13)");
        pixo.SetInvincible(CharacterController.ResolvedInvincibleSource, false);
        Expect(!pixo.Invincible, "resolution: with the mission's source cleared nothing else may hold her invincible");
        GD.Print("  flow     resolution     the end-of-mission invincibility survives Pixolotl's Shift (a source of its own)");
        await Strike(stage);
    }

    // ---------------------------------------------------------------- #12 Yukai's Rope

    private async Task ProbeYukaisRope()
    {
        Node2D stage = BuildStage(0f, 3000f);
        AddBox(stage, new Rect2(1600f, FloorY - 600f, 60f, 600f));       // a wall 18 m to her right
        CharacterController pome = Fighter(stage, "Pomegraknight", new Vector2(1040f, FloorY - 32f), out VirtualPad pad);
        await Ticks(20);
        var state = new ProtagonistState("Pomegraknight") { HeldItemDefId = "yukais_rope", HeldItemInstanceId = "check" };
        var rope = new ItemRuntime(pome, state);
        pad.Facing = 1f;
        pad.Hold(ControlAction.MoveRight);
        await Ticks(2);
        pad.ReleaseAll();
        await Ticks(4);
        bool used = rope.TryUse(out string reason);
        bool letGo = false;
        float seconds = 0f;
        for (int i = 0; i < 240 && used; i++)
        {
            await Ticks(1);
            rope.Tick(Tick);
            seconds += Tick;
            if (!rope.RopeDragging) { letGo = true; break; }
        }
        await Ticks(20);
        float gap = 1600f - pome.GlobalPosition.X;
        Expect(used, $"rope: Yukai's Rope found no wall 18 m ahead ({reason})");
        Expect(letGo && seconds <= YukaisRopeBehavior.MaxDragSeconds + 0.05f, $"rope: the drag must let go at the wall, not hold on for ever ({(letGo ? $"let go after {seconds:0.00} s" : "still pulling after 4 s")}) (#12)");
        Expect(gap < Units.Px(1.5f) && Mathf.Abs(pome.Velocity.X) < Units.Px(1f), $"rope: she must end at the wall, at rest ({M(gap)} m short, moving {M(pome.Velocity.X)} m/s)");
        rope.Dispose();
        GD.Print($"  flow     rope           Yukai's Rope drags her 18 m to a wall and lets go after {seconds:0.00} s, {M(gap)} m from it");
        await Strike(stage);
    }

    // ---------------------------------------------------------------- #17 jumps in a kit volume

    private static SoftVolume Volume(Node2D stage, Rect2 area, bool swimmable)
    {
        var volume = new SoftVolume { StagnationIndex = 0.3f, GravityIndex = 0.1f, Swimmable = swimmable, CollisionLayer = 0, CollisionMask = 0 };
        volume.SetCollisionMaskValue(Units.LayerPlayer, true);
        volume.AddChild(new CollisionShape2D { Shape = new RectangleShape2D { Size = area.Size } });
        stage.AddChild(volume);
        volume.GlobalPosition = area.GetCenter();
        return volume;
    }

    /// <summary>A volume a fighter swims in refills her jumps; a kit volume does not.</summary>
    private async Task ProbeKitVolumeJumps()
    {
        Node2D stage = BuildStage(0f, 4000f);
        Volume(stage, new Rect2(600f, FloorY - 1200f, 400f, 1000f), swimmable: false);
        Volume(stage, new Rect2(2600f, FloorY - 1200f, 400f, 1000f), swimmable: true);
        CharacterController inKit = Fighter(stage, "Pomegraknight", new Vector2(800f, FloorY - 700f), out _);
        CharacterController inSea = Fighter(stage, "Pomegraknight", new Vector2(2800f, FloorY - 700f), out _);
        await Ticks(40);                      // past the coyote grace: her one jump is forfeited
        Expect(inKit.JumpsRemaining == 0, $"jumps: inside a kit volume (not swimmable) her jump must stay spent, not refill every tick ({inKit.JumpsRemaining}) (#17)");
        Expect(inSea.JumpsRemaining == inSea.JumpCharges, $"jumps: inside a volume she swims in her jumps stay full ({inSea.JumpsRemaining}/{inSea.JumpCharges})");
        GD.Print($"  flow     jumps          in a kit volume a spent jump stays spent ({inKit.JumpsRemaining}); in a swimmable one they stay full ({inSea.JumpsRemaining})");
        await Strike(stage);
    }

    /// <summary>Cleopastar.gdd: out of jumps inside her own stationary star, a jump spends the
    /// star — it explodes — and launches her.</summary>
    private async Task ProbeStarJump()
    {
        Node2D stage = BuildStage(0f, 4000f);
        CharacterController cleo = Fighter(stage, "Cleopastar", new Vector2(1000f, FloorY - 32f), out VirtualPad pad);
        await Ticks(110);                     // her magazine starts empty: one Glow per 1.5 s
        // A star thrown up and to the right: it flies its 8 m, then hovers.
        pad.Aim = cleo.GlobalPosition + new Vector2(Units.Px(5f), -Units.Px(8f));
        pad.Tap(ControlAction.Attack);
        CleoStar star = null;
        for (int i = 0; i < 30 && star == null; i++)
        {
            await Ticks(1);
            foreach (Node child in stage.GetChildren()) if (child is CleoStar thrown) star = thrown;
        }
        // 8 m at 12 m/s, then a 1 s hover: only in its fall phase is its core a volume she can be in.
        await Ticks(115);
        Expect(star != null && GodotObject.IsInstanceValid(star), "star: Cleopastar threw no star");
        if (star == null || !GodotObject.IsInstanceValid(star)) { await Strike(stage); return; }

        // Spend her jumps in the air beside it, then put her inside it at the top of her rise.
        cleo.GlobalPosition = star.GlobalPosition + new Vector2(-Units.Px(4f), 0f);
        await Ticks(16);                      // the coyote grace takes one
        pad.Tap(ControlAction.Jump);          // the air jump takes the other
        await Ticks(32);                      // half a second: the top of the rise, at rest
        int left = cleo.JumpsRemaining;
        cleo.GlobalPosition = star.GlobalPosition;
        await Ticks(4);
        int inside = cleo.JumpsRemaining;
        pad.Tap(ControlAction.Jump);
        float launch = 0f;
        for (int i = 0; i < 12; i++)
        {
            await Ticks(1);
            launch = Mathf.Min(launch, cleo.Velocity.Y);
        }
        bool spent = !GodotObject.IsInstanceValid(star) || star.IsQueuedForDeletion();
        Expect(left == 0 && inside == 0, $"star: out of jumps, inside her star she must stay out of jumps ({left} before, {inside} inside) (#17)");
        Expect(spent && launch < -Units.Px(8f), $"star: a jump with no charges left inside her stationary star must spend it and launch her (spent {spent}, up {M(-launch)} m/s) (#17)");
        GD.Print($"  flow     star jump      out of jumps inside her star, a jump spends the star and launches her at {M(-launch)} m/s");
        await Strike(stage);
    }

    // ---------------------------------------------------------------- #14 damage credited

    private async Task ProbeDamageCredit()
    {
        var lines = new List<string>();

        // Sifu Pangda's palm is a melee blow to anything that listens for one (Kashaya).
        Node2D stage = BuildStage(0f, 4000f);
        CharacterController pangda = Fighter(stage, "Sifu Pangda", new Vector2(1000f, FloorY - 32f), out VirtualPad pad);
        CharacterController dummy = Dummy(stage, new Vector2(1000f + Units.Px(1.5f), FloorY - 32f));
        int melee = 0;
        pangda.MeleeDamageDealt += (_, dealt) => { if (dealt > 0f) melee++; };
        await Ticks(20);
        pad.Facing = 1f;
        pad.Tap(ControlAction.Attack);
        for (int i = 0; i < 90 && melee == 0; i++) await Ticks(1);
        Expect(melee > 0, "credit: Sifu Pangda's palm landed no melee blow a held item can hear — The Forgotten Kashaya never triggers for him (#14)");
        lines.Add($"Pangda's palm: {melee} melee blow(s)");
        await Strike(stage);

        // Cleopastar's star and PumpKing's head credit their thrower.
        foreach ((string id, float range) in new[] { ("Cleopastar", 4f), ("PumpKing", 3f) })
        {
            stage = BuildStage(0f, 4000f);
            CharacterController thrower = Fighter(stage, id, new Vector2(1000f, FloorY - 32f), out pad);
            dummy = Dummy(stage, new Vector2(1000f + Units.Px(range), FloorY - 32f));
            float credited = 0f;
            thrower.DamageDealt += dealt => credited += dealt;
            await Ticks(110);                 // Cleopastar's magazine starts empty: one Glow per 1.5 s
            pad.Facing = 1f;
            pad.Aim = dummy.GlobalPosition;
            pad.Tap(ControlAction.Attack);
            for (int i = 0; i < 240 && credited <= 0f; i++) await Ticks(1);
            Expect(credited > 0f, $"credit: {id}'s attack hit a target and credited her/him nothing — omni-vamp (Pome's Bravery) ignores it (#14)");
            lines.Add($"{id}: {credited:0.#} credited");
            await Strike(stage);
        }
        GD.Print($"  flow     credit         {string.Join(" · ", lines)}");
    }

    // ---------------------------------------------------------------- #15 the Fire Tornado

    private async Task ProbeFireTornado()
    {
        Node2D stage = BuildStage(0f, 4000f);
        var pome = (Pomegraknight)Fighter(stage, "Pomegraknight", new Vector2(1000f, FloorY - 32f), out VirtualPad pad);
        CharacterController dummy = Dummy(stage, new Vector2(1000f + Units.Px(2.5f), FloorY - 32f));
        var hits = new List<string>();
        void Heard(HitCue hit) { if (hit.Victim == dummy) hits.Add(hit.AttackAction); }
        CueBus.HitLanded += Heard;
        try
        {
            await Ticks(20);
            pad.Facing = 1f;
            pad.Tap(ControlAction.Skill1);    // Blush: the tornado's window opens
            await Ticks(8);
            pad.Tap(ControlAction.Attack);    // …and the next press is the tornado
            bool spinning = false, locked = false;
            for (int i = 0; i < 20 && !spinning; i++)
            {
                await Ticks(1);
                spinning = pome.TornadoSpinning;
                locked = pome.ControlsLocked;
            }
            float x0 = pome.GlobalPosition.X;
            pad.Hold(ControlAction.MoveRight);
            await Ticks(60);
            float walked = pome.GlobalPosition.X - x0;
            pad.ReleaseAll();
            for (int i = 0; i < 160 && pome.TornadoSpinning; i++) await Ticks(1);
            await Ticks(4);
            bool slashed = hits.Contains("ba");
            int ticks = hits.FindAll(action => action == "s1.tornado").Count;
            bool burning = (dummy.FeedbackState & FeedbackState.Burning) != 0;
            Expect(spinning && locked, $"tornado: the press after Blush must start the Fire Tornado and lock her in it (spinning {spinning}, locked {locked}) (#15)");
            Expect(!slashed && ticks >= 4, $"tornado: it must replace Slash 1, not come on top of it (a slash hit: {slashed}; {ticks} tornado ticks) (#15)");
            Expect(Mathf.Abs(walked) < Units.Px(0.3f), $"tornado: she must spin in place — she walked {M(walked)} m (#15)");
            Expect(!burning && !pome.ControlsLocked, $"tornado: it sets nothing burning (burning {burning}) and lets her go when it ends (locked {pome.ControlsLocked})");
            GD.Print($"  flow     tornado        Blush, then BA: the tornado instead of Slash 1 ({ticks} ticks, no slash), in place ({M(walked)} m), nothing burning");
        }
        finally
        {
            CueBus.HitLanded -= Heard;
        }
        await Strike(stage);
    }

    // ---------------------------------------------------------------- #11 the poop puddle

    private async Task ProbePoopPuddle()
    {
        Node2D stage = BuildStage(0f, 4000f);
        CharacterController pome = Fighter(stage, "Pomegraknight", new Vector2(1000f, FloorY - 32f), out _);
        await Ticks(20);
        float hp = pome.CurrentHP;
        var puddle = new PoopHazard { BoxSize = new Vector2(96f, 24f), Damage = 30f, Life = 3f };
        stage.AddChild(puddle);
        puddle.GlobalPosition = new Vector2(1000f, FloorY - 12f);
        await Ticks(200);                     // its whole 3 s and more
        float lost = hp - pome.CurrentHP;
        Expect(Mathf.IsEqualApprox(lost, 30f, 0.5f), $"puddle: standing in a seagull's puddle for its 3 s cost {lost:0} HP; FOES §4 says 30 on contact (#11)");
        GD.Print($"  flow     puddle         standing in a puddle for its 3 s costs {lost:0} HP (it hit four times a second before)");
        await Strike(stage);
    }
}
