using Godot;
using System;
using System.Collections.Generic;

/// <summary>Per-Shift damage history. It belongs to the volley rather than a character
/// node so Gliding stars keep their intended reduction after a protagonist switch.</summary>
public sealed class CleoGlideVolley
{
    private readonly Dictionary<ulong, int> _hitsByTarget = new();

    public float ConsumeMultiplier(BaseFoe target)
    {
        if (target == null) return 1f;
        ulong id = target.GetInstanceId();
        _hitsByTarget.TryGetValue(id, out int priorHits);
        _hitsByTarget[id] = priorHits + 1;
        return priorHits switch { 0 => 1f, 1 => 0.65f, _ => 0.30f };
    }
}

/// <summary>Cleopastar's star interior is a real SoftVolume but belongs only to
/// Cleopastar; a protagonist swapped in later must not inherit its movement drag.</summary>
public partial class CleoStarSoftVolume : SoftVolume
{
    protected override bool AffectsBody(Node2D body) => body is Cleopastar;
}

/// <summary>One independent Cleopastar projectile. Movement (stationary/gliding) and
/// effect (normal/blackhole) are kept independent so either skill can change only
/// its own axis. The projectile collides with Ground and Platform; stationary Normal
/// stars additionally contain a Cleopastar-only SoftVolume, never a physical platform.
/// </summary>
public partial class CleoStar : Area2D
{
    private enum Motion { Flight, Hover, Falling, Gliding, Stopped }

    [Export] public float Lifetime = 16f;
    [Export] public float HoverDuration = 1f;
    [Export] public float FallSpeed = Units.Px(0.5f);
    // Visual core diameters are deliberately smaller than the effect trigger radii:
    // a compact star can still have a readable area-of-effect without looking like
    // a character-sized boulder. The pull aura is a subtle range indicator, not a
    // second solid-looking projectile body.
    [Export] public float NormalVisualRadius = Units.Px(0.5f);       // 1 m diameter
    [Export] public float BlackholeVisualRadius = Units.Px(1.25f);   // 2.5 m diameter
    [Export] public float NormalRadius = Units.Px(2f);
    [Export] public float BlackholeRadius = Units.Px(3f);
    [Export] public float PullRadius = Units.Px(3f);                  // 6 m diameter pull ring
    [Export] public float PullSpeed = Units.Px(15f);
    [Export] public float TerrainCollisionWaiverSeconds = 0.2f;
    [Export] public float StationaryDamage = 30f;
    [Export] public float GlidingMaxDamage = 100f;
    [Export] public float GlideStartSpeed = Units.Px(20f);
    [Export] public float GlidingMaxSpeed = Units.Px(40f);
    [Export] public float GlideRampDuration = 1.5f;
    [Export] public float GlideLifetime = 3f;
    [Export] public float BlackholeTickDamage = 10f;
    [Export] public float BlackholeTickInterval = 0.2f;
    [Export] public int BlackholeTickCount = 4;

    public event Action<CleoStar> FlightEnded;
    public event Action<CleoStar> Removed;

    public bool IsStationary => _motion != Motion.Gliding;
    public bool IsResolving => _blackholeSequence;
    public bool IsInFlight => _motion == Motion.Flight;

    private Motion _motion = Motion.Flight;
    private Vector2 _direction = Vector2.Right;
    private Vector2 _lastDirection = Vector2.Right;
    private float _speed;
    private float _minimumDistance;
    private float _maximumDistance;
    private float _travelled;
    private bool _releaseBuffered;
    private bool _flightEnded;
    private float _hoverRemaining;
    private float _lifeRemaining;
    private float _glideElapsed;
    private CleoGlideVolley _volley;
    private float _damageMultiplier = 1f;
    private AmmoController _launchAmmo;
    private bool _blackhole;
    private bool _blackholeSequence;
    private int _ticksDone;
    private float _tickRemaining;
    private CollisionShape2D _contactShape;
    private Area2D _terrainProbe;
    private CollisionShape2D _terrainShape;
    private float _terrainCollisionWaiverRemaining;
    private CleoStarSoftVolume _starVolume;
    private CollisionShape2D _starVolumeShape;
    private bool _starVolumeActive;
    private readonly HashSet<Cleopastar> _insideStarVolume = new();
    private string _pullSource;
    private readonly List<BaseFoe> _pulledFoes = new();
    private readonly List<BaseFoe> _seenFoes = new();

