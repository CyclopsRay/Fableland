using Godot;
using System.Collections.Generic;

/// <summary>
/// An enterable "go-inside" volume — the tree/bush/cloud/water archetype, as
/// opposed to the thin one-way <b>platform</b> you can only land on or cross.
///
/// A body falls directly into this Area2D; it has no standable one-way top. While
/// inside, it preserves normal movement, gravity, and jumps, then gradually applies
/// two indices — both of which affect players and foes alike:
///
///   • StagnationIndex (scene fallback 0.5) — velocity is pulled toward index·maxSpeed,
///     never overwritten on entry. A map tile kind may override this before instantiation.
///   • GravityIndex   (0.1) — a constant index·maxSpeed downward drift.
///
/// Five more settings (v0.10.9) make it a body of water, a current or a waterfall:
///
///   • GravityScale (1) — the share of gravity a body keeps inside. Water is well under 1:
///     the rest is buoyancy, and the two forces are simply added.
///   • SinkSpeed (0 = off) — a buoyant medium also holds what is in it: this is the fastest
///     anything sinks in it on its own, in px/s, whatever the body's own top speed. It
///     replaces the GravityIndex drift. A player holding Down swims down at the medium's
///     speed cap instead.
///   • Drag (1) — multiplies the resistance. At 1 the resistance is gentler than a body's
///     own acceleration, so a held direction outruns the cap: foliage slows a coast, not a
///     run. Water uses about 5, which makes the cap the swimming speed and halves a jump.
///   • Flow (zero) — a steady carry velocity added to everything inside: a current, a
///     column of rising bubbles, falling water.
///   • IsMedium (true) — true for something a body is *in* (foliage, cloud, water): it
///     drags and it supports jumps. False for a pure current: it carries and nothing else,
///     so a waterfall cannot be climbed and a current does not slow you.
///
/// A body may be inside several at once (a current inside a sea); <see cref="SoftVolumeSet"/>
/// adds them up, and both body types resolve their resistance through it.
/// </summary>
public partial class SoftVolume : Area2D
{
    [Export] public float StagnationIndex = 0.5f;
    [Export] public float GravityIndex = 0.1f;
    // Multiplies a body's external-velocity damping while inside — knockback still
    // shoves you in, but the viscosity eats it quickly.
    [Export] public float ExternalDampingMult = 3f;
    [Export] public float GravityScale = 1f;
    [Export] public float SinkSpeed = 0f;
    [Export] public float Drag = 1f;
    [Export] public Vector2 Flow = Vector2.Zero;
    [Export] public bool IsMedium = true;

    /// <summary>
    /// Configures an authored runtime tile before it enters the tree. The map
    /// orchestrator supplies collision shapes in this volume's local/world-stable space;
    /// gameplay receives only Godot shapes and therefore remains independent of map data.
    /// The sprite in this reusable scene is hidden because the map owns the parallaxed art.
    /// </summary>
    public void ConfigureRuntimeCollision(IReadOnlyList<Shape2D> shapes,
        IReadOnlyList<Vector2> shapePositions)
    {
        if (shapes == null || shapePositions == null || shapes.Count != shapePositions.Count)
        {
            GD.PushError("SoftVolume: runtime collision configuration is invalid.");
            return;
        }

        var interior = GetNodeOrNull<CollisionShape2D>("Interior");
        if (interior == null)
        {
            GD.PushError("SoftVolume: scene is missing Interior CollisionShape2D.");
            return;
        }

        // This scene is normally instantiated once, but cleanup keeps the setup
        // idempotent for editor-preview rebuilds.
        foreach (Node child in GetChildren())
            if (child.Name.ToString().StartsWith("RuntimeInterior")) child.Free();

        if (shapes.Count == 0)
        {
            interior.Disabled = true;
        }
        else
        {
            interior.Disabled = false;
            interior.Shape = shapes[0];
            interior.Position = shapePositions[0];
            for (int i = 1; i < shapes.Count; i++)
            {
                var extra = new CollisionShape2D
                {
                    Name = "RuntimeInterior" + i,
                    Shape = shapes[i],
                    Position = shapePositions[i],
                };
                AddChild(extra);
            }
        }

        var sprite = GetNodeOrNull<Sprite2D>("Sprite2D");
        if (sprite != null) sprite.Visible = false;
    }

    public override void _Ready()
    {
        BodyEntered += OnBodyEntered;
        BodyExited += OnBodyExited;
    }

    private void OnBodyEntered(Node2D body)
    {
        if (!AffectsBody(body)) return;
        if (body is CharacterController c) c.EnterSoftVolume(this);
        else if (body is BaseFoe e) e.EnterSoftVolume(this);
    }

