namespace Fableland.Run.Dev;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using Fableland.Bosses;
using Fableland.Data;
using Fableland.Items;
using Fableland.Map;

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
/// • #19 — the wonder items live: perishing, the plantation, the Blessing's enchant, THE VOID's
///   phantoms, the Feather's rewind, the mushroom, the Claw, the ledger's save; DD on a real
///   stage; the passives and the mushroom's day on a real body.
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
            CheckEvents();
            CheckItemLedger();
            await CheckHudFaces();
            await CheckSettings();
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
            await ProbeDd();
            await ProbeItemBodies();
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

    // ---------------------------------------------------------------- #18 events

    private void CheckEvents()
    {
        foreach (string problem in EventDefs.Validate()) Expect(false, problem);
        Expect(EventDefs.All.Length >= 15, $"events: {EventDefs.All.Length} events for a run's 20–30 \"?\" nodes (#18)");
        Expect(EventDefs.All[0].Id == "traveller" && EventDefs.All[1].Id == "old_shrine" && EventDefs.All[2].Id == "abandoned_cart",
            "events: the list is append-only — its order is part of each node's pick");
        // A price gates its choice; the free one is always there.
        EventDef merchant = Array.Find(EventDefs.All, def => def.Id == "wandering_merchant");
        int priced = 0, free = 0;
        foreach (EventChoice choice in merchant.Pages[0].Choices)
        {
            if (!EventController.CanAfford(choice, 0)) priced++;
            else free++;
        }
        Expect(priced == 3 && free == 1 && EventController.CanAfford(merchant.Pages[0].Choices[0], 12) && !EventController.CanAfford(merchant.Pages[0].Choices[0], 11),
            "events: a priced choice must be taken only with its cores (the merchant's heart costs 12)");
        GD.Print($"  flow     events         {EventDefs.All.Length} events, every page with a free choice, every item real; prices gate their choices (wonder cores' first use)");
    }

    // ---------------------------------------------------------------- #20 settings

    /// <summary>The Settings screen: a slider per setting that changes it at once, and the
    /// values saved and read back — in a scratch folder, never the player's own.</summary>
    private async Task CheckSettings()
    {
        string savesBefore = SaveGameService.DirectoryOverride;
        string scratch = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "fableland-flow-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(scratch);
        SaveGameService.DirectoryOverride = scratch;
        try
        {
            Expect(SettingsStore.FilePath.StartsWith(scratch), $"settings: the file must follow the saves' redirection ({SettingsStore.FilePath})");
            FeedbackSettings.ResetToDefaults();
            FeedbackSettings.ScreenShake = 0.2f;
            FeedbackSettings.MusicVolume = 0.35f;
            Expect(SettingsStore.Save(), "settings: could not save");
            FeedbackSettings.ResetToDefaults();
            Expect(SettingsStore.Load() && Mathf.IsEqualApprox(FeedbackSettings.ScreenShake, 0.2f) && Mathf.IsEqualApprox(FeedbackSettings.MusicVolume, 0.35f),
                $"settings: a saved shake ×0.2 and music ×0.35 came back as ×{FeedbackSettings.ScreenShake} and ×{FeedbackSettings.MusicVolume}");

            var dialog = UI.SettingsDialog.Open(this);
            await Ticks(2);
            var sliders = new List<HSlider>();
            var stack = new Stack<Node>();
            stack.Push(dialog);
            while (stack.Count > 0)
                foreach (Node child in stack.Pop().GetChildren())
                {
                    if (child is HSlider slider) sliders.Add(slider);
                    stack.Push(child);
                }
            Expect(sliders.Count == 9, $"settings: the screen must have a slider for each of its nine settings ({sliders.Count})");
            if (sliders.Count > 0) sliders[0].Value = 0.6;
            Expect(Mathf.IsEqualApprox(FeedbackSettings.ScreenShake, 0.6f), $"settings: moving the shake slider must change the shake at once (×{FeedbackSettings.ScreenShake})");
            dialog.EmitSignal(AcceptDialog.SignalName.Confirmed);
            await Ticks(2);
            FeedbackSettings.ResetToDefaults();
            Expect(SettingsStore.Load() && Mathf.IsEqualApprox(FeedbackSettings.ScreenShake, 0.6f), "settings: closing the screen must save what it set");
            GD.Print("  flow     settings       nine sliders change the game at once; closing saves them; the file follows the saves' folder");
        }
        finally
        {
            FeedbackSettings.ResetToDefaults();
            SaveGameService.DirectoryOverride = savesBefore;
            try { System.IO.Directory.Delete(scratch, true); } catch { /* a leftover temp folder is harmless */ }
        }
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

    // ---------------------------------------------------------------- #19 the wonder items

    private static List<string> InstancesOf(RunState run, string defId) =>
        run.Items.FindAll(item => item.DefId == defId).ConvertAll(item => item.InstanceId);

    /// <summary>
    /// The item layer's run verbs on real runs (ITEMS.gdd §4, §7): a Weird Mushroom rots in two
    /// days wherever it lies; a Rotten Mushroom planted at a hub gives four after four; the
    /// Blessing of Shieldon enchants Possession (not on an Eternal item) and is spent; THE VOID
    /// raises a phantom every four days that a challenge draws nearer and whose touch costs a
    /// quarter of HP; Pixolotl's Feather turns the day back and becomes a Wish; a mushroom eaten
    /// grows max HP and rolls the morning after; the QiongFeng Claw loots a fallen realm, cracks
    /// it and throws you clear; and the ledger survives a save. Never touches <c>user://</c>.
    /// </summary>
    private void CheckItemLedger()
    {
        RunState run = RunState.Instance;
        string savesBefore = SaveGameService.DirectoryOverride;
        string scratch = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "fableland-items-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(scratch);
        SaveGameService.DirectoryOverride = scratch;
        try
        {
            run.NewRun("ITEMLEDGER");
            ProtagonistState lead = run.FindProtagonist(run.ActiveBuild[0]);

            // ── perishing: held or in the backpack, two day ends rot a mushroom ──
            run.AddItem("weird_mushroom");
            run.AddItem("weird_mushroom");
            run.HoldItem(lead, "weird_mushroom", fromBackpack: true);
            string held = lead.HeldItemInstanceId;
            string bagged = InstancesOf(run, "weird_mushroom")[0];
            Expect(run.Ledger.PerishDaysLeft.TryGetValue(bagged, out int clock) && clock == ItemLedger.WeirdMushroomPerishDays,
                $"items: a new Weird Mushroom must start a {ItemLedger.WeirdMushroomPerishDays}-day clock ({clock})");
            run.DayEndNotes.Clear();
            run.RunItemDay();
            Expect(run.ItemDefIdOf(bagged) == "weird_mushroom" && run.ItemDefIdOf(held) == "weird_mushroom", "items: after one day end the mushrooms are still fresh");
            run.RunItemDay();
            Expect(run.ItemDefIdOf(bagged) == "rotten_mushroom" && run.ItemDefIdOf(held) == "rotten_mushroom",
                $"items: after two day ends both mushrooms — held and bagged — must have rotted ({run.ItemDefIdOf(held)}, {run.ItemDefIdOf(bagged)}) (ITEMS §4.1)");
            Expect(run.DayEndNotes.Exists(note => note.Contains("went bad")), "items: the day summary must say a mushroom went bad");

            // ── the plantation: a Rotten Mushroom at a hub gives four Weird Mushrooms in four days ──
            MapNode hub = run.Graph.Nodes.Find(node => node.Kind == NodeKind.TransportHub && !node.Devoured && node.WorldIndex >= 0);
            run.CurrentNodeId = hub.Id;
            Expect(!run.TryPlant(0, held, out _), "items: a held item must not be planted (only the backpack's)");
            Expect(run.TryPlant(0, bagged, out string why), $"items: a Rotten Mushroom from the backpack must plant at a hub ({why})");
            Expect(run.ItemDefIdOf(bagged) == null && run.Ledger.PlotsAt(hub.Id)[0].DefId == "rotten_mushroom", "items: planting takes the item out of the inventory");
            for (int day = 0; day < 3; day++) run.RunItemDay();
            Expect(!run.TryHarvest(0, out _), "items: nothing is ripe after three days of a four-day crop");
            run.RunItemDay();
            int before = InstancesOf(run, "weird_mushroom").Count;
            Expect(run.TryHarvest(0, out why) && InstancesOf(run, "weird_mushroom").Count - before == 4 && run.Ledger.PlotsAt(hub.Id)[0].Empty,
                $"items: a ripe Rotten Mushroom must give four Weird Mushrooms and free its plot ({why})");

            // ── the Blessing of Shieldon: Possession onto another item, never an Eternal one ──
            run.UnholdItem(lead);
            run.AddItem(RunState.BlessingOfShieldonId);
            run.HoldItem(lead, RunState.BlessingOfShieldonId, fromBackpack: true);
            run.AddItem("fanchens_heart");
            run.AddItem(RunState.VoidItemId);
            string heart = InstancesOf(run, "fanchens_heart")[0];
            string voidId = InstancesOf(run, RunState.VoidItemId)[0];
            Expect(!run.TryEnchantPossession(voidId, out _), "items: THE VOID is Eternal — the Blessing must not take to it");
            Expect(run.TryEnchantPossession(heart, out why) && lead.HeldItemDefId == null,
                $"items: the Blessing must enchant FanChen's Heart and be spent ({why})");
            Expect(run.PossessionItems(null).Exists(item => item.InstanceId == heart), "items: an enchanted heart must work party-wide from the backpack");

            // ── THE VOID: a phantom every four days, at least three roads away; drawn nearer by a challenge ──
            run.Ledger.Phantoms.Clear();
            run.Ledger.PhantomClock = ItemLedger.PhantomEveryDays;
            MapNode start = run.Graph.StartNode;
            run.CurrentNodeId = start.Id;
            for (int day = 0; day < ItemLedger.PhantomEveryDays - 1; day++) run.RunItemDay();
            Expect(run.Ledger.Phantoms.Count == 0, "items: no phantom before its fourth day");
            run.RunItemDay();
            Expect(run.Ledger.Phantoms.Count == 1, $"items: THE VOID must raise a phantom on its fourth day ({run.Ledger.Phantoms.Count})");
            if (run.Ledger.Phantoms.Count == 1)
            {
                Dictionary<MapNode, int> roads = ItemLedger.Distances(run.Graph, start);
                int far = roads[run.FindNode(run.Ledger.Phantoms[0])];
                Expect(far >= ItemLedger.PhantomSpawnMinDistance, $"items: a phantom must rise at least {ItemLedger.PhantomSpawnMinDistance} roads away ({far})");
                run.Ledger.StepPhantoms(run.Graph, start.Id);
                int nearer = roads[run.FindNode(run.Ledger.Phantoms[0])];
                Expect(nearer == far - 1, $"items: a challenge must draw a phantom one road nearer ({far} → {nearer})");
            }
            MapNode fight = run.Graph.Nodes.Find(node => node.IsCombat && !node.Devoured && node.WorldIndex >= 0 && !run.CompletedNodeIds.Contains(node.Id));
            MapNode beside = null;
            foreach (MapEdge edge in run.Graph.EdgesOf(fight)) { beside = edge.Other(fight); break; }
            run.Ledger.Phantoms.Clear();
            run.Ledger.Phantoms.Add(beside.Id);
            foreach (ProtagonistState p in run.Owned) p.HpRatio = 1f;
            run.OnChallengeNode(fight);
            Expect(run.Ledger.Phantoms.Count == 0 && Mathf.IsEqualApprox(lead.HpRatio, 1f - ItemLedger.PhantomTouchHpShare) && run.LastPhantomNote.Length > 0,
                $"items: a phantom that reaches you must touch the party for a quarter of its HP, and go ({lead.HpRatio:0.00} HP, {run.Ledger.Phantoms.Count} left)");

            // ── the ledger in a save: everything above round-trips; an old save loads empty ──
            run.Ledger.Phantoms.Add(start.Id);
            run.Ledger.MushroomEaten.Add(lead.Id);
            run.Ledger.MushroomToday[lead.Id] = MushroomOutcome.Springy;
            run.AddItem("rotten_mushroom");
            run.TryPlant(1, InstancesOf(run, "rotten_mushroom")[^1], out _);
            string written = System.Text.Json.JsonSerializer.Serialize(run.Ledger.ToSave());
            RunSaveData copy = SaveGameService.CloneRunSave(new RunSaveData { ItemLedger = run.Ledger.ToSave() });
            string read = System.Text.Json.JsonSerializer.Serialize(ItemLedger.FromSave(copy?.ItemLedger).ToSave());
            Expect(written == read, $"items: the ledger must survive a save unchanged\n    wrote {written}\n    read  {read}");
            ItemLedger old = ItemLedger.FromSave(null);
            Expect(old.Phantoms.Count == 0 && old.Plots.Count == 0 && old.PhantomClock == ItemLedger.PhantomEveryDays, "items: a save from before v0.12.8 must load an empty ledger");

            // ── Pixolotl's Feather: back to this morning — place, HP, stamina — and a Wish ──
            run.NewRun("ITEMFEATHER");
            lead = run.FindProtagonist(run.ActiveBuild[0]);
            run.AddItem(RunState.FeatherItemId);
            run.HoldItem(lead, RunState.FeatherItemId, fromBackpack: true);
            MapNode morning = run.Graph.StartNode;
            MapNode away = null;
            foreach (MapEdge edge in run.Graph.EdgesOf(morning)) { away = edge.Other(morning); break; }
            run.CurrentNodeId = away.Id;
            run.Stamina = 0;
            foreach (ProtagonistState p in run.Owned) p.HpRatio = 0.3f;
            int dayBefore = run.Day;
            Expect(run.TryRewindToday(out why), $"items: a held Feather must turn the day back ({why})");
            Expect(run.CurrentNodeId == morning.Id && run.Stamina == RunState.MaxStamina && Mathf.IsEqualApprox(lead.HpRatio, 1f) && run.Day == dayBefore,
                $"items: the rewind must restore this morning's place, stamina and HP, not the day ({run.CurrentNodeId}, {run.Stamina}, {lead.HpRatio:0.00}, day {run.Day})");
            Expect(lead.HeldItemDefId == "pixolotls_wish" && !run.CanRewindToday(out _), "items: the Feather must become Pixolotl's Wish, which cannot rewind");

            // ── a Weird Mushroom eaten: +20 for good; the morning after, one roll ──
            run.AddItem("weird_mushroom");
            run.HoldItem(lead, "weird_mushroom", fromBackpack: true);
            int points = lead.MaxHpPercentPoints;
            Expect(run.TryEatMushroom(lead, out why) && lead.MaxHpPercentPoints == points + ItemLedger.MushroomMaxHpPoints && lead.HeldItemDefId == null,
                $"items: eating the mushroom must give +{ItemLedger.MushroomMaxHpPoints} max-HP points and use it up ({why})");
            run.RunItemDay();
            MushroomOutcome after = run.MushroomToday(lead.Id);
            Expect(after != MushroomOutcome.None || lead.MaxHpPercentPoints == points + 2 * ItemLedger.MushroomMaxHpPoints,
                "items: the morning after a mushroom brings a one-day effect, or 20 more points for good");
            Expect(after != MushroomOutcome.Frail || run.EffectiveMaxHpPoints(lead) == lead.MaxHpPercentPoints - ItemLedger.MushroomFrailPoints,
                "items: a frail day must take 40 points off the body's max HP");
            run.RunItemDay();
            Expect(run.MushroomToday(lead.Id) == MushroomOutcome.None, "items: a mushroom's effect lasts one day");
            var rolls = new int[6];
            for (int i = 0; i < 6000; i++) rolls[(int)ItemLedger.RollMushroom(new DetRandom("mushroom-roll-" + i))]++;
            bool spread = rolls[(int)MushroomOutcome.Hearty] is > 1500 and < 1860;
            for (int o = 1; o <= 4; o++) spread &= rolls[o] is > 900 and < 1260;
            Expect(spread, $"items: the mushroom's roll must be 18 % for each of (a)–(d) and 28 % for (e)+(f) — {string.Join(" ", rolls)} of 6000");

            // ── the QiongFeng Claw: a fallen capital's realm pays its unopened chests, cracks, throws you ──
            run.NewRun("ITEMCLAW");
            lead = run.FindProtagonist(run.ActiveBuild[0]);
            run.AddItem(RunState.ClawItemId);
            run.HoldItem(lead, RunState.ClawItemId, fromBackpack: true);
            MapNode capital = run.Graph.Nodes.Find(node => node.Kind == NodeKind.Boss && node.Level == 4 && node.WorldIndex >= 1 && node.Zone != RunState.ClawWorldAbbr);
            int chests = 0, cores = 0, atk = 0;
            foreach (MapNode node in run.Graph.Nodes)
            {
                if (node.Zone != capital.Zone || !node.IsCombat || node == capital || node.Devoured || run.CompletedNodeIds.Contains(node.Id)) continue;
                chests++;
                switch (RunState.EffectiveMission(node))
                {
                    case MissionType.Collection: cores += Missions.MissionTable.CollectionCores(node.Level); break;
                    case MissionType.Protect: cores += 10; break;
                    case MissionType.Slaughter: atk += 10; break;
                }
            }
            int coresBefore = run.WonderCores, atkBefore = lead.BonusAtk;
            run.CurrentNodeId = capital.Id;
            run.CompletedNodeIds.Add(capital.Id);
            run.OnCapitalWon(capital, firstTime: true);
            MapNode landed = run.FindNode(run.CurrentNodeId);
            bool cracked = run.Graph.Nodes.TrueForAll(node => node.Zone != capital.Zone || node.WorldIndex < 0 || node.Devoured);
            Expect(!run.OwnsItem(RunState.ClawItemId), "items: the claw must faint after it fires");
            Expect(cracked, "items: the fallen capital's realm must crack down (every node of it devoured)");
            Expect(landed != null && landed.Kind == NodeKind.TransportHub && landed.Zone != capital.Zone && !landed.Devoured,
                $"items: the claw must throw you to a hub of another realm ({landed?.Id})");
            Expect(run.WonderCores - coresBefore == cores && lead.BonusAtk - atkBefore == atk,
                $"items: {chests} unopened chests must pay {cores} cores and +{atk} ATK (paid {run.WonderCores - coresBefore} and +{lead.BonusAtk - atkBefore})");
            bool plFound = false;
            for (int s = 0; s < 12 && !plFound; s++)
            {
                run.NewRun("ITEMPALACE" + s);
                MapNode palace = run.Graph.Nodes.Find(node => node.Kind == NodeKind.Boss && node.Level == 4 && node.Zone == RunState.ClawWorldAbbr);
                if (palace == null) continue;
                plFound = true;
                run.CompletedNodeIds.Add(palace.Id);
                run.OnCapitalWon(palace, firstTime: true);
                Expect(run.OwnsItem(RunState.ClawItemId), "items: the Palace of LOOING's first fall must yield the QiongFeng Claw");
            }
            Expect(plFound, "items: no seed of twelve dealt the Palace of LOOING — the claw's grant went untested");
            GD.Print($"  flow     items          mushrooms rot in {ItemLedger.WeirdMushroomPerishDays} days and grow back in 4; the Blessing enchants; " +
                     $"THE VOID's phantoms rise, close in and touch for {ItemLedger.PhantomTouchHpShare:P0}; the Feather rewinds; the Claw loots {chests} chests and cracks the realm");
        }
        finally
        {
            SaveGameService.DirectoryOverride = savesBefore;
            try { System.IO.Directory.Delete(scratch, true); } catch { /* a leftover temp folder is harmless */ }
        }
    }

    /// <summary>DD let go (ITEMS.gdd §7.3): it crawls to the target, devours it for 200 once,
    /// turns at the wall, comes back and is reclaimed by walking into it; its two days start.</summary>
    private async Task ProbeDd()
    {
        Node2D stage = BuildStage(600f, 2200f);
        CharacterController pome = Fighter(stage, "Pomegraknight", new Vector2(1000f, FloorY - 32f), out VirtualPad pad);
        CharacterController target = Dummy(stage, new Vector2(1400f, FloorY - 32f));
        await Ticks(20);
        var state = new ProtagonistState("Pomegraknight") { HeldItemDefId = RunState.DdItemId, HeldItemInstanceId = "dd-check" };
        var dd = new ItemRuntime(pome, state);
        pad.Facing = 1f;
        pad.Hold(ControlAction.MoveRight);
        await Ticks(2);
        pad.ReleaseAll();
        await Ticks(4);
        float hp = target.CurrentHP;
        bool used = dd.TryUse(out string reason);
        Expect(used && DdCreature.IsLoose("dd-check"), $"dd: DD must be let go ({reason})");
        string again = null;
        Expect(state.HeldItemDayCooldownRemaining == 2 && !dd.TryUse(out again) && (again ?? "").Contains("day"),
            $"dd: letting DD go must start its two days ({state.HeldItemDayCooldownRemaining}, '{again}')");
        DdCreature creature = DdCreature.Loose.Find(c => c.InstanceId == "dd-check");
        float afterFirst = -1f;
        bool turned = false, home = false;
        int ticks = 0;
        for (; ticks < 1600 && !home; ticks++)
        {
            await Ticks(1);
            if (creature != null && IsInstanceValid(creature) && creature.Direction < 0f) turned = true;
            if (afterFirst < 0f && target.CurrentHP < hp) afterFirst = target.CurrentHP;
            home = !DdCreature.IsLoose("dd-check");
        }
        float dealt = afterFirst < 0f ? 0f : hp - afterFirst;
        Expect(dealt >= 100f, $"dd: DD must devour a target in its path for {DdCreature.Damage:0} (it dealt {dealt:0})");
        Expect(turned, "dd: DD must turn around at the wall");
        Expect(home, "dd: walking into DD must reclaim it");
        Expect(afterFirst < 0f || Mathf.IsEqualApprox(target.CurrentHP, afterFirst), $"dd: a target is devoured once per release ({target.CurrentHP:0} after {afterFirst:0})");
        dd.Dispose();
        GD.Print($"  flow     dd             DD devours the target for {dealt:0}, turns at the wall and is reclaimed {ticks / 60f:0.0} s after it was let go");
        await Strike(stage);
    }

    /// <summary>The items' passives and the mushroom's day on a real body: a Possession Feather
    /// lightens it and lets go; the Blessing hastens it; a mushroom asks the run to be eaten and
    /// its max HP grows keeping its share; swapped keys walk it the other way.</summary>
    private async Task ProbeItemBodies()
    {
        Node2D stage = BuildStage(0f, 4000f);
        CharacterController body = Fighter(stage, "Pomegraknight", new Vector2(2000f, FloorY - 32f), out VirtualPad pad);
        await Ticks(10);
        ItemRuntime feather = ItemRuntime.ForPossession(body, RunState.FeatherItemId, "feather-check");
        Expect(Mathf.IsEqualApprox(body.GravityScale, 0.8f) && !feather.IsUsable && !feather.TryUse(out _),
            $"items: a Possession Feather must lighten the body (gravity ×{body.GravityScale:0.00}) and lend no skill");
        feather.Dispose();
        Expect(Mathf.IsEqualApprox(body.GravityScale, 1f), "items: unbinding the Feather must give the body its weight back");
        var blessing = new ItemRuntime(body, new ProtagonistState("Pomegraknight") { HeldItemDefId = RunState.BlessingOfShieldonId, HeldItemInstanceId = "blessing-check" });
        Expect(Mathf.IsEqualApprox(body.MoveSpeedScale, 1.1f), $"items: the Blessing of Shieldon must add 10% move speed (×{body.MoveSpeedScale:0.00})");
        blessing.Dispose();
        var mushroom = new ItemRuntime(body, new ProtagonistState("Pomegraknight") { HeldItemDefId = RunState.WeirdMushroomId, HeldItemInstanceId = "mushroom-check" });
        Expect(mushroom.TryUse(out _) && mushroom.PendingVerb == ItemRunVerb.EatMushroom && mushroom.RebindRequested,
            "items: using a Weird Mushroom must ask the run to eat it");
        mushroom.Dispose();
        body.HydrateRun(body.MaxHP, 0, 0.5f, 0, 0);
        float max = body.MaxHP;
        body.RescaleMaxHp(max * 1.2f);
        Expect(Mathf.IsEqualApprox(body.MaxHP, max * 1.2f) && Mathf.IsEqualApprox(body.HpRatio, 0.5f, 1e-4f),
            $"items: a mushroom eaten mid-fight must grow max and current HP together ({body.CurrentHP:0}/{body.MaxHP:0})");
        body.UseControls(new MirroredControls(pad));
        float x0 = body.GlobalPosition.X;
        pad.Hold(ControlAction.MoveRight);
        await Ticks(30);
        pad.ReleaseAll();
        Expect(body.GlobalPosition.X < x0 - Units.Px(1f), $"items: with left and right swapped, holding right must walk left ({M(body.GlobalPosition.X - x0)} m)");
        body.UseControls(pad);
        GD.Print("  flow     item bodies    a Possession Feather lightens and lets go; the Blessing hastens; a mushroom grows max HP keeping its share; swapped keys walk the other way");
        await Strike(stage);
    }
}
