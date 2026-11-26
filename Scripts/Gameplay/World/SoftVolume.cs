using Godot;
using System.Collections.Generic;

/// <summary>
/// An enterable "go-inside" volume — the tree/bush/cloud/water archetype, as
/// opposed to the thin one-way <b>platform</b> you can only land on or cross.
///
/// A body falls directly into this Area2D; it has no standable one-way top. What the
/// volume then does depends on the body:
///
///   • A fighter <b>swims</b> in it (v0.11.1). Up and Down steer exactly as Left and Right
///     do — read, like them, through the body's <c>Controls</c>, so a BOSS swims on its
///     brain's pad and never on the player's keys — the volume's slow <i>drop</i> stands
///     in for gravity, and one resistance acts on both axes: velocity = the fighter's own
///     swimming + the drop. A jump inside is a stroke on top of that, as often as the
///     universal jump cooldown allows.
///     <see cref="SoftVolumeSet.SwimVertical"/>, <see cref="SoftVolumeSet.Swim"/>.
///   • A foe (<see cref="BaseFoe"/>) is <b>carried passively</b>, and so is a fighter in a kit volume that is not
///     <see cref="Swimmable"/>: normal movement, gravity and jumps continue and the volume
///     only resists them, as it has since v0.6.18. <see cref="Resist"/>.
///
/// Settings:
///
///   • StagnationIndex (scene fallback 0.5) — velocity is pulled toward index·maxSpeed,
///     never overwritten on entry. A map tile kind may override this before instantiation.
///   • GravityIndex (0.1) — index·maxSpeed is the <i>drop</i>: the downward speed a swimmer
///     settles on when it stops steering. For a passive body it is a drift added to the cap.
///   • GravityScale (1) — the share of gravity a body keeps inside. Water is well under 1:
///     the rest is buoyancy, and the two forces are simply added. For a swimmer it sets how
///     quickly a rise gives way to the drop.
///   • SinkSpeed (0 = off) — an absolute drop in px/s, the same for a fast body and a slow
///     one, replacing the GravityIndex one. Water sets it; a passive body is held to it too.
///   • Drag (1) — multiplies the resistance. At 1 the resistance is gentler than a body's
///     own acceleration, so a held direction outruns the cap: foliage slows a coast, not a
///     run — on either axis. Water uses about 5, which makes the cap the swimming speed.
///   • Flow (zero) — a steady carry velocity added to everything inside: a current, a
///     column of rising bubbles, falling water.
///   • IsMedium (true) — true for something a body is *in* (foliage, cloud, water): it
///     resists, and a fighter swims in it. False for a pure current: it carries and nothing
///     else, so a waterfall cannot be climbed and a current does not slow you.
///   • Swimmable (true) — false for a volume that belongs to a character's kit and must
///     keep the passive contract (Cleopastar's star).
///
/// A body may be inside several at once (a current inside a sea); <see cref="SoftVolumeSet"/>
/// adds them up, and both body types resolve everything through it.
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
    [Export] public bool Swimmable = true;

    private string _mediumKind;

    /// <summary>
    /// What this volume is, for feedback — the tag of the <c>world</c> cues
    /// <c>soft.enter</c> / <c>soft.exit</c> (<c>Docs/Feedback.gdd</c> §6). Whoever builds a
    /// volume may name it ("cloud", "swamp"); unnamed, it is what the volume's own tuning
    /// says: <c>water</c> when it has a sink speed, <c>current</c> when it only carries,
    /// <c>kit</c> for a character's own volume, else <c>foliage</c>. A cloud and a bush are
    /// tuned alike, so a cloud reads as foliage until its builder names it.
    /// </summary>
    public string MediumKind
    {
        get => _mediumKind ?? (!IsMedium ? "current" : !Swimmable ? "kit" : SinkSpeed > 0f ? "water" : "foliage");
        set => _mediumKind = string.IsNullOrWhiteSpace(value) ? null : value;
    }

    /// <summary>A body crossed between open air and a medium. Raised by the body — which
    /// knows every volume it is in — so a sea laid as forty tiles is entered once, not at
    /// every joint. Position is the body's, magnitude its speed in px/s.</summary>
    public static void RaiseCrossing(CharacterBody2D body, SoftVolume volume, bool entered)
    {
        if (body == null || volume == null || !body.IsInsideTree()) return;
        Vector2 velocity = body.Velocity;
        float speed = velocity.Length();
        var feedback = body as IFeedbackBody;
        CueBus.EmitAt(CueOwners.World, entered ? "soft.enter" : "soft.exit", body.GlobalPosition, body,
            feedback?.Side ?? CombatSide.Player, size: feedback?.FeedbackRadius ?? 0f, magnitude: speed,
            direction: speed > 1f ? velocity / speed : Vector2.Zero, tag: volume.MediumKind);
    }

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

    /// <summary>True if this volume's interior contains a world point. A fighter asks it
    /// about the top of its head: while that is under, Up still pushes; once it is out, the
    /// swimmer rests at the surface. The volume must be in the tree.</summary>
    public bool Covers(Vector2 globalPoint)
    {
        foreach (Node child in GetChildren())
        {
            if (child is not CollisionShape2D node || node.Disabled || node.Shape == null) continue;
            if (Contains(node.Shape, node.GlobalTransform.AffineInverse() * globalPoint)) return true;
        }
        return false;
    }

    private static bool Contains(Shape2D shape, Vector2 point) => shape switch
    {
        RectangleShape2D box => Mathf.Abs(point.X) <= box.Size.X * 0.5f && Mathf.Abs(point.Y) <= box.Size.Y * 0.5f,
        CircleShape2D circle => point.LengthSquared() <= circle.Radius * circle.Radius,
        ConvexPolygonShape2D polygon => Geometry2D.IsPointInPolygon(point, polygon.Points),
        _ => shape.GetRect().HasPoint(point),   // any other shape: its bounding box
    };

    /// <summary>This one volume's passive resistance, as if the body were in nothing else.
    /// Bodies go through <see cref="SoftVolumeSet"/>, which also handles overlaps.</summary>
    public Vector2 ApplyVelocityResistance(Vector2 velocity, float baseMaxSpeed, float delta) =>
        Resist(velocity, baseMaxSpeed, delta, StagnationIndex, GravityIndex, Drag, SinkSpeed);

    /// <summary>What a medium takes off a velocity that is outside its cap, per tick. It is
    /// derived from the physical gravity constant — <c>g × (1 − stagnation) × drag</c> — so
    /// the indices keep their designer-facing meaning.</summary>
    public static float ResistanceStep(float stagnation, float drag, float delta) =>
        Units.Gravity * Mathf.Clamp(1f - stagnation, 0f, 1f) * Mathf.Max(drag, 0f) * delta;

    /// <summary>
    /// The <b>passive</b> rule — foes, and a fighter in a kit volume. Gradually pulls a
    /// body's current intent velocity toward a medium's cap. Normal gravity and a jump
    /// launch happen first; this contributes a resistance delta-v rather than replacing
    /// either.
    ///
    /// Vertically there are two regimes. With no <paramref name="sinkSpeed"/> (foliage,
    /// cloud) the cap is ±stagnation·maxSpeed plus the GravityIndex drift, as it has been
    /// since v0.6.18. With one (water) the medium holds the sink at that absolute speed —
    /// the same for a fast body and a slow one.
    /// </summary>
    public static Vector2 Resist(Vector2 velocity, float baseMaxSpeed, float delta,
        float stagnation, float gravityIndex, float drag, float sinkSpeed)
    {
        float maxInside = stagnation * baseMaxSpeed;
        float step = ResistanceStep(stagnation, drag, delta);

        float targetY = sinkSpeed > 0f
            ? Mathf.Clamp(velocity.Y, -maxInside, sinkSpeed)
            : Mathf.Clamp(velocity.Y, -maxInside, maxInside) + gravityIndex * baseMaxSpeed;

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
/// the sea. <see cref="Flow"/> is the carry velocities added — currents included — with an
/// identical carry counted once, because one stream is laid as several tiles.
/// Outside any medium the set is inert: full gravity, no resistance, no jump support.
///
/// The set answers for both rules a body can be under (see <see cref="SoftVolume"/>): the
/// passive one (<see cref="Resist"/>) and, for a fighter, swimming
/// (<see cref="SwimVertical"/> then <see cref="Swim"/>, with <see cref="Covers"/> for the
/// surface).
///
/// Before v0.10.9 a body remembered a single volume, so leaving an inner one forgot the outer
/// one it was still standing in.
/// </summary>
public sealed class SoftVolumeSet
{
    private readonly List<SoftVolume> _inside = new();

    /// <summary>True while the body is in at least one medium: it is resisted, and its jump
    /// charges are kept full.</summary>
    public bool InMedium { get; private set; }
    /// <summary>True while at least one of those media is one a fighter swims in — every
    /// map volume is; a character's kit volume may not be.</summary>
    public bool Swims { get; private set; }
    public float StagnationIndex { get; private set; } = 1f;
    public float GravityIndex { get; private set; }
    public float Drag { get; private set; } = 1f;
    public float GravityScale { get; private set; } = 1f;
    public float SinkSpeed { get; private set; }
    public float ExternalDampingMult { get; private set; } = 1f;
    public Vector2 Flow { get; private set; }

    /// <summary>The downward speed a swimmer settles on when it stops steering: the medium's
    /// absolute SinkSpeed if it sets one (water), else GravityIndex × the body's top speed.</summary>
    public float Drop(float baseMaxSpeed) => SinkSpeed > 0f ? SinkSpeed : GravityIndex * baseMaxSpeed;

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

    /// <summary>The passive resistance of everything the body is in, for one physics tick.
    /// Call it after gravity, input and a jump launch have been applied to
    /// <paramref name="velocity"/>. Foes use this; so does a fighter that is not swimming.</summary>
    public Vector2 Resist(Vector2 velocity, float baseMaxSpeed, float delta) =>
        InMedium
            ? SoftVolume.Resist(velocity, baseMaxSpeed, delta, StagnationIndex, GravityIndex, Drag, SinkSpeed)
            : velocity;

    /// <summary>
    /// A swimmer's vertical intent for one tick — what stands in for gravity inside a medium.
    /// <paramref name="rise"/> is the held direction: −1 up, +1 down, 0 none.
    ///
    /// Held, it accelerates toward <c>±thrustSpeed + drop</c> exactly as a held Left or Right
    /// accelerates toward ±thrustSpeed: the body's own swimming, plus the drop. Thrust never
    /// brakes: a body already moving faster than its own swimming (a jump launch, a dive into
    /// the volume) is left to the medium, so holding Up cannot make a launch out-rise the air.
    ///
    /// Not held, the medium settles the body on its drop: gravity — the share the medium
    /// leaves of it — takes a rise away, and the body's own friction slows a faster sink.
    /// </summary>
    public float SwimVertical(float velocityY, float rise, float thrustSpeed, float baseMaxSpeed,
        float accel, float friction, float gravity, float delta)
    {
        float drop = Drop(baseMaxSpeed);
        if (rise < 0f)
        {
            float target = -thrustSpeed + drop;
            if (velocityY > target) return Mathf.Max(target, velocityY - accel * delta);
        }
        else if (rise > 0f)
        {
            float target = thrustSpeed + drop;
            if (velocityY < target) return Mathf.Min(target, velocityY + accel * delta);
        }

        float settle = velocityY < drop ? Mathf.Max(gravity * GravityScale, friction) : friction;
        return Mathf.MoveToward(velocityY, drop, settle * delta);
    }

    /// <summary>
    /// The resistance on a swimmer, for one physics tick: call it after input, a jump launch
    /// and <see cref="SwimVertical"/>. Horizontally it is the passive rule to the digit.
    /// Vertically it is the same rule around the drop — the cap is the body's own
    /// ±stagnation·maxSpeed carried down by the drop — so in water a held Up settles on
    /// swimming speed − drop, a held Down on swimming speed + drop.
    /// </summary>
    public Vector2 Swim(Vector2 velocity, float baseMaxSpeed, float delta)
    {
        if (!InMedium) return velocity;
        float maxInside = StagnationIndex * baseMaxSpeed;
        float drop = Drop(baseMaxSpeed);
        float step = SoftVolume.ResistanceStep(StagnationIndex, Drag, delta);
        return new Vector2(
            Mathf.MoveToward(velocity.X, Mathf.Clamp(velocity.X, -maxInside, maxInside), step),
            Mathf.MoveToward(velocity.Y, Mathf.Clamp(velocity.Y, -maxInside + drop, maxInside + drop), step));
    }

    /// <summary>True if a medium the body swims in reaches a world point. Asked about the
    /// top of a fighter's head: can it still swim up, or has it reached the surface?</summary>
    public bool Covers(Vector2 globalPoint)
    {
        foreach (SoftVolume volume in _inside)
        {
            if (!GodotObject.IsInstanceValid(volume) || !volume.IsMedium || !volume.Swimmable) continue;
            if (volume.Covers(globalPoint)) return true;
        }
        return false;
    }

    /// <summary>
    /// How far a jump rises inside this set's media before the body starts to sink — the
    /// model's own answer, integrated tick by tick the way a fighter integrates it: the
    /// launch tick only resists (a jump overrides what came before it); every later tick
    /// settles toward the drop, then resists. It is a jump with nothing held — a stroke
    /// in mid-volume or a push off the bottom, the same thing — in a medium deep enough that
    /// the body never leaves it. The content check holds a real character against this
    /// number, which is how a controller that dropped the share of gravity would be caught:
    /// the drag alone hides it.
    /// </summary>
    public float PredictJumpRise(float baseMaxSpeed, float jumpSpeed, float gravity, float friction, float delta = 1f / 60f)
    {
        float velocityY = -Mathf.Abs(jumpSpeed), rise = 0f;
        for (int tick = 0; tick < 600 && velocityY < 0f; tick++)
        {
            if (Swims)
            {
                if (tick > 0) velocityY = SwimVertical(velocityY, 0f, 0f, baseMaxSpeed, 0f, friction, gravity, delta);
                velocityY = Swim(new Vector2(0f, velocityY), baseMaxSpeed, delta).Y;
            }
            else
            {
                if (tick > 0) velocityY += gravity * GravityScale * delta;
                velocityY = Resist(new Vector2(0f, velocityY), baseMaxSpeed, delta).Y;
            }
            rise -= velocityY * delta;
        }
        return rise;
    }

    /// <summary>
    /// The vertical speed a swimmer settles on with a direction held (<paramref name="rise"/>:
    /// −1 up, +1 down, 0 none) — the model's own answer, integrated from rest tick by tick
    /// the way a fighter integrates it. The content check holds a real character against it,
    /// and asks it of every shipped kind: a medium nobody can swim up in is a trap.
    /// </summary>
    public float PredictSwimSpeed(float rise, float baseMaxSpeed, float accel, float friction, float gravity,
        float seconds = 3f, float delta = 1f / 60f)
    {
        float velocityY = 0f;
        for (int tick = Mathf.RoundToInt(seconds / delta); tick > 0; tick--)
        {
            velocityY = SwimVertical(velocityY, rise, baseMaxSpeed, baseMaxSpeed, accel, friction, gravity, delta);
            velocityY = Swim(new Vector2(0f, velocityY), baseMaxSpeed, delta).Y;
        }
        return velocityY;
    }

    /// <summary>One stream is laid as several tiles, and a body across the joint of two of
    /// them is in one stream, not two: an identical carry from an earlier volume counts once.</summary>
    private bool AlreadyCarrying(int before, Vector2 flow)
    {
        for (int i = 0; i < before; i++)
            if (GodotObject.IsInstanceValid(_inside[i]) && _inside[i].Flow == flow) return true;
        return false;
    }

    private void Recompute()
    {
        InMedium = false;
        Swims = false;
        StagnationIndex = 1f;
        GravityIndex = 0f;
        Drag = 1f;
        GravityScale = 1f;
        SinkSpeed = 0f;
        ExternalDampingMult = 1f;
        Flow = Vector2.Zero;

        for (int i = 0; i < _inside.Count; i++)
        {
            SoftVolume volume = _inside[i];
            if (!GodotObject.IsInstanceValid(volume)) continue;
            if (!AlreadyCarrying(i, volume.Flow)) Flow += volume.Flow;
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
            Swims |= volume.Swimmable;
        }
    }

    /// <summary>
    /// Checks the arithmetic of overlapping volumes and the shape of both rules — passive and
    /// swimming — by integrating the same steps a body runs each tick. It builds a few
    /// detached volumes and frees them; nothing enters the tree. So <see cref="Covers"/>, which
    /// needs world transforms, is left to the map content check's probe, as are the shipped
    /// tiles' own numbers: both are measured there on a real character.
    /// </summary>
    public static List<string> SelfTest()
    {
        var failures = new List<string>();
        void Check(bool ok, string what) { if (!ok) failures.Add(what); }
        static bool Near(float a, float b, float tolerance = 0.01f) => Mathf.Abs(a - b) <= tolerance;

        // A typical fighter (CharacterController's defaults), and a slow walker.
        const float runSpeed = 320f, slowSpeed = 90f;

        var cloud = new SoftVolume { StagnationIndex = 0.3f };
        var sea = new SoftVolume { StagnationIndex = 0.55f, GravityScale = 0.35f, Drag = 5f, SinkSpeed = 96f };
        // the same sea without buoyancy: all of gravity left
        var heavySea = new SoftVolume { StagnationIndex = 0.55f, GravityScale = 1f, Drag = 5f, SinkSpeed = 96f };
        var kelp = new SoftVolume { StagnationIndex = 0.3f };
        var star = new SoftVolume { StagnationIndex = 0.1f, Swimmable = false };
        var current = new SoftVolume
        {
            IsMedium = false, StagnationIndex = 1f, GravityIndex = 0f, ExternalDampingMult = 1f,
            Flow = new Vector2(128f, 0f),
        };
        // the next tile of the same stream, and a different stream crossing it
        var currentOn = new SoftVolume { IsMedium = false, StagnationIndex = 1f, GravityIndex = 0f, Flow = new Vector2(128f, 0f) };
        var bubbles = new SoftVolume { IsMedium = false, StagnationIndex = 1f, GravityIndex = 0f, Flow = new Vector2(0f, -224f) };

        try
        {
            var set = new SoftVolumeSet();
            Vector2 moving = new(400f, 300f);
            Check(!set.InMedium && !set.Swims && Near(set.GravityScale, 1f) && set.Flow == Vector2.Zero && Near(set.ExternalDampingMult, 1f),
                "an empty set must be inert: no medium, full gravity, no carry, ordinary damping");
            Check(set.Resist(moving, runSpeed, TestDt) == moving && set.Swim(moving, runSpeed, TestDt) == moving,
                "outside any medium nothing may resist a body");

            // The passive rule in foliage / cloud: the contract since v0.6.18, to the digit.
            set.Enter(cloud);
            Vector2 legacy = set.Resist(moving, runSpeed, TestDt);
            float legacyStep = Units.Gravity * 0.7f * TestDt;
            Check(Near(legacy.X, 400f - legacyStep) && Near(legacy.Y, 300f - legacyStep),
                "a plain medium must resist a passive body exactly as it did before water existed (g·(1−s) toward s·speed, plus the drift)");
            Check(set.Resist(moving, runSpeed, TestDt) == cloud.ApplyVelocityResistance(moving, runSpeed, TestDt),
                "a body in one volume must be resisted exactly as that volume resists");
            Check(Near(set.Resist(new Vector2(0f, 40f), runSpeed, TestDt).Y, 40f + legacyStep),
                "inside its cap a plain medium still adds the GravityIndex drift to a passive body");
            Check(set.Swims && Near(set.Swim(moving, runSpeed, TestDt).X, legacy.X),
                "a map volume is swum in, and a swimmer's horizontal resistance is the passive rule to the digit");
            set.Exit(cloud);
            Check(!set.InMedium && !set.Swims, "leaving the only medium must leave the set inert");

            // A kit volume resists but is not swum in; one swimmable medium beside it is enough.
            set.Enter(star);
            Check(set.InMedium && !set.Swims, "a volume that is not Swimmable must leave a fighter on the passive rule");
            set.Enter(cloud);
            Check(set.Swims, "one swimmable medium is enough to swim");
            set.Exit(cloud);
            set.Exit(star);

            // A current inside a sea carries; it does not change what the sea does.
            set.Enter(sea);
            set.Enter(current);
            set.Enter(current);
            Check(set.InMedium && set.Swims && Near(set.StagnationIndex, 0.55f) && Near(set.Drag, 5f) &&
                  Near(set.GravityScale, 0.35f) && Near(set.SinkSpeed, 96f),
                "a current must not change the medium it runs through");
            Check(set.Flow == new Vector2(128f, 0f), "entering the same current twice must not carry twice");
            set.Enter(currentOn);
            Check(set.Flow == new Vector2(128f, 0f),
                "a body across the joint of two tiles of one stream is in one stream: an identical carry must count once");
            set.Enter(bubbles);
            Check(set.Flow == new Vector2(128f, -224f), "two different streams that cross must both carry");
            set.Exit(current);
            Check(set.Flow == new Vector2(128f, -224f), "leaving one tile of a stream for the next must not drop its carry");
            set.Exit(bubbles);
            set.Exit(currentOn);
            set.Enter(current);

            // Kelp in the sea: the strictest of each wins.
            set.Enter(kelp);
            Check(Near(set.StagnationIndex, 0.3f) && Near(set.Drag, 5f) && Near(set.GravityScale, 0.35f) && Near(set.SinkSpeed, 96f),
                "overlapping media must combine strictest-first: lowest cap, highest drag, most buoyancy, slowest sink");
            set.Exit(kelp);
            Check(set.InMedium && Near(set.StagnationIndex, 0.55f) && Near(set.SinkSpeed, 96f),
                "leaving an inner volume must not forget the outer one the body is still in");

            set.Exit(sea);
            Check(!set.InMedium && !set.Swims && Near(set.GravityScale, 1f) && set.Flow == new Vector2(128f, 0f),
                "a current alone carries, and is not a medium: full gravity, nothing to swim in");
            Check(set.Resist(moving, runSpeed, TestDt) == moving && set.Swim(moving, runSpeed, TestDt) == moving,
                "a current alone must not resist");

            // Water, the passive rule: every body is held to the sink.
            set.Enter(sea);
            Check(Near(SettlePassive(set, runSpeed).Y, 96f, 0.5f) && Near(SettlePassive(set, slowSpeed).Y, 96f, 0.5f),
                "water must hold every passive body's sink at SinkSpeed, whatever its own top speed");

            // Water, swimming: the body's own 0.55 × speed on either axis, plus the drop.
            float cap = 0.55f * runSpeed;
            Check(Near(set.Drop(runSpeed), 96f) && Near(set.Drop(slowSpeed), 96f), "water's drop is its SinkSpeed, the same for a fast body and a slow one");
            Check(Near(Swum(set, runSpeed, 0f, 0f).Y, 96f, 0.5f), "a swimmer left alone in water must sink at the drop");
            Check(Near(Swum(set, runSpeed, 0f, -1f).Y, -cap + 96f, 0.5f), "swimming up in water must settle on swimming speed − drop");
            Check(Near(Swum(set, runSpeed, 0f, 1f).Y, cap + 96f, 0.5f), "swimming down in water must settle on swimming speed + drop");
            Vector2 across = Swum(set, runSpeed, 1f, 0f);
            Check(Near(across.X, cap, 0.5f) && Near(across.Y, 96f, 0.5f),
                "swimming sideways must settle on the speed cap while the drop carries the body down");
            Vector2 upAndAcross = Swum(set, runSpeed, 1f, -1f);
            Check(Near(upAndAcross.X, cap, 0.5f) && Near(upAndAcross.Y, -cap + 96f, 0.5f),
                "the two axes must not share one speed: up-and-across is the full speed on each");
            float push = set.PredictJumpRise(runSpeed, Units.JumpSpeed, Units.Gravity, TestAirFriction);
            float airJump = Units.JumpSpeed * Units.JumpSpeed / (2f * Units.Gravity);
            Check(push > 0.25f * airJump && push < 0.6f * airJump,
                $"a jump in water must be a stroke, well short of an air jump (rose {push:0} px of {airJump:0})");
            Check(NeverOutpacesTheAir(set, runSpeed), "a jump launched in water must never be faster than the same jump in the open, even with Up held");
            set.Exit(sea);
            set.Enter(heavySea);
            float heavyPush = set.PredictJumpRise(runSpeed, Units.JumpSpeed, Units.Gravity, TestAirFriction);
            Check(push > 1.15f * heavyPush,
                $"buoyancy must lengthen a jump in water: the share of gravity a medium leaves is what takes a swimmer's rise away (rose {push:0} px, {heavyPush:0} px with all of gravity)");
            set.Exit(heavySea);
            Check(Near(new SoftVolumeSet().PredictJumpRise(runSpeed, Units.JumpSpeed, Units.Gravity, TestAirFriction), 264.5f, 1f),
                "outside any medium a predicted jump must be the plain ballistic one");

            // Foliage / cloud, swimming (Drag 1): a slow drop, and Up steers as Right does.
            set.Enter(cloud);
            float drift = 0.1f * runSpeed;
            Check(Near(set.Drop(runSpeed), drift), "a plain medium's drop is GravityIndex × the body's top speed");
            Check(Near(Swum(set, runSpeed, 0f, 0f).Y, drift, 0.5f),
                "a swimmer left alone in a plain medium must float down at its drop, not fall");
            Vector2 held = Swum(set, runSpeed, 1f, -1f);
            Check(held.X > 0.9f * runSpeed,
                "at Drag 1 a held direction outruns the cap — foliage slows a coast, not a run (v0.6.18 behaviour)");
            Check(Near(drift - held.Y, held.X, 0.5f), "Up must steer exactly as Right does: the same speed, carried down by the drop");
            Check(Near(Swum(set, runSpeed, 0f, 1f).Y - drift, held.X, 0.5f), "Down must steer exactly as Right does");
            Check(NeverOutpacesTheAir(set, runSpeed), "a jump launched in foliage must never be faster than the same jump in the open, even with Up held");
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
            heavySea.Free();
            kelp.Free();
            star.Free();
            currentOn.Free();
            bubbles.Free();
            current?.Free();
        }
        return failures;

        // Two seconds from rest under the passive rule: gravity, then resistance.
        static Vector2 SettlePassive(SoftVolumeSet set, float speed)
        {
            Vector2 velocity = Vector2.Zero;
            for (int tick = 0; tick < 120; tick++)
            {
                velocity.Y += Units.Gravity * set.GravityScale * TestDt;
                velocity = set.Resist(velocity, speed, TestDt);
            }
            return velocity;
        }

        // Two seconds from rest as a swimmer, in the order a fighter runs them: the vertical
        // swim (rise: −1 up, +1 down), a held Right (across: 1) or none, then resistance.
        static Vector2 Swum(SoftVolumeSet set, float speed, float across, float rise)
        {
            Vector2 velocity = Vector2.Zero;
            for (int tick = 0; tick < 120; tick++)
            {
                velocity.Y = set.SwimVertical(velocity.Y, rise, speed, speed, TestAirAccel, TestAirFriction, Units.Gravity, TestDt);
                velocity.X = Mathf.MoveToward(velocity.X, across * speed, (across != 0f ? TestAirAccel : TestAirFriction) * TestDt);
                velocity = set.Swim(velocity, speed, TestDt);
            }
            return velocity;
        }

        // A jump launched inside the set with Up held, against the same jump in open air, for
        // the first fifth of a second: the medium may only ever slow it.
        static bool NeverOutpacesTheAir(SoftVolumeSet set, float speed)
        {
            float inside = -Units.JumpSpeed, open = -Units.JumpSpeed;
            for (int tick = 0; tick < 12; tick++)
            {
                if (tick > 0)
                {
                    inside = set.SwimVertical(inside, -1f, speed, speed, TestAirAccel, TestAirFriction, Units.Gravity, TestDt);
                    open += Units.Gravity * TestDt;
                }
                inside = set.Swim(new Vector2(0f, inside), speed, TestDt).Y;
                if (inside < open - 0.01f) return false;
            }
            return true;
        }
    }

    // CharacterController's default momentum, and one physics tick.
    private const float TestAirAccel = 1900f, TestAirFriction = 700f, TestDt = 1f / 60f;
}
