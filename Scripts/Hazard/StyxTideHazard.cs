using Godot;
using System;
using System.Collections.Generic;
using Fableland.Map;

/// <summary>
/// The Styx Tide (Docs/Zone6.gdd §4.2): the river at the horizon of the Void rears up and
/// sweeps the whole floor from one side to the other. A wall of water <see cref="HeightM"/>
/// tall and <see cref="CrestWidthM"/> deep, running at <see cref="SpeedMps"/>: jump it, or it
/// hits hard and carries you with it. Only up escapes it — which is the point of the Void
/// Frontier's gravity: at gravity ×0.5 a fighter hangs above it for over a second, at ×1 a
/// well-timed jump just clears it, from ×1.3 a single jump cannot (measured by the zone-6
/// check, Zone6Check §3).
///
/// Built on the tsunami's rules (<see cref="TsunamiHazard"/>): a moving field that hits each
/// body once and names itself, not a body, as the hitter. It hits the player's side only —
/// it is the final boss's attack, not the world's — and it does not care how a body got
/// above it: standing on a slab higher than the wave is as safe as being in the air.
/// </summary>
public partial class StyxTideHazard : Area2D
{
    public const float HeightM = 5.5f;
    public const float CrestWidthM = 3f;
    public const float SpeedMps = 14f;
    /// <summary>Share of the victim's max HP one wave takes.</summary>
    public const float DamageShareOfMaxHp = 0.30f;
    /// <summary>The hit's no-control window: long enough to be carried, short enough to land.</summary>
    public const float NoControlSeconds = 0.6f;
    /// <summary>How far the shove at the hit carries a body on its own (m).</summary>
    public const float ShoveCarryM = 8f;
    public const string HitAction = "styx_tide";
    private const string CarrySource = "styx_tide";

    public event Action Finished;

    /// <summary>+1 runs to the right, −1 to the left.</summary>
    public float Direction { get; private set; } = 1f;
    public float FloorY { get; private set; }
    public float Height { get; private set; } = Units.Px(HeightM);
    public float Width { get; private set; } = Units.Px(CrestWidthM);
    public float Speed { get; private set; } = Units.Px(SpeedMps);
    private float _endX;

    private readonly HashSet<ulong> _hit = new();
    private readonly List<CharacterController> _carried = new();
    private bool _finished;
    private float _age;

    /// <summary>How many bodies this wave has hit.</summary>
    public int HitCount => _hit.Count;
    public bool HasHit(Node body) => body != null && _hit.Contains(body.GetInstanceId());

    /// <summary>Sets the wave on the floor line at <paramref name="floorY"/>, rising just
    /// outside one edge of [<paramref name="left"/>, <paramref name="right"/>] and running to
    /// the other. Call before or after AddChild; it moves from the next physics tick.</summary>
    public void Configure(float direction, float floorY, float left, float right)
    {
        Direction = direction >= 0f ? 1f : -1f;
        FloorY = floorY;
        float startX = Direction > 0f ? left - Width : right + Width;
        _endX = Direction > 0f ? right + Width : left - Width;
        Position = new Vector2(startX, floorY);
    }

    public override void _Ready()
    {
        CollisionLayer = 0;
        CollisionMask = 0;
        SetCollisionLayerValue(Units.LayerHazard, true);
        SetCollisionMaskValue(Units.LayerPlayer, true);
        Monitorable = false;
        AddChild(new CollisionShape2D
        {
            Shape = new RectangleShape2D { Size = new Vector2(Width, Height) },
            Position = new Vector2(0f, -Height * 0.5f),        // the wave's foot is on the floor
        });
        BodyEntered += OnBodyEntered;
        BodyExited += OnBodyExited;
        ZIndex = 5;
        QueueRedraw();
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        _age += dt;
        Position += new Vector2(Direction * Speed * dt, 0f);
        if ((Direction > 0f && Position.X >= _endX) || (Direction < 0f && Position.X <= _endX)) Finish();
        else if ((int)(_age * 12f) != (int)((_age - dt) * 12f)) QueueRedraw();   // the foam churns
    }

    public override void _ExitTree()
    {
        foreach (CharacterController body in _carried)
            if (IsInstanceValid(body)) body.ClearContinuousExternalVelocity(CarrySource);
        _carried.Clear();
    }

    private void Finish()
    {
        if (_finished) return;
        _finished = true;
        Finished?.Invoke();
        QueueFree();
    }

