namespace Fableland.MapCreation.Dev;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using Fableland.MapCreation.Data;
using Fableland.MapCreation.Playtest;
using Fableland.MapCreation.Runtime;

/// <summary>
/// The soft-volume half of the content check: water, currents, bubbles and waterfalls
/// (MapCreation.gdd §2.2a).
///
/// 1. Parity — the arena and the editor Playtest must build the same volumes, with the same
///    tuning, from one map; a sea laid on a Farview layer included.
/// 2. Probe — it then puts the real Pomegraknight (and a crab) into each shipped water kind
///    through the Playtest, runs the physics for about ten simulated seconds, and measures
///    what the body actually does: how fast it sinks, swims and dives, how far a jump
///    carries, what a current, a bubble column and a waterfall do to it. Those are the
///    numbers the tiles' tuning promises, so a change to the controller or to the tuning
///    that breaks the promise fails here rather than in a playtest.
///
/// The probe drives the game's input actions and reads positions; it starts no fight.
/// </summary>
public partial class MapContentCheck
{
    // ---------------------------------------------------------------- arena / Playtest parity

    private readonly record struct VolumeSnapshot(Vector2 Position, bool Medium, float Stagnation,
        float GravityScale, float SinkSpeed, float Drag, Vector2 Flow);

    private const string SeaTile = "softvolume.ln_water";
    private const string CurrentTile = "softvolume.ln_current";
    private const string BubbleTile = "softvolume.ln_bubbles";
    private const string WaterfallTile = "softvolume.bm_waterfall";
    private const string SwampTile = "softvolume.tf_water";
    private const string FloorTile = "ground.stone";

    /// <summary>A sea on a non-looping Farview layer with Collision on, a flipped current and
    /// a bubble column on the battlefield — plus a looping Farview and a Farview without
    /// Collision, whose water must stay scenery.</summary>
    private static MapDocument SoftVolumeParityMap()
    {
        MapDocument doc = MapDocument.CreateNew("Soft Volume Parity");
        doc.Layers.Clear();

        MapLayerData sea = MapLayerData.CreateFarview("Sea", 1f, 1f);
        sea.Collision = true;
        sea.GridW = 20;
        sea.GridH = 12;
        sea.Tiles.Add(new PlacedTile { DefId = SeaTile, X = 0, Y = 0 });
        sea.Tiles.Add(new PlacedTile { DefId = SeaTile, X = 8, Y = 0 });

        MapLayerData looping = MapLayerData.CreateFarview("Looping Sea", 0.5f, 0.5f, loop: true);
        looping.Collision = true;
        looping.Tiles.Add(new PlacedTile { DefId = SeaTile, X = 0, Y = 0 });

        MapLayerData scenery = MapLayerData.CreateFarview("Painted Sea", 0.5f, 0.5f);
        scenery.Tiles.Add(new PlacedTile { DefId = SeaTile, X = 0, Y = 6 });

        MapLayerData field = MapLayerData.CreateBattlefield(20, 12);
        for (int x = 0; x < 20; x++) field.Tiles.Add(new PlacedTile { DefId = FloorTile, X = x, Y = 11 });
        field.Tiles.Add(new PlacedTile { DefId = "spawn.character", X = 1, Y = 10 });
        field.Tiles.Add(new PlacedTile { DefId = CurrentTile, X = 6, Y = 4, FlipX = true });
        field.Tiles.Add(new PlacedTile { DefId = BubbleTile, X = 12, Y = 4 });

        doc.Layers.Add(sea);
        doc.Layers.Add(looping);
        doc.Layers.Add(scenery);
        doc.Layers.Add(field);
        return doc;
    }

    private static List<VolumeSnapshot> VolumesUnder(Node parent)
    {
        var volumes = new List<VolumeSnapshot>();
        foreach (Node child in parent.GetChildren())
        {
            if (child is SoftVolume volume)
            {
                volumes.Add(new VolumeSnapshot(volume.Position, volume.IsMedium, volume.StagnationIndex,
                    volume.GravityScale, volume.SinkSpeed, volume.Drag, volume.Flow));
            }
        }
        volumes.Sort((a, b) => a.Position.X != b.Position.X
            ? a.Position.X.CompareTo(b.Position.X)
            : a.Position.Y.CompareTo(b.Position.Y));
        return volumes;
    }

    private static SoftVolumeTuning TuningOf(string defId) =>
        TileRegistry.TryGet(defId, out TileDef def) ? def.SoftVolumeTuning : null;