    public override void _Ready()
    {
        AddToGroup("cleopastar_star");
        _contactShape = GetNode<CollisionShape2D>("CollisionShape2D");
        BodyEntered += OnBodyEntered;

        // Effect contact deliberately uses the larger trigger radius, but terrain
        // collision follows the compact rendered core. Otherwise a 2–3 m effect
        // ring touches a nearby floor or wall as soon as the star is born.
        SetCollisionMaskValue(Units.LayerPlayer, false);
        SetCollisionMaskValue(Units.LayerGround, false);
        SetCollisionMaskValue(Units.LayerPlatform, false);
        SetCollisionMaskValue(Units.LayerFoes, true);

        _terrainProbe = new Area2D { Name = "TerrainProbe", CollisionLayer = 0, CollisionMask = 0 };
        _terrainProbe.SetCollisionMaskValue(Units.LayerGround, true);
        _terrainProbe.SetCollisionMaskValue(Units.LayerPlatform, true);
        _terrainShape = new CollisionShape2D
        {
            Name = "CollisionShape2D",
            Shape = new CircleShape2D { Radius = NormalVisualRadius },
        };
        _terrainProbe.AddChild(_terrainShape);
        _terrainProbe.BodyEntered += OnTerrainBodyEntered;
        AddChild(_terrainProbe);

        // A stationary Normal star is enterable rather than standable. The core's
        // small SoftVolume slows only Cleopastar (StagnationIndex 0.1) and supplies
        // the exhausted-jump check without creating a Platform-layer body that can
        // collide with other stars.
        _starVolume = new CleoStarSoftVolume
        {
            Name = "StarSoftVolume",
            CollisionLayer = 0,
            CollisionMask = 0,
            StagnationIndex = 0.1f,
            GravityIndex = 0.1f,
        };
        _starVolume.SetCollisionMaskValue(Units.LayerPlayer, true);
        _starVolumeShape = new CollisionShape2D
        {
            Name = "CollisionShape2D",
            Shape = new CircleShape2D { Radius = NormalVisualRadius },
            Disabled = true,
        };
        _starVolume.AddChild(_starVolumeShape);
        _starVolume.BodyEntered += OnStarVolumeBodyEntered;
        _starVolume.BodyExited += OnStarVolumeBodyExited;
        AddChild(_starVolume);
    }

    /// <summary>Called after AddChild so _Ready has created the terrain and star-volume probes.</summary>
    public void Init(Vector2 direction, float speed, float minDistance,
        float maxDistance, float damageMultiplier, AmmoController launchAmmo)
    {
        _direction = direction.LengthSquared() > 0.0001f ? direction.Normalized() : Vector2.Right;
        _lastDirection = _direction;
        _speed = speed;
        _minimumDistance = minDistance;
        _maximumDistance = maxDistance;
        _damageMultiplier = damageMultiplier;
        _launchAmmo = launchAmmo;
        _lifeRemaining = Lifetime;
        _terrainCollisionWaiverRemaining = TerrainCollisionWaiverSeconds;
        _pullSource = $"CleoStar:{GetInstanceId()}";
        RefreshCollisionAndVolume();
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;

        _lifeRemaining -= dt;
        if (_lifeRemaining <= 0f) { NotifyFlightEnded(); QueueFree(); return; }

        UpdateMotion(dt);
        UpdateTerrainCollisionWaiver(dt);
        if (_blackhole) UpdatePull();
        else ClearPulls();
        UpdateBlackholeSequence(dt);
        QueueRedraw();
    }