    private void OnBodyExited(Node2D body)
    {
        if (!AffectsBody(body)) return;
        if (body is CharacterController c) c.ExitSoftVolume(this);
        else if (body is BaseFoe e) e.ExitSoftVolume(this);
    }

    /// <summary>Most SoftVolumes use the shared player-and-foe traversal contract.
    /// Specialized volumes may narrow their membership without duplicating the
    /// velocity-resistance implementation.</summary>
    protected virtual bool AffectsBody(Node2D body) => body is CharacterController or BaseFoe;

    /// <summary>This one volume's resistance, as if the body were in nothing else. Bodies
    /// go through <see cref="SoftVolumeSet.Resist"/>, which also handles overlaps.</summary>
    public Vector2 ApplyVelocityResistance(Vector2 velocity, float baseMaxSpeed, float delta) =>
        Resist(velocity, baseMaxSpeed, delta, StagnationIndex, GravityIndex, Drag, SinkSpeed);

    /// <summary>
    /// Gradually pulls a body's current intent velocity toward a medium's cap.
    /// Normal gravity and a jump launch happen first; this contributes a resistance
    /// delta-v rather than replacing either. The resistance is derived from the physical
    /// gravity constant — <c>g × (1 − stagnation) × drag</c> — so the indices keep their
    /// designer-facing meaning.
    ///
    /// Vertically there are two regimes. With no <paramref name="sinkSpeed"/> (foliage,
    /// cloud) the cap is ±stagnation·maxSpeed plus the GravityIndex drift, as it has been
    /// since v0.6.18. With one (water) the medium holds the sink at that absolute speed —
    /// the same for a fast body and a slow one — or at the swim cap while diving.
    /// </summary>
    public static Vector2 Resist(Vector2 velocity, float baseMaxSpeed, float delta,
        float stagnation, float gravityIndex, float drag, float sinkSpeed, bool dive = false)
    {
        float maxInside = stagnation * baseMaxSpeed;
        float step = Units.Gravity * Mathf.Clamp(1f - stagnation, 0f, 1f) * Mathf.Max(drag, 0f) * delta;

        float targetY;
        if (sinkSpeed > 0f)
        {
            float deepest = dive ? Mathf.Max(sinkSpeed, maxInside) : sinkSpeed;
            targetY = Mathf.Clamp(velocity.Y, -maxInside, deepest);
        }
        else
        {
            targetY = Mathf.Clamp(velocity.Y, -maxInside, maxInside) + gravityIndex * baseMaxSpeed;
        }

        return new Vector2(
            Mathf.MoveToward(velocity.X, Mathf.Clamp(velocity.X, -maxInside, maxInside), step),
            Mathf.MoveToward(velocity.Y, targetY, step));
    }
}

/// <summary>
/// The soft volumes one body is inside, and what they add up to. Players and foes each own
/// one, so both obey the same rule when volumes overlap — a current inside a sea, kelp in
/// water, a waterfall crossing mist. Of the media (the volumes a body is *in*):
///
///   • the lowest StagnationIndex sets the speed cap, and brings its GravityIndex drift;
///   • the highest Drag and ExternalDampingMult apply;
///   • the lowest GravityScale applies — the most buoyant medium wins;
///   • the lowest SinkSpeed among those that set one applies.
///
/// So the strictest of each wins, and kelp growing in the sea is never easier to cross than
/// the sea. <see cref="Flow"/> is every volume's carry velocity, added — currents included.
/// Outside any medium the set is inert: full gravity, no resistance, no jump support.
///
/// Before v0.10.9 a body remembered a single volume, so leaving an inner one forgot the outer
/// one it was still standing in.
/// </summary>
public sealed class SoftVolumeSet
{
    private readonly List<SoftVolume> _inside = new();

    /// <summary>True while the body is in at least one medium: it is resisted, and it may
    /// jump from where it is.</summary>
    public bool InMedium { get; private set; }
    public float StagnationIndex { get; private set; } = 1f;
    public float GravityIndex { get; private set; }
    public float Drag { get; private set; } = 1f;
    public float GravityScale { get; private set; } = 1f;
    public float SinkSpeed { get; private set; }
    public float ExternalDampingMult { get; private set; } = 1f;
    public Vector2 Flow { get; private set; }

    /// <summary>True in a medium that holds its sink (water): Down swims down in it.</summary>
    public bool Buoyant => InMedium && SinkSpeed > 0f;

    public void Enter(SoftVolume volume)
    {
        if (volume == null || _inside.Contains(volume)) return;
        _inside.Add(volume);
        Recompute();
    }

    public void Exit(SoftVolume volume)
    {
        if (_inside.Remove(volume)) Recompute();
    }

    /// <summary>Drops volumes that were freed without an exit event (a map torn down under a
    /// body). Cheap enough to call every physics tick.</summary>
    public void Prune()
    {
        bool dropped = false;
        for (int i = _inside.Count - 1; i >= 0; i--)
        {
            if (GodotObject.IsInstanceValid(_inside[i])) continue;
            _inside.RemoveAt(i);
            dropped = true;
        }
        if (dropped) Recompute();
    }

