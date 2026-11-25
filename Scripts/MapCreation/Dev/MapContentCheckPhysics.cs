namespace Fableland.MapCreation.Dev;

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Godot;
using Fableland.MapCreation.Data;
using Fableland.MapCreation.Playtest;
using Fableland.MapCreation.Runtime;

/// <summary>
/// The soft-volume half of the content check: swimming, water, currents, bubbles and
/// waterfalls (MapCreation.gdd §2.2a).
///
/// 1. Parity — the arena and the editor Playtest must build the same volumes, with the same
///    tuning, from one map; a sea laid on a Farview layer included.
/// 2. Every medium in the registry can be swum up in (asked of the model).
/// 3. Probe — it then puts the real Pomegraknight (and a crab) into the shipped kinds
///    through the Playtest, runs the physics for some twenty simulated seconds, and measures
///    what the body actually does: how fast it sinks and swims on each axis, how far a jump
///    carries inside a volume and that its cooldown holds there, how fast tapping Jump
///    climbs against holding Up, how a swimmer rests at a surface and leaves it, what a
///    current, a bubble column and a waterfall do, and that a foe does not swim. Those
///    are the numbers the tiles' tuning promises, so a change to the controller or to the
///    tuning that breaks the promise fails here rather than in a playtest.
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

    // ---------------------------------------------------------------- every kind can be climbed

    /// <summary>Slower than this after three seconds of holding Up and a medium is a trap.</summary>
    private const float SlowestClimb = Units.PixelsPerMeter * 0.5f;

    /// <summary>Asks the model about one kind of volume, or about several overlapping ones
    /// (kelp in the sea). A null tuning is a kind that keeps the scene's defaults.</summary>
    private static T AskModel<T>(Func<SoftVolumeSet, T> question, params SoftVolumeTuning[] tunings)
    {
        var volumes = new List<SoftVolume>();
        var set = new SoftVolumeSet();
        foreach (SoftVolumeTuning tuning in tunings)
        {
            var volume = new SoftVolume();
            MapCollision.ApplyTuning(volume, tuning, flipX: false);
            volumes.Add(volume);
            set.Enter(volume);
        }
        T answer = question(set);
        foreach (SoftVolume volume in volumes) volume.Free();
        return answer;
    }

    /// <summary>
    /// A fighter swims in every soft volume, and a medium it cannot swim up in is a pit with
    /// no way out. So every medium in the registry is asked what a standard fighter holding
    /// Up for three seconds does in it. (Two overlapping kinds are a map's business — the
    /// probe below swims the shipped pair, kelp in the sea.)
    /// </summary>
    private void CheckEveryMediumCanBeSwumUp()
    {
        const float run = 320f, accel = 1900f, friction = 700f;   // CharacterController's defaults
        int media = 0, traps = 0;
        string slowest = null;
        float slowestClimb = float.MaxValue;
        foreach (TileDef def in TileRegistry.All)
        {
            if (def.Category != TileCategory.SoftVolume) continue;
            if (def.SoftVolumeTuning != null && !def.SoftVolumeTuning.Medium) continue;   // a pure current
            float climb = -AskModel(set => set.PredictSwimSpeed(-1f, run, accel, friction, Units.Gravity), def.SoftVolumeTuning);
            media++;
            bool ok = climb > SlowestClimb;
            if (!ok) traps++;
            Expect(ok, $"soft volumes: {def.Id} cannot be swum up in — a 10 m/s fighter holding Up for 3 s rises {Metres(climb)} m/s. Its swimming speed (StagnationIndex x top speed) must exceed its drop");
            if (climb < slowestClimb) { slowestClimb = climb; slowest = def.Id; }
        }
        Expect(media > 0, "soft volumes: the registry has no soft-volume medium to check");
        if (media > 0 && traps == 0)
            GD.Print($"  physics  media     {media} soft-volume kinds can all be swum up in; slowest after 3 s: {slowest}, {Metres(slowestClimb)} m/s");
    }

    // ---------------------------------------------------------------- the probe

    private const int ProbeGridW = 24, ProbeGridH = 20;
    /// <summary>The probe runs the simulation this many times faster than real time: more
    /// physics ticks per second and a matching time scale, so each tick still advances
    /// exactly 1/60 s of game time.</summary>
    private const int ProbeSpeedUp = 8;
    private const float ProbeDt = 1f / 60f;
    private static readonly string[] ProbeActions = { "move_left", "move_right", "move_down", InputDefaults.MoveUp, "jump" };

    private readonly record struct ProbeTile(string DefId, int X, int Y, bool Flip = false);

    /// <summary>The probe fighter's own momentum, read off the real character.</summary>
    private readonly record struct Fighter(float Run, float Accel, float Friction);

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

    /// <summary>Presses Jump once, whatever else is held, and returns how far the body rose
    /// within `ticks`.</summary>
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

    /// <summary>With no direction held, nothing but a launch adds upward speed to a body in
    /// a volume: it only ever settles toward the drop. So any tick that gains more than
    /// this is a launch — even one that follows the last by two ticks, when the stroke has
    /// barely begun to fade (a larger threshold counted a broken cooldown as one launch).</summary>
    private const float LaunchKick = Units.JumpSpeed * 0.02f;

    /// <summary>Taps Jump every other tick — far faster than any cooldown — for `ticks`,
    /// whatever else is held. Returns the average climb (px/s, up positive) and how many
    /// times the body actually launched; the count is only meaningful with no direction
    /// held (see <see cref="LaunchKick"/>).</summary>
    private async Task<(float Climb, int Launches)> TapJump(Node2D body, int ticks)
    {
        float start = body.GlobalPosition.Y, last = start, lastSpeed = 0f;
        int launches = 0;
        for (int i = 0; i < ticks; i++)
        {
            if (i % 2 == 0) Input.ActionPress("jump");
            else Input.ActionRelease("jump");
            await Ticks(1);
            float now = body.GlobalPosition.Y;
            float speed = (last - now) / ProbeDt;
            if (speed - lastSpeed > LaunchKick) launches++;
            last = now;
            lastSpeed = speed;
        }
        Input.ActionRelease("jump");
        return ((start - body.GlobalPosition.Y) / (ticks * ProbeDt), launches);
    }

    /// <summary>The most launches a cooldown allows in a window that starts with one.</summary>
    private static int MostLaunches(int ticks, float cooldown) =>
        1 + Mathf.FloorToInt(ticks * ProbeDt / Mathf.Max(cooldown, ProbeDt) + 0.001f);

    /// <summary>The fastest a body moved upward in any single tick of the next `ticks`, in px/s.</summary>
    private async Task<float> FastestRise(Node2D body, int ticks)
    {
        float fastest = 0f, last = body.GlobalPosition.Y;
        for (int i = 0; i < ticks; i++)
        {
            await Ticks(1);
            float now = body.GlobalPosition.Y;
            fastest = Mathf.Max(fastest, (last - now) / ProbeDt);
            last = now;
        }
        return fastest;
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

    /// <summary>From a fighter's origin to the top and to the bottom of its collision shape.</summary>
    private static (float Head, float Feet) BodyExtent(Node body)
    {
        foreach (Node child in body.GetChildren())
        {
            if (child is not CollisionShape2D { Shape: not null } shape) continue;
            Rect2 box = shape.Shape.GetRect();
            return (shape.Position.Y + box.Position.Y, shape.Position.Y + box.End.Y);
        }
        return (-Units.PlayerHeightPx * 0.5f, Units.PlayerHeightPx * 0.5f);
    }

    /// <summary>The y of the surface straight above a point inside a medium — read off the
    /// volumes the Playtest actually built, so the stage measures against the real edge.</summary>
    private float SurfaceAbove(ProbeRun run, Vector2 inside, string what)
    {
        var media = new List<SoftVolume>();
        Node physics = run.Playtest.GetNodeOrNull("Physics");
        if (physics != null)
            foreach (Node child in physics.GetChildren())
                if (child is SoftVolume { IsMedium: true } volume) media.Add(volume);

        bool Covered(float y)
        {
            foreach (SoftVolume volume in media)
                if (volume.Covers(new Vector2(inside.X, y))) return true;
            return false;
        }

        bool starts = Covered(inside.Y);
        Expect(starts, $"physics probe ({what}): the body is not inside the volume this stage is about");
        if (!starts) return inside.Y;
        float surface = inside.Y;
        while (surface > -MapGrid.PixelsPerCell && Covered(surface - 1f)) surface -= 1f;
        return surface;
    }

    /// <summary>Holds Up for long enough to reach a surface, then watches the top of the head
    /// for a second: how far above the surface it gets, and how far under (px, both ≥ 0).
    /// Up stays held.</summary>
    private async Task<(float Above, float Under)> HoldUpToSurface(Node2D body, float surface, int ticksToReach)
    {
        float head = BodyExtent(body).Head;
        Input.ActionPress(InputDefaults.MoveUp);
        await Ticks(ticksToReach);
        float above = 0f, under = 0f;
        for (int i = 0; i < 60; i++)
        {
            await Ticks(1);
            float depth = body.GlobalPosition.Y + head - surface;
            above = Mathf.Max(above, -depth);
            under = Mathf.Max(under, depth);
        }
        return (above, under);
    }

    /// <summary>Presses Jump at a surface and returns how far above it the body's feet got
    /// (negative: they never cleared it).</summary>
    private async Task<float> LeapOut(Node2D body, float surface, int ticks)
    {
        float start = body.GlobalPosition.Y;
        float rise = await MeasureJump(body, ticks);
        return surface - (start - rise + BodyExtent(body).Feet);
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

    /// <summary>A pool a fighter can fall into, in a shipped map: the cell on its bottom the
    /// probe starts from — two cells from its east bank — and the column where that bank
    /// begins. If a map is recomposed, move these; when a map ships a new pool, add it (the
    /// probe refuses a shipped map that holds swamp water and is not listed).</summary>
    private readonly record struct ShippedPool(string Map, string Name, Vector2I Cell, int BankColumn);

    private static readonly ShippedPool[] ShippedPools =
    {
        // the showcase map's west canal
        new("forgotten_canals_of_the_sun", "Forgotten City", new Vector2I(12, 22), 14),
        // the low-ground band map (v0.11.0): a basin with one canal across its floor. Its
        // banks stood 2 m over the water until v0.11.1 — more than a surface jump clears.
        new("forgotten_low_ground", "Forgotten City low-ground", new Vector2I(29, 23), 32),
    };

    /// <summary>
    /// A pool is a pit unless a fighter can get out of it. This builds each shipped map that
    /// holds a pool the way a run does, stands a character on the pool's bottom, and has it
    /// do what a player would: swim up and toward the bank, then jump. It must end up
    /// standing on the bank.
    /// </summary>
    private async Task ProbeShippedPools()
    {
        // Swamp water is the kind laid as a pool with banks (the sea fills its maps). A map
        // that ships some and is not listed above has a pool nobody has swum out of.
        string dir = ProjectSettings.GlobalizePath("res://Maps");
        foreach (string path in Directory.GetFiles(dir, "*.json", SearchOption.TopDirectoryOnly))
        {
            MapDocument shipped = MapJson.Load(path, out _);
            if (shipped?.Layers == null) continue;
            bool swamp = false;
            foreach (MapLayerData layer in shipped.Layers)
                if (layer?.Tiles != null)
                    foreach (PlacedTile tile in layer.Tiles) swamp |= tile.DefId == SwampTile;
            string name = Path.GetFileNameWithoutExtension(path);
            bool probed = false;
            foreach (ShippedPool pool in ShippedPools) probed |= pool.Map == name;
            Expect(!swamp || probed,
                $"physics probe: {name} holds swamp water ({SwampTile}) and no pool of it is probed for a way out — add its pool to ShippedPools");
        }

        foreach (ShippedPool pool in ShippedPools) await ProbeShippedPool(pool);
    }

    private async Task ProbeShippedPool(ShippedPool pool)
    {
        string where = $"in the shipped {pool.Name} map";
        MapDocument doc = MapJson.Load(ProjectSettings.GlobalizePath($"res://Maps/{pool.Map}.json"), out _);
        PackedScene heroScene = ResourceLoader.Load<PackedScene>("res://Scenes/Pomegraknight.tscn");
        Expect(doc != null && heroScene != null, $"physics probe: the shipped map {pool.Map} or the character scene does not load");
        if (doc == null || heroScene == null) return;

        var world = new Node2D { Name = "ProbeSwampWorld" };
        var hazards = new Node2D { Name = "ProbeSwampHazards" };
        AddChild(world);
        AddChild(hazards);
        CombatMapRuntime.Build(doc, world, hazards);

        float cell = MapGrid.PixelsPerCell;
        var hero = heroScene.Instantiate<CharacterController>();
        hero.Position = new Vector2((pool.Cell.X + 0.5f) * cell, (pool.Cell.Y + 0.5f) * cell);
        world.AddChild(hero);
        float bottom = hero.Position.Y;

        await Ticks(15);
        Input.ActionPress(InputDefaults.MoveUp);
        Input.ActionPress("move_right");
        await Ticks(180);                       // up to the surface, and across to the bank's wall
        float atSurface = hero.GlobalPosition.Y;
        Input.ActionPress("jump");
        await Ticks(1);
        Input.ActionRelease("jump");
        // Right stays held until the fighter stands on the bank (the showcase map's is only
        // two cells wide: held any longer, the fighter runs across it into the next canal).
        bool onBank = false;
        for (int i = 0; i < 90 && !onBank; i++)
        {
            await Ticks(1);
            onBank = hero.IsOnFloor() && hero.GlobalPosition.X > pool.BankColumn * cell &&
                     hero.GlobalPosition.Y < bottom - cell;
        }
        Input.ActionRelease("move_right");
        Input.ActionRelease(InputDefaults.MoveUp);

        Expect(atSurface < bottom - 0.5f * cell,
            $"physics probe: {where} a fighter holding Up must rise from the canal's bottom — it rose {Metres(bottom - atSurface)} m");
        Expect(onBank,
            $"physics probe: {where} a fighter must be able to leave the canal — swim up to the bank and jump — but it ended " +
            $"{Metres(bottom - hero.GlobalPosition.Y)} m above the bottom at column {hero.GlobalPosition.X / cell:0.0}, {(hero.IsOnFloor() ? "standing" : "not standing")}");
        GD.Print($"  physics  canal     {where}, built as a run builds it, a fighter swims up {Metres(bottom - atSurface)} m and jumps out onto the bank: {(onBank ? "yes" : "NO")}");

        world.QueueFree();
        hazards.QueueFree();
        await Ticks(1);
    }

    private const string BambooTile = "softvolume.bm_bamboo_giant";
    private const string KelpTile = "softvolume.ln_kelp";

    private async Task ProbeSoftVolumePhysics()
    {
        if (!(ProbeFootprint(SeaTile, 8, 6) & ProbeFootprint(CurrentTile, 4, 2) & ProbeFootprint(BubbleTile, 2, 4) &
              ProbeFootprint(WaterfallTile, 2, 4) & ProbeFootprint(SwampTile, 4, 2) &
              ProbeFootprint(BambooTile, 2, 16) & ProbeFootprint(KelpTile, 2, 6)))
            return;

        SoftVolumeTuning seaTuning = TuningOf(SeaTile), currentTuning = TuningOf(CurrentTile),
            bubbleTuning = TuningOf(BubbleTile), fallTuning = TuningOf(WaterfallTile), swampTuning = TuningOf(SwampTile),
            bambooTuning = TuningOf(BambooTile), kelpTuning = TuningOf(KelpTile);
        bool tuned = seaTuning.SinkSpeed.HasValue && seaTuning.StagnationIndex.HasValue &&
                     swampTuning.SinkSpeed.HasValue && swampTuning.StagnationIndex.HasValue;
        Expect(tuned, "physics probe: sea and swamp water must set StagnationIndex and SinkSpeed");
        if (!tuned) return;

        InputDefaults.Ensure();   // the stages press move_up, and the cleanup releases it
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
            var fighter = new Fighter(run.Body.MoveSpeed, run.Body.AirAccel, run.Body.AirFriction);
            float runSpeed = fighter.Run;
            await Ticks(8);
            Vector2 airFall = await MeasureVelocity(run.Body, 4);
            await LeaveProbe(run);

            run = await EnterProbe("a jump from the ground", 20, 11, null, FloorRow(18, 22, 12));
            await Ticks(10);
            float airJump = await MeasureJump(run.Body, 45);
            await LeaveProbe(run);
            ExpectNear(airJump, Units.JumpSpeed * Units.JumpSpeed / (2f * Units.Gravity), 0.05f,
                "a jump from the ground must still reach the design height (Units.cs)");

            // What the model says a fighter does in a kind (or in overlapping kinds).
            float ModelSwim(float rise, params SoftVolumeTuning[] kinds) => AskModel(
                set => set.PredictSwimSpeed(rise, fighter.Run, fighter.Accel, fighter.Friction, Units.Gravity), kinds);
            float ModelPush(SoftVolumeTuning kind) => AskModel(
                set => set.PredictJumpRise(fighter.Run, Units.JumpSpeed, Units.Gravity, fighter.Friction), kind);

            // ---- the sea: sink, a stroke, and swimming on both axes ---------------------------
            run = await EnterProbe("the sea", 11, 8, sea, null);
            float jumpCooldown = run.Body.JumpCooldown;
            await Ticks(20);
            Vector2 sink = await MeasureVelocity(run.Body, 20);
            float seaStroke = await MeasureJump(run.Body, 30);
            Input.ActionPress("move_right");
            await Ticks(20);
            Vector2 swim = await MeasureVelocity(run.Body, 20);
            Input.ActionRelease("move_right");
            Input.ActionPress(InputDefaults.MoveUp);
            await Ticks(20);
            Vector2 swimUp = await MeasureVelocity(run.Body, 20);
            Input.ActionPress("move_right");
            await Ticks(15);
            Vector2 upAndAcross = await MeasureVelocity(run.Body, 15);
            Input.ActionRelease("move_right");
            Input.ActionRelease(InputDefaults.MoveUp);
            Input.ActionPress("move_down");
            await Ticks(15);
            Vector2 swimDown = await MeasureVelocity(run.Body, 15);
            await LeaveProbe(run);

            float swimCap = seaTuning.StagnationIndex.Value * runSpeed;
            ExpectNear(sink.Y, seaSink, 0.06f, "a body left alone in the sea must sink at the tile's SinkSpeed");
            ExpectNear(seaStroke, ModelPush(seaTuning), 0.06f,
                "a jump in open water must rise as far as the sea's tuning says — drag AND the share of gravity it leaves");
            Expect(seaStroke > 0.25f * airJump && seaStroke < 0.6f * airJump,
                $"physics probe: a jump in open water must be a stroke, well short of an air jump — rose {Metres(seaStroke)} m of {Metres(airJump)} m");
            ExpectNear(swim.X, swimCap, 0.06f, "a held direction in the sea must settle on StagnationIndex x top speed");
            ExpectNear(swim.Y, seaSink, 0.08f, "swimming across must leave the sink as it is: the two are simply added");
            ExpectNear(swimUp.Y, -swimCap + seaSink, 0.08f, "holding Up in the sea must rise at swimming speed minus the sink");
            ExpectNear(swimUp.Y, ModelSwim(-1f, seaTuning), 0.08f, "holding Up in the sea must do what the model says");
            ExpectNear(swimDown.Y, swimCap + seaSink, 0.06f, "holding Down in the sea must sink at swimming speed plus the sink");
            ExpectNear(upAndAcross.X, swimCap, 0.06f, "Up and Right together must keep the full swimming speed across");
            ExpectNear(upAndAcross.Y, -swimCap + seaSink, 0.08f, "Up and Right together must keep the full rise");
            GD.Print($"  physics  sea       sinks {Metres(sink.Y)} m/s · swims {Metres(swim.X)} m/s across (runs {Metres(runSpeed)}) while sinking {Metres(swim.Y)} · " +
                     $"up {Metres(-swimUp.Y)} m/s · down {Metres(swimDown.Y)} m/s · up-and-across {Metres(upAndAcross.X)} / {Metres(-upAndAcross.Y)} m/s");

            // ---- jumping in the sea: off the bed, and out through the surface -----------------
            run = await EnterProbe("the sea bed", 11, 13, sea, FloorRow(8, 15, 14));
            await Ticks(10);
            float seaPush = await MeasureJump(run.Body, 40);
            await LeaveProbe(run);
            ExpectNear(seaPush, ModelPush(seaTuning), 0.06f,
                "a jump off the sea bed must rise as far as the sea's tuning says — drag AND the share of gravity it leaves");

            // Space, tapped far faster than the cooldown, from deep water: alone, then with Up held.
            const int tapTicks = 90;
            run = await EnterProbe("tapping Jump in the sea", 11, 13, sea, null);
            await Ticks(10);
            (float seaTapClimb, int seaTapLaunches) = await TapJump(run.Body, tapTicks);
            await LeaveProbe(run);

            run = await EnterProbe("tapping Jump and holding Up in the sea", 11, 13, sea, null);
            Input.ActionPress(InputDefaults.MoveUp);
            await Ticks(10);
            (float seaTapUpClimb, _) = await TapJump(run.Body, tapTicks);
            await LeaveProbe(run);

            int mostLaunches = MostLaunches(tapTicks, jumpCooldown);
            Expect(seaTapLaunches <= mostLaunches,
                $"physics probe: the jump cooldown must hold inside a soft volume — tapping Jump for {tapTicks * ProbeDt:0.0} s launched {seaTapLaunches} times, and a {jumpCooldown:0.00} s cooldown allows {mostLaunches}");
            Expect(seaTapLaunches >= mostLaunches - 2,
                $"physics probe: a jump must come back inside a soft volume once its cooldown is over — tapping Jump for {tapTicks * ProbeDt:0.0} s launched only {seaTapLaunches} times of a possible {mostLaunches}");
            Expect(seaTapUpClimb > seaTapClimb,
                $"physics probe: holding Up must add to jumping in the sea — climbed {Metres(seaTapUpClimb)} m/s with Up, {Metres(seaTapClimb)} m/s without");

            run = await EnterProbe("the sea's surface", 11, 4, sea, null);
            float seaSurface = SurfaceAbove(run, run.Body.GlobalPosition, "the sea's surface");
            (float seaAbove, float seaUnder) = await HoldUpToSurface(run.Body, seaSurface, 120);
            float seaLeap = await LeapOut(run.Body, seaSurface, 45);
            await LeaveProbe(run);
            Expect(seaAbove <= SurfaceBob && seaUnder <= SurfaceBob,
                $"physics probe: a swimmer holding Up must settle with its head at the surface of the sea — it ranged from {Metres(seaAbove)} m above to {Metres(seaUnder)} m under");
            Expect(seaLeap > Units.PixelsPerMeter,
                $"physics probe: a jump at the surface must carry a fighter out of the sea — its feet cleared the water by {Metres(seaLeap)} m");
            Expect(seaLeap < airJump, $"physics probe: the sea must still cost a jump out of it — the leap cleared {Metres(seaLeap)} m, an air jump is {Metres(airJump)} m");
            GD.Print($"  physics  sea jump  a jump in open water rises {Metres(seaStroke)} m · off the sea bed {Metres(seaPush)} m (air jump {Metres(airJump)} m) · " +
                     $"at the surface the head stays within {Metres(Mathf.Max(seaAbove, seaUnder))} m of it, and a jump clears the water by {Metres(seaLeap)} m");
            GD.Print($"  physics  sea climb holding Up {Metres(-swimUp.Y)} m/s · tapping Jump {Metres(seaTapClimb)} m/s ({seaTapLaunches} launches in {tapTicks * ProbeDt:0.0} s: the {jumpCooldown:0.0} s cooldown allows {mostLaunches}) · both {Metres(seaTapUpClimb)} m/s");

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

            // ---- bubbles lift: alone, swum, and across the joint of two stacked tiles ---------
            float bubbleLift = Units.Px(bubbleTuning.FlowY);
            ProbeTile[] column = { new(BubbleTile, 11, 3), new(BubbleTile, 11, 7) };
            run = await EnterProbe("a bubble column", 11, 9, sea, column);
            await Ticks(10);
            Vector2 lifted = await MeasureVelocity(run.Body, 20);
            float fastestInColumn = await FastestRise(run.Body, 130);   // rides up across the joint
            await LeaveProbe(run);

            run = await EnterProbe("swimming up a bubble column", 11, 9, sea, column);
            Input.ActionPress(InputDefaults.MoveUp);
            await Ticks(10);
            Vector2 liftedSwimming = await MeasureVelocity(run.Body, 12);
            await LeaveProbe(run);

            ExpectNear(lifted.Y, seaSink + bubbleLift, 0.08f, "a bubble column must lift a sinking body by its FlowY");
            Expect(lifted.Y < 0f, "physics probe: a bubble column must lift a body the sea is sinking");
            Expect(fastestInColumn <= -lifted.Y * 1.15f,
                $"physics probe: a column laid as two tiles must lift evenly — at their joint a body rose {Metres(fastestInColumn)} m/s, against {Metres(-lifted.Y)} m/s inside one tile (identical carries must count once)");
            ExpectNear(liftedSwimming.Y, -swimCap + seaSink + bubbleLift, 0.08f, "swimming up a bubble column must add the swim to the lift");
            GD.Print($"  physics  bubbles   lift {Metres(-lifted.Y)} m/s · swum up {Metres(-liftedSwimming.Y)} m/s · across the joint of two tiles never faster than {Metres(fastestInColumn)} m/s");

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

            // ---- swamp water: waded, pushed off, and left through its surface -----------------
            run = await EnterProbe("a swamp", 9, 11, null,
                FloorRow(6, 17, 12, new ProbeTile(SwampTile, 8, 10), new ProbeTile(SwampTile, 12, 10)));
            await Ticks(10);
            Input.ActionPress("move_right");
            await Ticks(15);
            Vector2 wade = await MeasureVelocity(run.Body, 20);
            Input.ActionRelease("move_right");
            await Ticks(20);
            Input.ActionPress(InputDefaults.MoveUp);       // off the bottom, well under the surface
            await Ticks(8);
            Vector2 swampUp = await MeasureVelocity(run.Body, 15);
            Input.ActionRelease(InputDefaults.MoveUp);
            await Ticks(60);                               // and back down to it
            float swampPush = await MeasureJump(run.Body, 40);
            float swampSurface = SurfaceAbove(run, run.Body.GlobalPosition, "a swamp");
            (float swampAbove, float swampUnder) = await HoldUpToSurface(run.Body, swampSurface, 200);
            float swampLeap = await LeapOut(run.Body, swampSurface, 45);
            await LeaveProbe(run);

            ExpectNear(wade.X, swampTuning.StagnationIndex.Value * runSpeed, 0.08f, "wading in the swamp must settle on StagnationIndex x top speed");
            ExpectNear(swampUp.Y, ModelSwim(-1f, swampTuning), 0.1f, "holding Up in the swamp must rise as fast as the model says");
            ExpectNear(swampPush, ModelPush(swampTuning), 0.06f,
                "a jump off the swamp's bottom must rise as far as the swamp's tuning says");
            Expect(swampPush < 0.5f * airJump,
                $"physics probe: a jump off the swamp's bottom must be held back — rose {Metres(swampPush)} m of {Metres(airJump)} m");
            Expect(swampAbove <= SurfaceBob && swampUnder <= SurfaceBob,
                $"physics probe: a swimmer holding Up must settle with its head at the surface of the swamp — it ranged from {Metres(swampAbove)} m above to {Metres(swampUnder)} m under");
            Expect(swampLeap > 0.5f * Units.PixelsPerMeter,
                $"physics probe: a jump at the surface must carry a fighter out of the swamp — its feet cleared it by {Metres(swampLeap)} m");
            GD.Print($"  physics  swamp     wades {Metres(wade.X)} m/s · up {Metres(-swampUp.Y)} m/s · a jump off the bottom rises {Metres(swampPush)} m · " +
                     $"at the surface a jump clears the swamp by {Metres(swampLeap)} m");

            // ---- the shipped canals: a pool must have a way out --------------------------------
            await ProbeShippedPools();

            // ---- a plain soft volume (a tall bamboo): floated in, swum up, left by its top ----
            ProbeTile[] bamboo = { new(BambooTile, 10, 3) };
            float bambooDrop = AskModel(set => set.Drop(fighter.Run), bambooTuning);
            run = await EnterProbe("a bamboo", 10, 15, null, bamboo);
            float bambooTop = SurfaceAbove(run, run.Body.GlobalPosition, "a bamboo");
            await Ticks(30);
            Vector2 floating = await MeasureVelocity(run.Body, 20);
            Vector2 stillFloating = await MeasureVelocity(run.Body, 20);
            float jumpInside = await MeasureJump(run.Body, 30);
            Input.ActionPress(InputDefaults.MoveUp);
            await Ticks(40);
            Vector2 climb = await MeasureVelocity(run.Body, 20);
            (float bambooAbove, float bambooUnder) = await HoldUpToSurface(run.Body, bambooTop, 150);
            float bambooLeap = await LeapOut(run.Body, bambooTop, 45);
            await LeaveProbe(run);

            ExpectNear(floating.Y, bambooDrop, 0.1f, "a fighter left alone in a bamboo must float down at its drop (GravityIndex x top speed)");
            ExpectNear(stillFloating.Y, bambooDrop, 0.1f, "a fighter floating in a bamboo must not speed up: the volume holds it");
            ExpectNear(jumpInside, ModelPush(bambooTuning), 0.06f, "a jump deep inside a bamboo must rise as far as the model says");

            run = await EnterProbe("tapping Jump in a bamboo", 10, 17, null, bamboo);
            await Ticks(10);
            (float bambooTapClimb, int bambooTapLaunches) = await TapJump(run.Body, 60);
            await LeaveProbe(run);
            Expect(bambooTapLaunches <= MostLaunches(60, jumpCooldown),
                $"physics probe: the jump cooldown must hold inside a bamboo — tapping Jump for 1.0 s launched {bambooTapLaunches} times, and a {jumpCooldown:0.00} s cooldown allows {MostLaunches(60, jumpCooldown)}");
            ExpectNear(climb.Y, ModelSwim(-1f, bambooTuning), 0.06f, "holding Up in a bamboo must climb as fast as the model says");
            Expect(-climb.Y > 0.75f * runSpeed,
                $"physics probe: at Drag 1 a held Up must outrun the cap as a held Right does — climbed {Metres(-climb.Y)} m/s of a {Metres(runSpeed)} m/s run");
            Expect(bambooAbove <= TopBob && bambooUnder <= TopBob,
                $"physics probe: a fighter holding Up must settle with its head at the top of a bamboo — it ranged from {Metres(bambooAbove)} m above to {Metres(bambooUnder)} m under");
            Expect(bambooLeap > 0.5f * airJump,
                $"physics probe: a jump from the top of a bamboo must be most of an air jump — its feet cleared the top by {Metres(bambooLeap)} m of {Metres(airJump)} m");
            GD.Print($"  physics  bamboo    floats down {Metres(floating.Y)} m/s · holding Up climbs {Metres(-climb.Y)} m/s · a jump inside rises {Metres(jumpInside)} m · " +
                     $"tapping Jump climbs {Metres(bambooTapClimb)} m/s ({bambooTapLaunches} launches in 1.0 s) · " +
                     $"at the top the head stays within {Metres(Mathf.Max(bambooAbove, bambooUnder))} m of it, and a jump clears it by {Metres(bambooLeap)} m");

            // ---- a foe does not swim: it falls through foliage, slowed, as it always has -------
            run = await EnterProbe("a crab in a bamboo", 2, 3, null, FloorRow(1, 3, 4, bamboo));
            if (crabScene != null)
            {
                var crab = crabScene.Instantiate<Node2D>();
                crab.Position = new Vector2(11f * MapGrid.PixelsPerCell, 9f * MapGrid.PixelsPerCell);
                run.Playtest.GetNode("Entities").AddChild(crab);
                await Ticks(20);
                Vector2 crabFall = await MeasureVelocity(crab, 20);
                Expect(crabFall.Y > 5f * bambooDrop,
                    $"physics probe: a foe is carried passively — it must fall through a bamboo, not float in it — but a crab fell at {Metres(crabFall.Y)} m/s");
                GD.Print($"  physics  foe       a crab falls through a bamboo at {Metres(crabFall.Y)} m/s (a fighter floats at {Metres(bambooDrop)})");
            }
            await LeaveProbe(run);

            // ---- kelp in the sea: the strictest of both, and still a way up ---------------------
            float kelpCap = Mathf.Min(kelpTuning.StagnationIndex ?? 1f, seaTuning.StagnationIndex.Value) * runSpeed;
            run = await EnterProbe("kelp in the sea", 11, 9, sea, new ProbeTile[] { new(KelpTile, 11, 6) });
            Input.ActionPress(InputDefaults.MoveUp);
            await Ticks(25);
            Vector2 kelpUp = await MeasureVelocity(run.Body, 20);
            Input.ActionRelease(InputDefaults.MoveUp);
            Input.ActionPress("move_right");
            await Ticks(12);
            Vector2 kelpAcross = await MeasureVelocity(run.Body, 12);
            await LeaveProbe(run);

            ExpectNear(kelpUp.Y, ModelSwim(-1f, seaTuning, kelpTuning), 0.1f, "holding Up in kelp in the sea must do what the model says of the two together");
            Expect(-kelpUp.Y > SlowestClimb,
                $"physics probe: kelp in the sea must leave a way up — a fighter holding Up rises {Metres(-kelpUp.Y)} m/s");
            ExpectNear(kelpAcross.X, kelpCap, 0.08f, "kelp in the sea must be crossed at the slower of the two speeds");
            GD.Print($"  physics  kelp      in the sea: up {Metres(-kelpUp.Y)} m/s · across {Metres(kelpAcross.X)} m/s");
        }
        finally
        {
            foreach (string action in ProbeActions) Input.ActionRelease(action);
            Engine.PhysicsTicksPerSecond = ticksBefore;
            Engine.TimeScale = scaleBefore;
        }
    }

    /// <summary>How far a head may bob about a surface while Up is held: water holds it
    /// closely; a plain volume lets a fast climb overshoot and drift back.</summary>
    private const float SurfaceBob = Units.PixelsPerMeter * 0.5f, TopBob = Units.PixelsPerMeter * 1f;
}