    public void ReleaseFlight()
    {
        if (_motion != Motion.Flight) return;
        if (_travelled >= _minimumDistance) EnterHover();
        else _releaseBuffered = true;
    }

    /// <summary>Converts a stationary star to a Gliding star. The captured aim point
    /// determines direction exactly once; it is not a destination or homing target.</summary>
    public void BeginGlide(Vector2 capturedAim, CleoGlideVolley volley)
    {
        if (!IsStationary) return;
        Vector2 toAim = capturedAim - GlobalPosition;
        _direction = toAim.LengthSquared() > 0.0001f ? toAim.Normalized() : _lastDirection;
        _lastDirection = _direction;
        _motion = Motion.Gliding;
        _glideElapsed = 0f;
        _volley = volley;
        _lifeRemaining = GlideLifetime;
        NotifyFlightEnded();
        RefreshCollisionAndVolume();
    }

    /// <summary>Returns false while a triggered Blackhole is resolving; that sequence
    /// has already committed its collision outcome.</summary>
    public bool ToggleBlackhole()
    {
        if (_blackholeSequence) return false;
        _blackhole = !_blackhole;
        RefreshCollisionAndVolume();
        return true;
    }

    private void UpdateMotion(float dt)
    {
        switch (_motion)
        {
            case Motion.Flight:
            {
                float step = _speed * dt;
                GlobalPosition += _direction * step;
                _travelled += step;
                _lastDirection = _direction;
                if (_travelled >= _maximumDistance || (_releaseBuffered && _travelled >= _minimumDistance))
                    EnterHover();
                break;
            }
            case Motion.Hover:
                _hoverRemaining -= dt;
                if (_hoverRemaining <= 0f)
                {
                    _motion = Motion.Falling;
                    RefreshCollisionAndVolume();
                }
                break;
            case Motion.Falling:
                GlobalPosition += Vector2.Down * FallSpeed * dt;
                break;
            case Motion.Gliding:
            {
                _glideElapsed += dt;
                float t = Mathf.Clamp(_glideElapsed / GlideRampDuration, 0f, 1f);
                float glideSpeed = Mathf.Lerp(GlideStartSpeed, GlidingMaxSpeed, t);
                GlobalPosition += _direction * glideSpeed * dt;
                _lastDirection = _direction;
                break;
            }
        }
    }

    private void EnterHover()
    {
        if (_motion != Motion.Flight) return;
        _motion = Motion.Hover;
        _hoverRemaining = HoverDuration;
        NotifyFlightEnded();
        RefreshCollisionAndVolume();
    }

    private void OnBodyEntered(Node2D body)
    {
        if (body == null) return;
        // The broad effect trigger is for foes only. Cleopastar is never a direct
        // projectile collision target; her SoftVolume has exhausted-jump rules.
        if (body is CharacterController) return; // no player-vs-player effect in this slice

        if (body is BaseFoe foe)
        {
            if (_blackhole) StartBlackholeSequence(stopMotion: false);
            else ExplodeNormal();
            return;
        }

    }

    private void OnTerrainBodyEntered(Node2D body)
    {
        if (body == null || _terrainCollisionWaiverRemaining > 0f)
            return;

        // Every CleoStar projectile is blocked by Ground/Platform and passes through
        // SoftVolumes (which are Areas, not bodies). This is the project-wide default.
        if (_blackhole) StartBlackholeSequence(stopMotion: true);
        else ExplodeNormal();
    }

    private void UpdateTerrainCollisionWaiver(float dt)
    {
        if (_terrainCollisionWaiverRemaining <= 0f) return;
        _terrainCollisionWaiverRemaining -= dt;
        if (_terrainCollisionWaiverRemaining > 0f || _terrainProbe == null) return;

        // BodyEntered events that occurred during the waiver are not replayed by
        // Godot, so resolve any terrain still overlapping at expiry. This preserves
        // intentional close terrain hits without the spawn-frame false positive.
        foreach (Node2D body in _terrainProbe.GetOverlappingBodies())
        {
            OnTerrainBodyEntered(body);
            if (IsQueuedForDeletion()) break;
        }
    }

