namespace Fableland.Run.Dev;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using Fableland.Data;
using Fableland.Missions;
using Fableland.Realms;

/// <summary>
/// The realm core, headless (`Docs/Tech/T50-REALMS.md` §11, `Docs/Realms.gdd`). A flat main stage
/// and, far above it, flat test patches this check hands out itself (it is the space provider).
/// It proves the projection; who goes in (anyone in scope, never an objective); silence on the
/// caster's enemies only; the filter; no nesting; walking out over the border at the ratio's pace;
/// the entry nudge; the 0.5 s re-entry grace; the nearest open spot off solid ground; a close
/// returning everyone; a ride's timer through a moving anchor (a wormhole); the map's edge.
///
///     Godot --headless --path . res://Scenes/Run/RealmCheck.tscn      # exit 0 = clean
/// </summary>
public partial class RealmCheck : Node2D, IRealmSpaces
{
    private readonly List<string> _problems = new();
    private int _checks;
    private const int SpeedUp = 8;
    private const float Tick = 1f / 60f;
    private const float FloorY = 600f;
    private const float StageWidth = 6000f;
    private readonly List<VirtualPad> _pads = new();

    // Test realms: ratio 2 like the Black Prison; one with a block where a body would land; one
    // that carries a body for half a second (the tsunami's kind).
    private const string Ratio2 = "realm.test.ratio2";
    private const string Blocked = "realm.test.blocked";
    private const string Ride = "realm.test.ride";

    private static readonly Vector2 A = new(2000f, FloorY - 32f);   // the main anchor: a fighter's centre on the floor

    public Rect2 MainBounds => new(0f, FloorY - 2000f, StageWidth, 2000f);

    public override void _Process(double delta) { foreach (VirtualPad pad in _pads) pad.StepProcess(); }
    public override void _PhysicsProcess(double delta) { foreach (VirtualPad pad in _pads) pad.StepPhysics(); }

    public override async void _Ready()
    {
        AddToGroup(Realm.SpacesGroup);
        RealmTable.Register(new RealmDef { Id = Ratio2, Ratio = 2f, ArtExtentM = 24f, Pull = RealmPull.Continuous, SilenceCastersEnemies = true });
        RealmTable.Register(new RealmDef { Id = Blocked, Ratio = 2f, ArtExtentM = 24f, Pull = RealmPull.OnOpen });
        RealmTable.Register(new RealmDef { Id = Ride, Ratio = 1f, ArtExtentM = 30f, Pull = RealmPull.OnOpen, RideSeconds = 0.5f });

        int ticksPerSecond = Engine.PhysicsTicksPerSecond;
        double timeScale = Engine.TimeScale;
        try
        {
            Engine.PhysicsTicksPerSecond = ticksPerSecond * SpeedUp;
            Engine.TimeScale = SpeedUp;
            await Ticks(1);
            await ProbeInAndOut();
            await ProbeOpenSpot();
            await ProbeWormhole();
            await ProbeMapEdge();
            await ProbeAdoptAndAdmit();
        }
        catch (Exception e)
        {
            _problems.Add("realm check threw: " + e);
        }
        finally
        {
            Engine.PhysicsTicksPerSecond = ticksPerSecond;
            Engine.TimeScale = timeScale;
        }
        foreach (string problem in _problems) GD.Print("FAIL  " + problem);
        GD.Print(_problems.Count == 0 ? $"RealmCheck: OK ({_checks} checks)" : $"RealmCheck: {_problems.Count} problem(s) in {_checks} checks");
        GetTree().Quit(_problems.Count == 0 ? 0 : 1);
    }

    private void Expect(bool ok, string what)
    {
        _checks++;
        if (!ok) _problems.Add(what);
    }