    /// <summary>The resistance of everything the body is in, for one physics tick. Call it
    /// after gravity, input and a jump launch have been applied to <paramref name="velocity"/>.</summary>
    public Vector2 Resist(Vector2 velocity, float baseMaxSpeed, float delta, bool dive = false) =>
        InMedium
            ? SoftVolume.Resist(velocity, baseMaxSpeed, delta, StagnationIndex, GravityIndex, Drag, SinkSpeed, dive)
            : velocity;

    /// <summary>
    /// How far a jump rises inside this set's media before the body starts to sink — the
    /// model's own answer, integrated tick by tick the way a body integrates it (the launch
    /// tick has no gravity: a jump overrides it; every later tick adds scaled gravity, then
    /// resistance). The content check holds a real character against this number, which is
    /// how a controller that stopped scaling gravity would be caught: the drag alone hides it.
    /// </summary>
    public float PredictJumpRise(float baseMaxSpeed, float jumpSpeed, float gravity, float delta = 1f / 60f)
    {
        Vector2 velocity = new(0f, -Mathf.Abs(jumpSpeed));
        float rise = 0f;
        for (int tick = 0; tick < 600 && velocity.Y < 0f; tick++)
        {
            if (tick > 0) velocity.Y += gravity * GravityScale * delta;
            velocity = Resist(velocity, baseMaxSpeed, delta);
            rise -= velocity.Y * delta;
        }
        return rise;
    }

    private void Recompute()
    {
        InMedium = false;
        StagnationIndex = 1f;
        GravityIndex = 0f;
        Drag = 1f;
        GravityScale = 1f;
        SinkSpeed = 0f;
        ExternalDampingMult = 1f;
        Flow = Vector2.Zero;

        foreach (SoftVolume volume in _inside)
        {
            if (!GodotObject.IsInstanceValid(volume)) continue;
            Flow += volume.Flow;
            if (!volume.IsMedium) continue;

            if (!InMedium || volume.StagnationIndex < StagnationIndex)
            {
                StagnationIndex = volume.StagnationIndex;
                GravityIndex = volume.GravityIndex;
            }
            Drag = InMedium ? Mathf.Max(Drag, volume.Drag) : volume.Drag;
            ExternalDampingMult = InMedium
                ? Mathf.Max(ExternalDampingMult, volume.ExternalDampingMult)
                : volume.ExternalDampingMult;
            GravityScale = Mathf.Min(GravityScale, volume.GravityScale);
            if (volume.SinkSpeed > 0f)
                SinkSpeed = SinkSpeed > 0f ? Mathf.Min(SinkSpeed, volume.SinkSpeed) : volume.SinkSpeed;
            InMedium = true;
        }
    }