    /// <summary>Consume a usable stationary Normal star only when Cleopastar has no
    /// ordinary jump charges left and is currently inside this star's SoftVolume.</summary>
    public bool TryConsumeForExhaustedJump(Cleopastar cleo)
    {
        if (!_starVolumeActive || cleo == null || !_insideStarVolume.Contains(cleo)
            || _blackhole || _motion == Motion.Gliding || _blackholeSequence)
            return false;
        ExplodeNormal(); // Cleopastar is intentionally excluded from this explosion.
        return true;
    }

    private void ExplodeNormal()
    {
        if (!IsInsideTree()) return;
        NotifyFlightEnded();

        float damage = StationaryDamage;
        float knockback = 0f;
        if (_motion == Motion.Gliding)
        {
            float t = Mathf.Clamp(_glideElapsed / GlideRampDuration, 0f, 1f);
            damage = Mathf.Lerp(StationaryDamage, GlidingMaxDamage, t);
            knockback = Mathf.Lerp(0f, Units.Px(6f), t);
        }

        foreach (Node node in GetTree().GetNodesInGroup("enemy"))
        {
            if (node is not BaseFoe foe) continue;
            Vector2 to = foe.GlobalPosition - GlobalPosition;
            float distance = to.Length();
            if (distance - foe.HitRadius > NormalRadius) continue;

            float damageMult = _motion == Motion.Gliding ? _volley?.ConsumeMultiplier(foe) ?? 1f : 1f;
            Vector2 push = knockback > 0f
                ? (distance > 0.001f ? to / distance : _lastDirection) * knockback : Vector2.Zero;
            foe.TakeHit(new HitInfo(damage * _damageMultiplier * damageMult, push), GlobalPosition);
        }

        QueueFree();
    }

    private void StartBlackholeSequence(bool stopMotion)
    {
        if (_blackholeSequence) return;
        _blackholeSequence = true;
        if (stopMotion) _motion = Motion.Stopped;
        _ticksDone = 0;
        _tickRemaining = BlackholeTickInterval;
        RefreshCollisionAndVolume();
    }

    private void UpdateBlackholeSequence(float dt)
    {
        if (!_blackholeSequence) return;
        _tickRemaining -= dt;
        while (_ticksDone < BlackholeTickCount && _tickRemaining <= 0f)
        {
            _tickRemaining += BlackholeTickInterval;
            ApplyBlackholeTick();
            _ticksDone++;
        }
        if (_ticksDone >= BlackholeTickCount) QueueFree();
    }

    private void ApplyBlackholeTick()
    {
        foreach (Node node in GetTree().GetNodesInGroup("enemy"))
        {
            if (node is not BaseFoe foe) continue;
            if (foe.GlobalPosition.DistanceTo(GlobalPosition) > PullRadius) continue;

            foe.TakeHit(new HitInfo(BlackholeTickDamage * _damageMultiplier, Vector2.Zero, 0.2f), GlobalPosition);
            if (IsInstanceValid(foe) && foe.CurrentHp < foe.MaxHP * 0.10f)
                foe.Execute();
        }
    }