    private void CheckSoftVolumeParity()
    {
        SoftVolumeTuning seaTuning = TuningOf(SeaTile), currentTuning = TuningOf(CurrentTile), bubbleTuning = TuningOf(BubbleTile);
        bool known = seaTuning != null && currentTuning != null && bubbleTuning != null;
        Expect(known, "soft volumes: the sea, current or bubble tile kind is missing from the registry");
        if (!known) return;

        MapDocument doc = SoftVolumeParityMap();

        var world = new Node2D();
        var hazards = new Node2D();
        AddChild(world);
        AddChild(hazards);
        CombatMapRuntime.Build(doc, world, hazards);
        List<VolumeSnapshot> arena = VolumesUnder(world.GetNode("AuthoredMapPhysics"));
        world.Free();
        hazards.Free();

        var playtest = new MapPlaytestController();
        playtest.Initialize(doc);
        AddChild(playtest);
        List<VolumeSnapshot> played = VolumesUnder(playtest.GetNode("Physics"));
        playtest.Free();

        // Two sea tiles from the Farview layer, the current and the bubbles; never the looping
        // layer's water (it would repeat forever) nor the layer that did not ask for Collision.
        Expect(arena.Count == 4, $"soft volumes: the arena built {arena.Count} volumes from a map that authors 4 (a sea on a Farview layer must be built; a looping or non-colliding layer's must not)");
        Expect(played.Count == arena.Count, $"soft volumes: the Playtest built {played.Count} volumes and the arena {arena.Count} from one map");
        if (arena.Count != 4 || played.Count != 4) return;

        bool same = true;
        for (int i = 0; i < arena.Count; i++) same &= arena[i] == played[i];
        Expect(same, "soft volumes: the Playtest and the arena disagree on where a volume is or how it behaves");

        float cell = MapGrid.PixelsPerCell;
        VolumeSnapshot sea = arena[0], current = arena[1], secondSea = arena[2], bubbles = arena[3];
        Expect(sea.Position == Vector2.Zero && secondSea.Position == new Vector2(8f * cell, 0f),
            "soft volumes: a Farview layer's water must be built at its authored cells, whatever the layer's parallax");
        Expect(sea.Medium && Mathf.IsEqualApprox(sea.Stagnation, seaTuning.StagnationIndex ?? -1f) &&
               Mathf.IsEqualApprox(sea.GravityScale, seaTuning.GravityScale ?? -1f) &&
               Mathf.IsEqualApprox(sea.SinkSpeed, Units.Px(seaTuning.SinkSpeed ?? -1f)) &&
               Mathf.IsEqualApprox(sea.Drag, seaTuning.Drag ?? -1f),
            "soft volumes: the sea tile's tuning did not reach the volume a body swims in");
        Expect(!current.Medium && current.Flow == new Vector2(-Units.Px(currentTuning.FlowX), Units.Px(currentTuning.FlowY)),
            "soft volumes: a flipped current must carry the other way");
        Expect(!bubbles.Medium && bubbles.Flow == new Vector2(Units.Px(bubbleTuning.FlowX), Units.Px(bubbleTuning.FlowY)),
            "soft volumes: the bubble column's carry did not reach its volume");
    }

    // ---------------------------------------------------------------- the probe

    private const int ProbeGridW = 24, ProbeGridH = 20;
    /// <summary>The probe runs the simulation this many times faster than real time: more
    /// physics ticks per second and a matching time scale, so each tick still advances
    /// exactly 1/60 s of game time.</summary>
    private const int ProbeSpeedUp = 8;
    private const float ProbeDt = 1f / 60f;
    private static readonly string[] ProbeActions = { "move_left", "move_right", "move_down", "jump" };

    private readonly record struct ProbeTile(string DefId, int X, int Y, bool Flip = false);

    private sealed class ProbeRun
    {
        public MapPlaytestController Playtest;
        public CharacterController Body;
    }