    private void OnBodyEntered(Node2D body)
    {
        if (body is not CharacterController fighter || fighter.Side != CombatSide.Player || fighter.IsDefeated) return;
        if (!_hit.Add(body.GetInstanceId())) return;

        // Carried: the river takes the body along for as long as it is inside the wave, and the
        // hit's shove throws it on when the wave lets go.
        fighter.SetContinuousExternalVelocity(CarrySource, new Vector2(Direction * Speed, 0f));
        _carried.Add(fighter);
        float v0 = Mathf.Sqrt(2f * fighter.ExternalDamping * Units.Px(ShoveCarryM));
        Vector2 shove = new Vector2(Direction, -0.25f).Normalized() * v0;
        fighter.TakeHit(new HitInfo(fighter.MaxHP * DamageShareOfMaxHp, shove, NoControlSeconds)
            .FromRanged(CueOwners.Hazard, HitAction), GlobalPosition);
    }

    private void OnBodyExited(Node2D body)
    {
        if (body is not CharacterController fighter) return;
        fighter.ClearContinuousExternalVelocity(CarrySource);
        _carried.Remove(fighter);
    }

    /// <summary>Drawn in the Void Frontier's medium until the wave has art (cyanotype: the
    /// water a Prussian-blue ground, its crest and foam the white of a photogram).</summary>
    public override void _Draw()
    {
        float w = Width, h = Height, d = Direction;
        var prussian = new Color(0.07f, 0.20f, 0.42f, 0.92f);
        var deep = new Color(0.03f, 0.11f, 0.27f, 0.95f);
        var pale = new Color(0.80f, 0.93f, 0.96f, 0.95f);
        // Body: a wall with its face leaning into the run.
        var body = new[]
        {
            new Vector2(-w * 0.5f, 0f), new Vector2(w * 0.5f, 0f),
            new Vector2(w * 0.5f * d + w * 0.12f * d, -h * 0.82f), new Vector2(-w * 0.35f * d, -h),
        };
        if (d < 0f) Array.Reverse(body);
        DrawColoredPolygon(body, prussian);
        DrawColoredPolygon(new[] { new Vector2(-w * 0.5f, 0f), new Vector2(w * 0.5f, 0f), new Vector2(0f, -h * 0.45f) }, deep);
        // Crest: a white curl breaking forward.
        Vector2 lip = new(w * 0.62f * d, -h * 0.78f);
        DrawPolyline(new[] { new Vector2(-w * 0.4f * d, -h), new Vector2(w * 0.2f * d, -h * 1.02f), lip, lip + new Vector2(-w * 0.18f * d, h * 0.08f) }, pale, 5f);
        // Foam: a few white flecks that churn with the wave's age.
        for (int i = 0; i < 6; i++)
        {
            float t = (i * 0.37f + _age * 1.7f) % 1f;
            DrawCircle(new Vector2((t - 0.5f) * w * 0.9f, -h * (0.15f + 0.6f * ((i * 0.53f) % 1f))), 3f + (i % 3), pale);
        }
    }
}

/// <summary>
/// When the Styx rises (Docs/Zone6.gdd §4.2): the Unwritten's signature, run by the boss
/// mission whatever kit the boss fields. One wave every <see cref="IntervalSeconds"/> from a
/// side its own stream picks; below half HP the Rising Tide — a second wave
/// <see cref="RisingTideGapSeconds"/> behind the first; below a quarter the second comes from
/// the other side. Pure scheduling: the wave is a <see cref="StyxTideHazard"/>.
/// </summary>
public sealed class StyxTideSchedule
{
    public const float FirstWaveSeconds = 6f;
    public const float IntervalSeconds = 9f;
    public const float RisingTideGapSeconds = 1.1f;
    public const float RisingTideBelowHp = 0.5f;
    public const float BothSidesBelowHp = 0.25f;

    private readonly Func<float, StyxTideHazard> _launch;
    private readonly DetRandom _rng;
    private float _untilNext = FirstWaveSeconds;
    private float _secondIn = -1f;
    private float _secondDirection;

    public int WavesLaunched { get; private set; }
    public int RisingTides { get; private set; }

    /// <param name="launch">Raises one wave running in the given direction (+1 right, −1 left).</param>
    public StyxTideSchedule(Func<float, StyxTideHazard> launch, DetRandom rng)
    {
        _launch = launch;
        _rng = rng;
    }

    public void Tick(float dt, float bossHpShare)
    {
        if (_secondIn >= 0f)
        {
            _secondIn -= dt;
            if (_secondIn < 0f) Launch(_secondDirection);
        }
        _untilNext -= dt;
        if (_untilNext > 0f) return;
        _untilNext += IntervalSeconds;
        float direction = _rng.Chance(0.5) ? 1f : -1f;
        Launch(direction);
        if (bossHpShare <= RisingTideBelowHp)
        {
            RisingTides++;
            _secondIn = RisingTideGapSeconds;
            _secondDirection = bossHpShare <= BothSidesBelowHp ? -direction : direction;
        }
    }

    private void Launch(float direction)
    {
        if (_launch(direction) != null) WavesLaunched++;
    }
}