    /// <summary>
    /// Checks the arithmetic of overlapping volumes and the shape of the two resistance
    /// regimes, by integrating the same three steps a body runs each tick (gravity, input,
    /// resistance). It builds a few detached volumes and frees them; nothing enters the tree.
    /// The shipped tiles' own numbers are measured on a real character by the map content check.
    /// </summary>
    public static List<string> SelfTest()
    {
        var failures = new List<string>();
        void Check(bool ok, string what) { if (!ok) failures.Add(what); }
        static bool Near(float a, float b, float tolerance = 0.01f) => Mathf.Abs(a - b) <= tolerance;

        // A typical fighter (CharacterController's defaults), and a slow walker.
        const float runSpeed = 320f, airAccel = 1900f, slowSpeed = 90f, dt = 1f / 60f;

        var cloud = new SoftVolume { StagnationIndex = 0.3f };
        var sea = new SoftVolume { StagnationIndex = 0.55f, GravityScale = 0.35f, Drag = 5f, SinkSpeed = 96f };
        var kelp = new SoftVolume { StagnationIndex = 0.3f };
        var current = new SoftVolume
        {
            IsMedium = false, StagnationIndex = 1f, GravityIndex = 0f, ExternalDampingMult = 1f,
            Flow = new Vector2(128f, 0f),
        };

        try
        {
            var set = new SoftVolumeSet();
            Vector2 moving = new(400f, 300f);
            Check(!set.InMedium && Near(set.GravityScale, 1f) && set.Flow == Vector2.Zero && Near(set.ExternalDampingMult, 1f),
                "an empty set must be inert: no medium, full gravity, no carry, ordinary damping");
            Check(set.Resist(moving, runSpeed, dt) == moving, "outside any medium nothing may resist a body");

            // Foliage / cloud: the contract since v0.6.18, to the digit.
            set.Enter(cloud);
            Vector2 legacy = set.Resist(moving, runSpeed, dt);
            float legacyStep = Units.Gravity * 0.7f * dt;
            Check(Near(legacy.X, 400f - legacyStep) && Near(legacy.Y, 300f - legacyStep),
                "a plain medium must resist exactly as it did before water existed (g·(1−s) toward s·speed, plus the drift)");
            Check(set.Resist(moving, runSpeed, dt) == cloud.ApplyVelocityResistance(moving, runSpeed, dt),
                "a body in one volume must be resisted exactly as that volume resists");
            Check(Near(set.Resist(new Vector2(0f, 40f), runSpeed, dt).Y, 40f + legacyStep),
                "inside its cap a plain medium still adds the GravityIndex drift");
            set.Exit(cloud);
            Check(!set.InMedium, "leaving the only medium must leave the set inert");

            // A current inside a sea carries; it does not change what the sea does.
            set.Enter(sea);
            set.Enter(current);
            set.Enter(current);
            Check(set.InMedium && set.Buoyant && Near(set.StagnationIndex, 0.55f) && Near(set.Drag, 5f) &&
                  Near(set.GravityScale, 0.35f) && Near(set.SinkSpeed, 96f),
                "a current must not change the medium it runs through");
            Check(set.Flow == new Vector2(128f, 0f), "entering the same current twice must not carry twice");

            // Kelp in the sea: the strictest of each wins.
            set.Enter(kelp);
            Check(Near(set.StagnationIndex, 0.3f) && Near(set.Drag, 5f) && Near(set.GravityScale, 0.35f) && Near(set.SinkSpeed, 96f),
                "overlapping media must combine strictest-first: lowest cap, highest drag, most buoyancy, slowest sink");
            set.Exit(kelp);
            Check(set.InMedium && Near(set.StagnationIndex, 0.55f) && Near(set.SinkSpeed, 96f),
                "leaving an inner volume must not forget the outer one the body is still in");

            set.Exit(sea);
            Check(!set.InMedium && Near(set.GravityScale, 1f) && set.Flow == new Vector2(128f, 0f),
                "a current alone carries, and is not a medium: full gravity, no jump support");
            Check(set.Resist(moving, runSpeed, dt) == moving, "a current alone must not resist");

            // What water does to a body, integrated the way a body integrates it.
            set.Enter(sea);
            Check(Near(Settle(set, runSpeed, 0f, false).Y, 96f, 0.5f) && Near(Settle(set, slowSpeed, 0f, false).Y, 96f, 0.5f),
                "water must hold every body's sink at SinkSpeed, whatever its own top speed");
            Check(Near(Settle(set, runSpeed, 0f, true).Y, 0.55f * runSpeed, 0.5f),
                "diving must sink at the medium's speed cap");
            Check(Settle(set, runSpeed, airAccel, false).X <= 0.55f * runSpeed + 0.5f,
                "with Drag well above 1 a held direction must settle on the speed cap");
            float stroke = set.PredictJumpRise(runSpeed, Units.JumpSpeed, Units.Gravity);
            float airJump = Units.JumpSpeed * Units.JumpSpeed / (2f * Units.Gravity);
            Check(stroke > 0.25f * airJump && stroke < 0.6f * airJump,
                $"a jump in water must be a swimming stroke, well short of an air jump (rose {stroke:0} px of {airJump:0})");
            set.Exit(sea);
            Check(Near(new SoftVolumeSet().PredictJumpRise(runSpeed, Units.JumpSpeed, Units.Gravity), 264.5f, 1f),
                "outside any medium a predicted jump must be the plain ballistic one");

            set.Enter(cloud);
            Check(Settle(set, runSpeed, airAccel, false).X > 0.9f * runSpeed,
                "at Drag 1 a held direction outruns the cap — foliage slows a coast, not a run (v0.6.18 behaviour)");
            set.Exit(cloud);

            // A volume freed under the body (its map was torn down) is forgotten.
            set.Enter(current);
            current.Free();
            current = null;
            set.Prune();
            Check(set.Flow == Vector2.Zero, "a freed volume must stop carrying after Prune");
        }
        finally
        {
            cloud.Free();
            sea.Free();
            kelp.Free();
            current?.Free();
        }
        return failures;

        // Two seconds from rest: gravity, then a held direction (accel > 0), then resistance.
        static Vector2 Settle(SoftVolumeSet set, float speed, float accel, bool dive)
        {
            Vector2 velocity = Vector2.Zero;
            for (int tick = 0; tick < 120; tick++)
            {
                velocity.Y += Units.Gravity * set.GravityScale * dt;
                if (accel > 0f) velocity.X = Mathf.MoveToward(velocity.X, speed, accel * dt);
                velocity = set.Resist(velocity, speed, dt, dive);
            }
            return velocity;
        }
    }
}