    private async Task Ticks(int count)
    {
        for (int i = 0; i < count; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }

    /// <summary>Stands up a Playtest of a small map: `sea` tiles on a Farview layer with
    /// Collision (the way a shipped sea is laid), `field` tiles on the battlefield, and the
    /// character at the centre of the given cell.</summary>
    private async Task<ProbeRun> EnterProbe(string what, int spawnX, int spawnY, ProbeTile[] sea, ProbeTile[] field)
    {
        MapDocument doc = MapDocument.CreateNew("Physics probe: " + what);
        doc.Layers.Clear();

        if (sea != null && sea.Length > 0)
        {
            MapLayerData water = MapLayerData.CreateFarview("Sea", 1f, 1f);
            water.Collision = true;
            water.GridW = ProbeGridW;
            water.GridH = ProbeGridH;
            foreach (ProbeTile tile in sea)
                water.Tiles.Add(new PlacedTile { DefId = tile.DefId, X = tile.X, Y = tile.Y, FlipX = tile.Flip });
            doc.Layers.Add(water);
        }

        MapLayerData battlefield = MapLayerData.CreateBattlefield(ProbeGridW, ProbeGridH);
        foreach (ProbeTile tile in field ?? Array.Empty<ProbeTile>())
            battlefield.Tiles.Add(new PlacedTile { DefId = tile.DefId, X = tile.X, Y = tile.Y, FlipX = tile.Flip });
        battlefield.Tiles.Add(new PlacedTile { DefId = "spawn.character", X = spawnX, Y = spawnY });
        doc.Layers.Add(battlefield);

        var playtest = new MapPlaytestController();
        playtest.Initialize(doc);
        AddChild(playtest);
        Node entities = playtest.GetNodeOrNull("Entities");
        var run = new ProbeRun
        {
            Playtest = playtest,
            Body = entities != null && entities.GetChildCount() > 0 ? entities.GetChild(0) as CharacterController : null,
        };
        Expect(run.Body != null, $"physics probe ({what}): the Playtest did not spawn a character");
        await Ticks(1);
        return run;
    }

    private async Task LeaveProbe(ProbeRun run)
    {
        foreach (string action in ProbeActions) Input.ActionRelease(action);
        run.Playtest.QueueFree();
        await Ticks(1);
    }

    /// <summary>A body's real velocity over the next `ticks` physics ticks, from where it went.</summary>
    private async Task<Vector2> MeasureVelocity(Node2D body, int ticks)
    {
        Vector2 from = body.GlobalPosition;
        await Ticks(ticks);
        return (body.GlobalPosition - from) / (ticks * ProbeDt);
    }

    /// <summary>Presses Jump once and returns how far the body rose within `ticks`.</summary>
    private async Task<float> MeasureJump(Node2D body, int ticks)
    {
        float start = body.GlobalPosition.Y, top = start;
        Input.ActionPress("jump");
        await Ticks(1);
        Input.ActionRelease("jump");
        for (int i = 1; i < ticks; i++)
        {
            await Ticks(1);
            top = Mathf.Min(top, body.GlobalPosition.Y);
        }
        return start - top;
    }

    private void ExpectNear(float measured, float expected, float tolerance, string what)
    {
        float allowed = Mathf.Max(Mathf.Abs(expected) * tolerance, 4f);
        Expect(Mathf.Abs(measured - expected) <= allowed,
            $"physics probe: {what} — measured {measured / Units.PixelsPerMeter:0.00}, expected {expected / Units.PixelsPerMeter:0.00} (m, m/s)");
    }

    private static ProbeTile[] FloorRow(int fromX, int toX, int y, params ProbeTile[] more)
    {
        var tiles = new List<ProbeTile>();
        for (int x = fromX; x <= toX; x++) tiles.Add(new ProbeTile(FloorTile, x, y));
        tiles.AddRange(more);
        return tiles.ToArray();
    }

    private static string Metres(float px) => (px / Units.PixelsPerMeter).ToString("0.0");

    /// <summary>What the resistance model itself says a jump inside one kind of volume
    /// rises, for a body of the given top speed with the game's standard jump and gravity.</summary>
    private static float PredictedJump(SoftVolumeTuning tuning, float runSpeed)
    {
        var volume = new SoftVolume();
        MapCollision.ApplyTuning(volume, tuning, flipX: false);
        var set = new SoftVolumeSet();
        set.Enter(volume);
        float rise = set.PredictJumpRise(runSpeed, Units.JumpSpeed, Units.Gravity);
        volume.Free();
        return rise;
    }

    /// <summary>The probe's geometry is written for the shipped footprints. If a kind is
    /// resized, say so plainly instead of measuring nonsense.</summary>
    private bool ProbeFootprint(string defId, int w, int h)
    {
        bool ok = TileRegistry.TryGet(defId, out TileDef def) && def.FootprintW == w && def.FootprintH == h &&
                  def.SoftVolumeTuning != null;
        Expect(ok, $"physics probe: {defId} is not a {w}x{h} tuned SoftVolume any more — update the probe's layout in MapContentCheckPhysics.cs");
        return ok;
    }

    private const string ShippedSeaMap = "res://Maps/loongs_nest_sunken_court.json";
    /// <summary>A cell of open water in that map: nothing solid, no current and no bubbles
    /// in its column for a dozen rows below. If the map is recomposed, move this.</summary>
    private static readonly Vector2I ShippedSeaCell = new(34, 8);

    /// <summary>
    /// Every other stage stands a map up through the editor Playtest. This one builds the
    /// shipped Loong's Nest map through <see cref="CombatMapRuntime"/> — what a run does —
    /// and drops a character into its open water: the sea there is laid on a Farview layer,
    /// which the arena did not turn into volumes at all before v0.10.9.
    /// </summary>
    private async Task ProbeShippedSea(float seaSink)
    {
        MapDocument doc = MapJson.Load(ProjectSettings.GlobalizePath(ShippedSeaMap), out _);
        PackedScene heroScene = ResourceLoader.Load<PackedScene>("res://Scenes/Pomegraknight.tscn");
        Expect(doc != null && heroScene != null, "physics probe: the shipped sea map or the character scene does not load");
        if (doc == null || heroScene == null) return;

        var world = new Node2D { Name = "ProbeArenaWorld" };
        var hazards = new Node2D { Name = "ProbeArenaHazards" };
        AddChild(world);
        AddChild(hazards);
        CombatMapRuntime.Build(doc, world, hazards);

        var hero = heroScene.Instantiate<CharacterController>();
        hero.Position = new Vector2((ShippedSeaCell.X + 0.5f) * MapGrid.PixelsPerCell, (ShippedSeaCell.Y + 0.5f) * MapGrid.PixelsPerCell);
        world.AddChild(hero);

        await Ticks(20);
        Vector2 sink = await MeasureVelocity(hero, 20);
        ExpectNear(sink.Y, seaSink, 0.06f,
            $"in the shipped Loong's Nest map, built as a run builds it, a body in open water (cell {ShippedSeaCell.X},{ShippedSeaCell.Y}) must sink at the sea's SinkSpeed");
        GD.Print($"  physics  arena     in the shipped Loong's Nest map, built as a run builds it, a body sinks {Metres(sink.Y)} m/s");

        world.QueueFree();
        hazards.QueueFree();
        await Ticks(1);
    }

    private async Task ProbeSoftVolumePhysics()
    {
        if (!(ProbeFootprint(SeaTile, 8, 6) & ProbeFootprint(CurrentTile, 4, 2) & ProbeFootprint(BubbleTile, 2, 4) &
              ProbeFootprint(WaterfallTile, 2, 4) & ProbeFootprint(SwampTile, 4, 2)))
            return;

        SoftVolumeTuning seaTuning = TuningOf(SeaTile), currentTuning = TuningOf(CurrentTile),
            bubbleTuning = TuningOf(BubbleTile), fallTuning = TuningOf(WaterfallTile), swampTuning = TuningOf(SwampTile);
        bool tuned = seaTuning.SinkSpeed.HasValue && seaTuning.StagnationIndex.HasValue &&
                     swampTuning.SinkSpeed.HasValue && swampTuning.StagnationIndex.HasValue;
        Expect(tuned, "physics probe: sea and swamp water must set StagnationIndex and SinkSpeed");
        if (!tuned) return;

        int ticksBefore = Engine.PhysicsTicksPerSecond;
        double scaleBefore = Engine.TimeScale;
        Engine.PhysicsTicksPerSecond = 60 * ProbeSpeedUp;
        Engine.TimeScale = ProbeSpeedUp;
        try
        {
            // Step into the physics loop first. This method is entered from _Ready, between
            // frames; every later stage is entered at the start of a physics tick, where a
            // body added to the tree already moves in that tick. Without this the first stage
            // would run one tick behind the others, and stages are compared tick for tick.
            await Ticks(1);

            // A sea eight cells wide and twelve deep, laid the way a shipped sea is.
            ProbeTile[] sea = { new(SeaTile, 8, 3), new(SeaTile, 8, 9) };
            float seaSink = Units.Px(seaTuning.SinkSpeed.Value);

            // ---- open air: the reference fall and the reference jump -----------------------
            ProbeRun run = await EnterProbe("open air", 3, 2, null, null);
            if (run.Body == null) { await LeaveProbe(run); return; }
            float runSpeed = run.Body.MoveSpeed;
            await Ticks(8);
            Vector2 airFall = await MeasureVelocity(run.Body, 4);
            await LeaveProbe(run);

            run = await EnterProbe("a jump from the ground", 20, 11, null, FloorRow(18, 22, 12));
            await Ticks(10);
            float airJump = await MeasureJump(run.Body, 45);
            await LeaveProbe(run);
            ExpectNear(airJump, Units.JumpSpeed * Units.JumpSpeed / (2f * Units.Gravity), 0.05f,
                "a jump from the ground must still reach the design height (Units.cs)");

            // ---- the sea: sink, stroke, swim, dive -----------------------------------------
            run = await EnterProbe("the sea", 11, 8, sea, null);
            await Ticks(20);
            Vector2 sink = await MeasureVelocity(run.Body, 20);
            float stroke = await MeasureJump(run.Body, 40);
            Input.ActionPress("move_right");
            await Ticks(20);
            Vector2 swim = await MeasureVelocity(run.Body, 20);
            Input.ActionRelease("move_right");
            Input.ActionPress("move_down");
            await Ticks(15);
            Vector2 dive = await MeasureVelocity(run.Body, 15);
            await LeaveProbe(run);

            float swimCap = seaTuning.StagnationIndex.Value * runSpeed;
            float strokeModel = PredictedJump(seaTuning, runSpeed);
            ExpectNear(stroke, strokeModel, 0.06f,
                "a jump in the sea must rise as far as the sea's tuning says — drag AND the share of gravity it leaves");
            ExpectNear(sink.Y, seaSink, 0.06f, "a body left alone in the sea must sink at the tile's SinkSpeed");
            ExpectNear(swim.X, swimCap, 0.06f, "a held direction in the sea must settle on StagnationIndex x top speed");
            ExpectNear(dive.Y, Mathf.Max(seaSink, swimCap), 0.06f, "holding Down in the sea must swim down at swimming speed");
            Expect(stroke > 0.25f * airJump && stroke < 0.6f * airJump,
                $"physics probe: a jump in the sea must be a swimming stroke, well short of an air jump — rose {Metres(stroke)} m of {Metres(airJump)} m");
            GD.Print($"  physics  sea       sinks {Metres(sink.Y)} m/s · swims {Metres(swim.X)} m/s (runs {Metres(runSpeed)}) · " +
                     $"dives {Metres(dive.Y)} m/s · stroke {Metres(stroke)} m (air jump {Metres(airJump)} m)");

            // ---- a foe sinks like anyone else ----------------------------------------------
            run = await EnterProbe("a crab in the sea", 9, 6, sea, null);
            PackedScene crabScene = ResourceLoader.Load<PackedScene>("res://Scenes/Foes/CrabFoe.tscn");
            Expect(crabScene != null, "physics probe: the crab scene does not load");
            if (crabScene != null)
            {
                var crab = crabScene.Instantiate<Node2D>();
                crab.Position = new Vector2(14.5f * MapGrid.PixelsPerCell, 9f * MapGrid.PixelsPerCell);
                run.Playtest.GetNode("Entities").AddChild(crab);
                await Ticks(20);
                Vector2 crabSink = await MeasureVelocity(crab, 20);
                ExpectNear(crabSink.Y, seaSink, 0.06f, "a slow foe must sink in the sea as fast as a fighter does");
                GD.Print($"  physics  crab      sinks {Metres(crabSink.Y)} m/s in the sea");
            }
            await LeaveProbe(run);

            // ---- the shipped sea, built the way a run builds it ----------------------------
            await ProbeShippedSea(seaSink);

            // ---- a current in the sea: it carries; leaving it, the sea is still there -------
            run = await EnterProbe("a current", 10, 8, sea, new ProbeTile[] { new(CurrentTile, 10, 8) });
            await Ticks(10);
            Vector2 carried = await MeasureVelocity(run.Body, 20);
            await Ticks(70);
            Vector2 afterCurrent = await MeasureVelocity(run.Body, 15);
            await LeaveProbe(run);

            run = await EnterProbe("a flipped current", 13, 8, sea, new ProbeTile[] { new(CurrentTile, 10, 8, Flip: true) });
            await Ticks(10);
            Vector2 carriedBack = await MeasureVelocity(run.Body, 20);
            await LeaveProbe(run);

            float currentCarry = Units.Px(currentTuning.FlowX);
            ExpectNear(carried.X, currentCarry, 0.06f, "a current must carry a body at its FlowX");
            ExpectNear(carriedBack.X, -currentCarry, 0.06f, "a flipped current must carry the other way");
            ExpectNear(carried.Y, seaSink, 0.08f, "a current must not change how the sea holds a body");
            ExpectNear(afterCurrent.Y, seaSink, 0.06f, "a body that drifts out of a current is still in the sea and must keep sinking at SinkSpeed");
            Expect(Mathf.Abs(afterCurrent.X) < 12f, $"physics probe: a body kept moving at {Metres(afterCurrent.X)} m/s after leaving a current");
            GD.Print($"  physics  current   carries {Metres(carried.X)} m/s · flipped {Metres(carriedBack.X)} m/s · " +
                     $"swimming against it leaves {Metres(swim.X - carried.X)} m/s · out of it the body sinks {Metres(afterCurrent.Y)} m/s");

            // ---- bubbles lift ---------------------------------------------------------------
            run = await EnterProbe("a bubble column", 11, 9, sea, new ProbeTile[] { new(BubbleTile, 11, 7) });
            await Ticks(10);
            Vector2 lifted = await MeasureVelocity(run.Body, 20);
            await LeaveProbe(run);
            ExpectNear(lifted.Y, seaSink + Units.Px(bubbleTuning.FlowY), 0.08f, "a bubble column must lift a sinking body by its FlowY");
            Expect(lifted.Y < 0f && Mathf.Max(seaSink, swimCap) + Units.Px(bubbleTuning.FlowY) < 0f,
                "physics probe: a bubble column must lift a body even while it dives");
            GD.Print($"  physics  bubbles   lift {Metres(-lifted.Y)} m/s (still {Metres(-(Mathf.Max(seaSink, swimCap) + Units.Px(bubbleTuning.FlowY)))} m/s against a dive)");

            // ---- a waterfall: falls faster, and a jump inside it is beaten down --------------
            ProbeTile[] fall = { new(WaterfallTile, 4, 4), new(WaterfallTile, 4, 8) };
            run = await EnterProbe("a waterfall", 4, 4, null, fall);
            await Ticks(8);
            Vector2 fallFall = await MeasureVelocity(run.Body, 4);
            await LeaveProbe(run);

            run = await EnterProbe("a jump under a waterfall", 4, 11, null, FloorRow(3, 6, 12, fall));
            await Ticks(10);
            float fallJump = await MeasureJump(run.Body, 45);
            await LeaveProbe(run);

            ExpectNear(fallFall.Y - airFall.Y, Units.Px(fallTuning.FlowY), 0.05f, "a waterfall must add its FlowY to a falling body");
            Expect(fallJump < 0.5f * airJump,
                $"physics probe: a jump under a waterfall must be beaten down — rose {Metres(fallJump)} m of {Metres(airJump)} m");
            GD.Print($"  physics  waterfall drags down {Metres(fallFall.Y - airFall.Y)} m/s · a jump under it rises {Metres(fallJump)} m (air jump {Metres(airJump)} m)");

            // ---- swamp water: waded, and it holds on ----------------------------------------
            run = await EnterProbe("a swamp", 9, 11, null,
                FloorRow(6, 17, 12, new ProbeTile(SwampTile, 8, 10), new ProbeTile(SwampTile, 12, 10)));
            await Ticks(10);
            Input.ActionPress("move_right");
            await Ticks(15);
            Vector2 wade = await MeasureVelocity(run.Body, 20);
            Input.ActionRelease("move_right");
            await Ticks(20);
            float swampJump = await MeasureJump(run.Body, 40);
            await LeaveProbe(run);

            ExpectNear(wade.X, swampTuning.StagnationIndex.Value * runSpeed, 0.08f, "wading in the swamp must settle on StagnationIndex x top speed");
            ExpectNear(swampJump, PredictedJump(swampTuning, runSpeed), 0.06f,
                "a jump in the swamp must rise as far as the swamp's tuning says");
            Expect(swampJump < 0.5f * airJump,
                $"physics probe: a jump out of the swamp must be held back — rose {Metres(swampJump)} m of {Metres(airJump)} m");
            GD.Print($"  physics  swamp     wades {Metres(wade.X)} m/s · a jump from the bottom rises {Metres(swampJump)} m");
        }
        finally
        {
            foreach (string action in ProbeActions) Input.ActionRelease(action);
            Engine.PhysicsTicksPerSecond = ticksBefore;
            Engine.TimeScale = scaleBefore;
        }
    }
}