    private void UpdatePull()
    {
        _seenFoes.Clear();
        foreach (Node node in GetTree().GetNodesInGroup("enemy"))
        {
            if (node is not BaseFoe foe || !IsInstanceValid(foe)) continue;
            Vector2 toCenter = GlobalPosition - foe.GlobalPosition;
            float distance = toCenter.Length();
            if (distance > PullRadius) continue;

            Vector2 velocity = distance > 0.001f ? toCenter / distance * PullSpeed : Vector2.Zero;
            foe.SetContinuousExternalVelocity(_pullSource, velocity);
            _seenFoes.Add(foe);
            if (!_pulledFoes.Contains(foe)) _pulledFoes.Add(foe);
        }

        for (int i = _pulledFoes.Count - 1; i >= 0; i--)
        {
            BaseFoe foe = _pulledFoes[i];
            if (!IsInstanceValid(foe) || !_seenFoes.Contains(foe))
            {
                if (IsInstanceValid(foe)) foe.ClearContinuousExternalVelocity(_pullSource);
                _pulledFoes.RemoveAt(i);
            }
        }
    }

    private void ClearPulls()
    {
        foreach (BaseFoe foe in _pulledFoes)
            if (IsInstanceValid(foe)) foe.ClearContinuousExternalVelocity(_pullSource);
        _pulledFoes.Clear();
        _seenFoes.Clear();
    }

    private void RefreshCollisionAndVolume()
    {
        if (_contactShape?.Shape is CircleShape2D circle)
            circle.Radius = _blackhole ? BlackholeRadius : NormalRadius;
        if (_terrainShape?.Shape is CircleShape2D terrainCircle)
            terrainCircle.Radius = _blackhole ? BlackholeVisualRadius : NormalVisualRadius;
        if (_starVolumeShape?.Shape is CircleShape2D volumeCircle)
            volumeCircle.Radius = NormalVisualRadius;
        bool canEnter = !_blackhole && (_motion == Motion.Hover || _motion == Motion.Falling);
        SetStarVolumeActive(canEnter);
    }

    private void SetStarVolumeActive(bool value)
    {
        if (_starVolumeActive == value) return;
        _starVolumeActive = value;
        if (_starVolumeShape != null)
            _starVolumeShape.SetDeferred(CollisionShape2D.PropertyName.Disabled, !value);
        if (!value) ClearStarVolumeOccupants();
    }

    private void OnStarVolumeBodyEntered(Node2D body)
    {
        if (body is Cleopastar cleo) _insideStarVolume.Add(cleo);
    }

    private void OnStarVolumeBodyExited(Node2D body)
    {
        if (body is Cleopastar cleo) _insideStarVolume.Remove(cleo);
    }

    private void ClearStarVolumeOccupants()
    {
        foreach (Cleopastar cleo in _insideStarVolume)
            if (IsInstanceValid(cleo)) cleo.ExitSoftVolume(_starVolume);
        _insideStarVolume.Clear();
    }

    private void NotifyFlightEnded()
    {
        if (_flightEnded) return;
        _flightEnded = true;
        // A star may finish after its source body was Tab-switched out. This controller
        // is deliberately data-only; its state was saved by the source body before it
        // left, and RequestReload/interval persistence makes this update survive too.
        _launchAmmo?.StartAttackInterval();
        FlightEnded?.Invoke(this);
    }

    public override void _ExitTree()
    {
        ClearStarVolumeOccupants();
        ClearPulls();
        NotifyFlightEnded();
        Removed?.Invoke(this);
    }

    public override void _Draw()
    {
        float radius = _blackhole ? BlackholeVisualRadius : NormalVisualRadius;
        Color edge = _blackhole ? new Color(0.45f, 0.1f, 0.8f, 0.9f) : new Color(0.95f, 0.8f, 0.2f, 0.9f);
        Color fill = _blackhole ? new Color(0.16f, 0.03f, 0.32f, 0.75f) : new Color(0.15f, 0.85f, 0.8f, 0.7f);
        DrawCircle(Vector2.Zero, radius, fill);
        DrawArc(Vector2.Zero, radius, 0f, Mathf.Tau, 32, edge, 3f);
        if (_blackhole)
            DrawArc(Vector2.Zero, PullRadius, 0f, Mathf.Tau, 32, new Color(0.45f, 0.1f, 0.8f, 0.12f), 1f);
    }
}