    private async Task Ticks(int count)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }

    private static string M(float px) => (px / Units.PixelsPerMeter).ToString("0.00");

    // ---------------------------------------------------------------- the space provider

    private sealed class Prepared
    {
        public RealmSpace Space;
        public bool InUse;
    }

    private readonly List<Prepared> _spaces = new();

    public RealmSpace Acquire(RealmDef def)
    {
        foreach (Prepared prepared in _spaces)
            if (!prepared.InUse && prepared.Space.Def.Id == def.Id) { prepared.InUse = true; return prepared.Space; }
        var built = new Prepared { Space = BuildPatch(def, _spaces.Count), InUse = true };
        _spaces.Add(built);
        return built.Space;
    }

    public void Release(RealmSpace space)
    {
        foreach (Prepared prepared in _spaces)
            if (prepared.Space == space) prepared.InUse = false;
    }

    /// <summary>A flat patch far above the stage: its floor a fighter's half-height below the anchor,
    /// so a fighter standing level with the main anchor stands on it.</summary>
    private RealmSpace BuildPatch(RealmDef def, int slot)
    {
        var root = new Node2D { Name = "Space" + slot };
        AddChild(root);
        var anchor = new Vector2(2000f, -40000f - 20000f * slot);
        float extent = Units.Px(def.ArtExtentM);
        AddBox(root, new Rect2(anchor.X - extent, anchor.Y + 32f, 2f * extent, 200f));
        // The blocked patch: rock exactly where a body two metres right of the main anchor lands.
        if (def.Id == Blocked) AddBox(root, new Rect2(anchor.X + Units.Px(4f) - 60f, anchor.Y - 120f, 120f, 152f));
        float half = extent + Units.Px(6f);
        return new RealmSpace { Def = def, Anchor = anchor, Root = root, Bounds = new Rect2(anchor.X - half, anchor.Y - half, 2f * half, 2f * half) };
    }

    // ---------------------------------------------------------------- stage and bodies

    private Node2D BuildStage()
    {
        var stage = new Node2D { Name = "Stage" };
        AddChild(stage);
        AddBox(stage, new Rect2(0f, FloorY, StageWidth, 200f));
        AddBox(stage, new Rect2(-40f, FloorY - 2000f, 40f, 2200f));
        AddBox(stage, new Rect2(StageWidth, FloorY - 2000f, 40f, 2200f));
        return stage;
    }

    private static void AddBox(Node2D parent, Rect2 rect)
    {
        var body = new StaticBody2D { Position = rect.GetCenter(), CollisionLayer = 0, CollisionMask = 0 };
        body.SetCollisionLayerValue(Units.LayerGround, true);
        body.AddChild(new CollisionShape2D { Shape = new RectangleShape2D { Size = rect.Size } });
        parent.AddChild(body);
    }

    private CharacterController Fighter(Node2D stage, Vector2 at, out VirtualPad pad)
    {
        var body = ResourceLoader.Load<PackedScene>("res://Scenes/Pomegraknight.tscn").Instantiate<CharacterController>();
        stage.AddChild(body);
        body.GlobalPosition = at;
        body.SetSpawnPoint(at);
        pad = new VirtualPad();
        body.UseControls(pad);
        _pads.Add(pad);
        return body;
    }

    private static CrabFoe Crab(Node2D stage, float x, ulong seed)
    {
        var crab = ResourceLoader.Load<PackedScene>("res://Scenes/Foes/CrabFoe.tscn").Instantiate<CrabFoe>();
        crab.Rng = new RandomNumberGenerator { Seed = seed };
        stage.AddChild(crab);
        crab.GlobalPosition = new Vector2(x, FloorY - 22f);
        crab.Init(1);
        crab.OverrideMaxHp(5000f);
        return crab;
    }

    private static DestroyObjective Objective(Node2D stage, float x)
    {
        var objective = ResourceLoader.Load<PackedScene>("res://Scenes/Missions/DestroyObjective.tscn").Instantiate<DestroyObjective>();
        stage.AddChild(objective);
        objective.GlobalPosition = new Vector2(x, FloorY - 40f);
        objective.Init(1);
        return objective;
    }

    private async Task Strike(Node2D stage)
    {
        _pads.Clear();
        stage.QueueFree();
        await Ticks(3);
    }

    // ---------------------------------------------------------------- the probes

    /// <summary>The projection, who goes in, silence, the filter, no nesting, the walk out over the
    /// border, the grace, the entry nudge, and a close.</summary>
    private async Task ProbeInAndOut()
    {
        Node2D stage = BuildStage();
        CharacterController player = Fighter(stage, A + new Vector2(Units.Px(3f), 0f), out VirtualPad pad);
        CrabFoe near = Crab(stage, A.X - Units.Px(5f), 3);
        CrabFoe far = Crab(stage, A.X + Units.Px(15f), 5);
        DestroyObjective objective = Objective(stage, A.X - Units.Px(2f));
        near.ApplyTrapped(60f); far.ApplyTrapped(60f);
        await Ticks(20);
        Vector2 playerMain = player.GlobalPosition, nearMain = near.GlobalPosition;

        RealmInstance realm = Realm.Open(player, Ratio2, A, Units.Px(8f), CombatSide.Player, "check.realm");
        Expect(realm != null, "realm: Open with a free space must open");
        if (realm == null) { await Strike(stage); return; }

        Vector2 p = new(123.5f, -456.25f);
        Expect((realm.ToMain(realm.ToSub(p)) - p).Length() < 0.01f, "realm: main → sub → main must come back to the same point");
        Expect(Mathf.Abs(realm.ToSub(A + new Vector2(Units.Px(3f), 0f)).DistanceTo(realm.Space.Anchor) - Units.Px(6f)) < 0.01f,
            "realm: at ratio 2, 3 m from the main anchor is 6 m from the sub anchor");
        Expect(Mathf.IsEqualApprox(realm.ExitRadiusPx, Units.Px(16f)), $"realm: an 8 m region at ratio 2 has a 16 m exit border ({M(realm.ExitRadiusPx)} m)");

        Expect(Realm.Of(player) == realm && Realm.Of(near) == realm,
            "realm: everyone in scope goes in as it opens — the caster's own side too");
        Expect(Realm.Of(far) == null, "realm: a crab 15 m off, outside the 8 m region, must stay in the main realm");
        Expect(Realm.Of(objective) == null, "realm: a mission objective must never go in (Realms.gdd §10)");
        Vector2 playerSub = player.GlobalPosition;
        Vector2 projected = realm.ToSub(playerMain);
        // Across exactly; up or down only as far as the nearest open spot needed (the ratio doubles
        // the few px a fighter's centre sits off the anchor's height, into the patch's floor).
        Expect(Mathf.Abs(playerSub.X - projected.X) < 1f && Mathf.Abs(playerSub.Y - projected.Y) <= Units.Px(0.5f),
            $"realm: the player must land at their projected spot ({M(playerSub.X - projected.X)} m across, {M(playerSub.Y - projected.Y)} m up or down)");
        Expect(near.Silenced && !player.Silenced, $"realm: silence falls on the caster's enemies only (crab {near.Silenced}, player {player.Silenced})");

        Expect(Realm.SameAs(player, near) && !Realm.SameAs(player, far), "realm: SameAs must tell who shares a realm");
        var seen = new List<ICombatTarget>(Realm.HostileTo(player, CombatSide.Player));
        Expect(seen.Contains(near) && !seen.Contains(far), "realm: HostileTo must list only the asker's realm");
        Expect(Realm.Open(player, Ratio2, player.GlobalPosition, Units.Px(4f), CombatSide.Player, "check.nest") == null,
            "realm: a realm must not open from inside one (no nesting in v1)");

        // Out over the border, walking: normal speed inside, so the 16 m border takes twice the time
        // the 8 m region would.
        pad.Hold(ControlAction.MoveRight);
        int ticks = 0;
        for (; ticks < 60 * 4 && Realm.Of(player) == realm; ticks++) await Ticks(1);
        pad.Hold(ControlAction.MoveRight, false);
        float walked = ticks * Tick;
        float wants = (realm.ExitRadiusPx + player.HitRadius - (playerSub - realm.Space.Anchor).Length()) / player.MoveSpeed;
        Expect(Realm.Of(player) == null && realm.LastExit == RealmExit.Border, "realm: walking past the exit border must bring the player out");
        Expect(Mathf.Abs(walked - wants) <= 6f * Tick, $"realm: the walk out took {walked:0.00} s; at the fighter's speed it is {wants:0.00} s");
        float clear = player.GlobalPosition.DistanceTo(realm.MainAnchor) - player.HitRadius;
        Expect(clear > Units.Px(8f) && clear <= Units.Px(8f + RealmInstance.ExitNudgeM) + 2f,
            $"realm: out over the border, the player must land just clear of the region ({M(clear)} m from it, by the hit circle)");
        Expect(!player.Silenced, "realm: coming out never leaves a status on the player");

        // The grace: put straight back inside, not taken for half a second.
        BodyPlacement.Teleport(player, A + new Vector2(Units.Px(2f), 0f));
        await Ticks(12);
        Expect(Realm.Of(player) == null, "realm: within 0.5 s of coming out the region must not take the player back");
        await Ticks(30);
        Expect(Realm.Of(player) == realm, "realm: once the grace is over, standing in the region takes the player in again");

        // The entry nudge: a body caught at the region's rim lands inside the exit border.
        CrabFoe rim = Crab(stage, A.X - Units.Px(8.5f), 7);
        rim.ApplyTrapped(60f);
        await Ticks(4);
        float depth = rim.GlobalPosition.DistanceTo(realm.Space.Anchor);
        Expect(Realm.Of(rim) == realm && depth <= realm.ExitRadiusPx - Units.Px(RealmInstance.EntryNudgeM) + 1f,
            $"realm: a body caught at the rim must land at least {RealmInstance.EntryNudgeM} m inside the exit border ({M(realm.ExitRadiusPx - depth)} m inside)");

        // The close: everyone back at their projected spots, statuses gone.
        Vector2 nearBack = realm.ToMain(near.GlobalPosition);
        realm.Close();
        await Ticks(3);
        Expect(Realm.Of(near) == null && Realm.Of(player) == null && Realm.Of(rim) == null, "realm: a close must bring everyone out");
        Expect((near.GlobalPosition - nearBack).Length() < Units.Px(0.6f) && !near.Silenced,
            $"realm: closed, the crab must stand at its projected spot ({M((near.GlobalPosition - nearBack).Length())} m off), unsilenced");
        Expect(Mathf.Abs(near.GlobalPosition.X - nearMain.X) < Units.Px(0.6f), "realm: a crab that never moved inside comes back where it was");
        GD.Print($"  realm    in and out     ratio 2: in at 2× the offset; the caster's side in too, an objective never; out over a {M(realm.ExitRadiusPx)} m border in {walked:0.00} s; 0.5 s grace; a rim catch {M(realm.ExitRadiusPx - depth)} m inside; a close returns all");
        await Strike(stage);
    }

    /// <summary>A body whose projected spot is inside the patch's rock is moved to the nearest open spot.</summary>
    private async Task ProbeOpenSpot()
    {
        Node2D stage = BuildStage();
        CharacterController player = Fighter(stage, A + new Vector2(Units.Px(2f), 0f), out _);
        await Ticks(10);
        RealmInstance realm = Realm.Open(player, Blocked, A, Units.Px(8f), CombatSide.Player, "check.blocked");
        Expect(realm != null && Realm.Of(player) == realm, "realm: the blocked realm must open and take the player");
        if (realm != null)
        {
            Vector2 wanted = realm.ToSub(A + new Vector2(Units.Px(2f), 0f));
            var query = new PhysicsShapeQueryParameters2D { CollisionMask = 1u << (Units.LayerGround - 1) };
            foreach (Node child in player.GetChildren())
                if (child is CollisionShape2D { Shape: not null, Disabled: false } shape) { query.Shape = shape.Shape; query.Transform = shape.GlobalTransform; break; }
            int overlaps = GetWorld2D().DirectSpaceState.IntersectShape(query, 1).Count;
            float moved = player.GlobalPosition.DistanceTo(wanted);
            Expect(overlaps == 0 && moved > 1f && moved < Units.Px(5f),
                $"realm: a landing inside rock must move to the nearest open spot (overlapping {overlaps}, moved {M(moved)} m)");
            GD.Print($"  realm    open spot      a landing in rock moved {M(moved)} m to the nearest open spot");
            realm.Close();
        }
        await Strike(stage);
    }

    /// <summary>A ride: carried half a second while the main anchor travels, out where it is now.</summary>
    private async Task ProbeWormhole()
    {
        Node2D stage = BuildStage();
        CrabFoe crab = Crab(stage, A.X, 11);
        CharacterController watcher = Fighter(stage, A + new Vector2(Units.Px(20f), 0f), out _);
        for (int i = 0; i < 10; i++) { await Ticks(1); crab.ApplyTrapped(1f); }
        RealmInstance realm = Realm.Open(watcher, Ride, crab.GlobalPosition, Units.Px(1f), CombatSide.Player, "check.ride");
        Expect(realm != null && Realm.Of(crab) == realm, "realm: the ride realm must take the crab");
        if (realm == null) { await Strike(stage); return; }
        float speed = Units.Px(10f);
        int ticks = 0;
        for (; ticks < 60 && Realm.Of(crab) == realm; ticks++)
        {
            realm.MainAnchor += new Vector2(speed * Tick, 0f);
            crab.ApplyTrapped(1f);
            await Ticks(1);
        }
        float rode = ticks * Tick;
        Expect(Realm.Of(crab) == null && realm.LastExit == RealmExit.Timer && Mathf.Abs(rode - Def(Ride).RideSeconds) <= 3f * Tick,
            $"realm: a ride must bring its rider out by its own timer after {Def(Ride).RideSeconds} s (out after {rode:0.00} s, {realm.LastExit})");
        float clearOfIt = Units.Px(1f) + crab.HitRadius + Units.Px(RealmInstance.ExitNudgeM);
        float wants = realm.MainAnchor.X + clearOfIt;
        Expect(Mathf.Abs(crab.GlobalPosition.X - wants) < Units.Px(0.6f),
            $"realm: through a moving anchor the rider comes out where it is now ({M(crab.GlobalPosition.X - A.X)} m from where it went in; wanted {M(wants - A.X)})");
        GD.Print($"  realm    wormhole       a {Def(Ride).RideSeconds} s ride while the anchor ran 10 m/s: out {M(crab.GlobalPosition.X - A.X)} m from where it went in");
        realm.Close();
        await Strike(stage);
    }

    /// <summary>A way out past the map's edge lands on the edge.</summary>
    private async Task ProbeMapEdge()
    {
        Node2D stage = BuildStage();
        CrabFoe crab = Crab(stage, A.X, 13);
        CharacterController watcher = Fighter(stage, A + new Vector2(Units.Px(20f), 0f), out _);
        for (int i = 0; i < 10; i++) { await Ticks(1); crab.ApplyTrapped(1f); }
        RealmInstance realm = Realm.Open(watcher, Ride, crab.GlobalPosition, Units.Px(1f), CombatSide.Player, "check.edge");
        if (realm == null) { Expect(false, "realm: the ride realm must open for the edge probe"); await Strike(stage); return; }
        realm.MainAnchor = new Vector2(StageWidth + Units.Px(200f), A.Y);
        for (int i = 0; i < 60 && Realm.Of(crab) == realm; i++) { crab.ApplyTrapped(1f); await Ticks(1); }
        Expect(Realm.Of(crab) == null && crab.GlobalPosition.X <= MainBounds.End.X - crab.HitRadius + 1f,
            $"realm: a way out past the map's edge must land on the edge (at {M(crab.GlobalPosition.X)} m of a {M(StageWidth)} m map)");
        GD.Print($"  realm    map edge       a way out 200 m past the edge lands at {M(crab.GlobalPosition.X)} m of {M(StageWidth)}");
        realm.Close();
        await Strike(stage);
    }

    /// <summary>Two ways in besides the pull. A body that turns up inside the space — a protagonist
    /// switched in by Tab where the one before stood — is a member from then on, under the realm's
    /// rules, and comes out with everyone. <c>Admit</c> brings a body in from outside to a chosen
    /// spot (PumpKing's recast), and refuses one that is in another realm (no nesting).</summary>
    private async Task ProbeAdoptAndAdmit()
    {
        Node2D stage = BuildStage();
        CharacterController player = Fighter(stage, A, out _);
        CrabFoe stray = Crab(stage, A.X + Units.Px(30f), 17);
        CrabFoe called = Crab(stage, A.X - Units.Px(25f), 19);
        CrabFoe other = Crab(stage, A.X + Units.Px(60f), 23);
        for (int i = 0; i < 10; i++) { await Ticks(1); stray.ApplyTrapped(1f); called.ApplyTrapped(1f); other.ApplyTrapped(1f); }
        RealmInstance realm = Realm.Open(player, Ratio2, A, Units.Px(8f), CombatSide.Player, "check.adopt");
        Expect(realm != null && Realm.Of(player) == realm && Realm.Of(stray) == null && Realm.Of(called) == null,
            "realm: the adopt probe's realm must take the player and leave the far crabs out");
        if (realm == null) { await Strike(stage); return; }

        BodyPlacement.Teleport(stray, realm.Space.Anchor + new Vector2(Units.Px(4f), 0f));
        stray.ApplyTrapped(1f);
        await Ticks(2);
        Expect(realm.Holds(stray) && stray.Silenced,
            "realm: a body that turns up inside the space must be a member from then on, under the realm's rules (silenced: the caster's enemy)");

        Vector2 spot = realm.Space.Anchor + new Vector2(-Units.Px(3f), 0f);
        bool admitted = realm.Admit(called, spot);
        Expect(admitted && realm.Holds(called) && Realm.Of(called) == realm && called.Silenced && called.GlobalPosition.DistanceTo(spot) <= Units.Px(1f),
            $"realm: Admit must bring a body in from outside at its spot, under the realm's rules ({M(called.GlobalPosition.DistanceTo(spot))} m from it)");

        RealmInstance second = Realm.Open(stage, Blocked, other.GlobalPosition, Units.Px(1f), CombatSide.Player, "check.second");
        Expect(second != null && Realm.Of(other) == second, "realm: the second realm must take its crab");
        Expect(!realm.Admit(other, spot) && (second == null || Realm.Of(other) == second),
            "realm: Admit must refuse a body that is in another realm (no nesting)");
        second?.Close();

        // A member moved out by other means (a respawn teleports the player to the map's spawn):
        // let go where it is — unsilenced, not projected back out of the border.
        Vector2 respawn = new(A.X - Units.Px(25f), FloorY - 22f);
        BodyPlacement.Teleport(called, respawn);
        called.ApplyTrapped(1f);
        await Ticks(2);
        Expect(!realm.Holds(called) && Realm.Of(called) == null && !called.Silenced && called.GlobalPosition.DistanceTo(respawn) < Units.Px(0.6f),
            $"realm: a member moved out of the space by other means must be let go where it is, unsilenced ({M(called.GlobalPosition.DistanceTo(respawn))} m from where it was put)");

        Vector2 strayBack = realm.ToMain(stray.GlobalPosition);
        realm.Close();
        await Ticks(3);
        Expect(Realm.Of(stray) == null && !stray.Silenced, "realm: a close must bring out the adopted body too, unsilenced");
        Expect((stray.GlobalPosition - strayBack).Length() < Units.Px(0.6f),
            $"realm: the adopted body comes out at its projected spot ({M((stray.GlobalPosition - strayBack).Length())} m off)");
        GD.Print("  realm    adopt, admit   a body turning up inside is a member (silenced), Admit brings one in at its spot and refuses one in another realm; one moved out by a respawn is let go where it is; a close returns the adopted");
        await Strike(stage);
    }

    private static RealmDef Def(string id) => RealmTable.TryGet(id, out RealmDef def) ? def : null;
}
