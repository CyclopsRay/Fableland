using Godot;

/// <summary>
/// One pumpkin of PumpKing's Black Prison (<c>Docs/PumpKing.gdd</c>). It grows on the prison's
/// vines for <see cref="BlackPrison.PumpkinGrowSeconds"/> — harmless, a tell — and is then flung
/// outward. In flight it falls under its thrower's share of gravity and bursts on the first body
/// of the other side it touches (its damage, no shove: a prisoner stays in the prison) or on the
/// ground, or after <see cref="LifetimeSeconds"/>. It outlives the prison that grew it.
///
/// Hits are tested by distance against <see cref="CombatTargets.HostileTo"/>, like the head's
/// blast, so it needs no collision layers and fights for whichever side grew it. TODO(art): the
/// placeholder below is a scratchboard pumpkin drawn in code.
/// </summary>
public partial class PrisonPumpkin : Node2D
{
    public const float LifetimeSeconds = 2f;

    public float Radius { get; private set; }
    public float Damage { get; private set; }
    public bool Growing => _begun && _grow > 0f && !_done;
    public bool Flying => _begun && _grow <= 0f && !_done;
    /// <summary>It has burst — on a body or on the ground.</summary>
    public bool Done => _done;
    /// <summary>What it dealt when it burst on a body (0 on the ground).</summary>
    public float Dealt { get; private set; }

    private Vector2 _velocity;
    private float _grow;
    private float _growFor;
    private float _age;
    private float _fade;
    private float _spin;
    private float _gravityScale = 1f;
    private CombatSide _side;
    private string _cueOwner = "pumpking";
    private string _cueClass = CueOwners.Fighter;
    private Node2D _thrower;
    private bool _begun;
    private bool _done;

    private const float FadeSeconds = 0.25f;

    /// <summary>Call right after AddChild, with GlobalPosition set where it grows.</summary>
    public void Init(Vector2 launch, float radius, float damage, float growSeconds, CombatSide side,
        string cueOwner, string cueClass, Node2D thrower, float gravityScale, float size01)
    {
        _velocity = launch;
        Radius = radius;
        Damage = damage;
        _grow = _growFor = Mathf.Max(0f, growSeconds);
        _side = side;
        _cueOwner = cueOwner;
        _cueClass = cueClass;
        _thrower = thrower;
        _gravityScale = gravityScale;
        _spin = (launch.X >= 0f ? 1f : -1f) * Mathf.Lerp(9f, 5f, size01);   // small ones spin faster
        _begun = true;
        QueueRedraw();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!_begun) return;
        float dt = (float)delta;

        if (_done)
        {
            _fade -= dt;
            Modulate = new Color(1f, 1f, 1f, Mathf.Clamp(_fade / FadeSeconds, 0f, 1f));
            if (_fade <= 0f) QueueFree();
            return;
        }

        if (_grow > 0f)
        {
            _grow -= dt;
            QueueRedraw();
            if (_grow > 0f) return;
        }

        _age += dt;
        if (_age >= LifetimeSeconds) { Burst(); return; }

        _velocity.Y += Units.Gravity * _gravityScale * dt;
        Vector2 from = GlobalPosition;
        Vector2 to = from + _velocity * dt;
        GlobalPosition = to;
        Rotation += _spin * dt;

        if (TryStrike()) return;
        if (HitsGround(from, to, out Vector2 at))
        {
            GlobalPosition = at;
            Burst();
        }
    }

    /// <summary>Touching a body it fights: its damage, credited to the thrower, and it bursts.</summary>
    private bool TryStrike()
    {
        foreach (ICombatTarget target in CombatTargets.HostileTo(GetTree(), _side))
        {
            if (target.CurrentHp <= 0f) continue;
            if (target.GlobalPosition.DistanceTo(GlobalPosition) - target.HitRadius > Radius) continue;
            Node2D thrower = _thrower != null && IsInstanceValid(_thrower) ? _thrower : null;
            Dealt = target.TakeHit(new HitInfo(Damage, Vector2.Zero).FromRanged(_cueOwner, "ult.pumpkin", thrower),
                GlobalPosition);
            if (Dealt > 0f && thrower is CharacterController body) body.ReportDamageDealt(Dealt);
            Finish();
            return true;
        }
        return false;
    }

    /// <summary>The ground (and a platform it falls onto) stops it.</summary>
    private bool HitsGround(Vector2 from, Vector2 to, out Vector2 at)
    {
        at = to;
        PhysicsDirectSpaceState2D space = GetWorld2D()?.DirectSpaceState;
        if (space == null) return false;
        uint mask = 1u << (Units.LayerGround - 1);
        if (_velocity.Y > 0f) mask |= 1u << (Units.LayerPlatform - 1);
        var query = PhysicsRayQueryParameters2D.Create(from, to + (to - from).Normalized() * Radius, mask);
        Godot.Collections.Dictionary hit = space.IntersectRay(query);
        if (hit.Count == 0) return false;
        at = (Vector2)hit["position"];
        return true;
    }

    /// <summary>On the ground or at the end of its flight: it splits, harming nobody.</summary>
    private void Burst()
    {
        CueBus.EmitAt(_cueOwner, "ult.pumpkin.splat", GlobalPosition, null, _side, size: Radius,
            direction: _velocity.Normalized(), ownerClass: _cueClass);
        Finish();
    }

    private void Finish()
    {
        _done = true;
        _fade = FadeSeconds;
        _velocity = Vector2.Zero;
        QueueRedraw();
    }

    // ── Placeholder art (TODO(art), PumpKing.gdd: "the pumpkins") ──────────────
    private static readonly Color Rind = new("#D8323A");
    private static readonly Color RindDeep = new("#7A161C");
    private static readonly Color Stem = new("#3F6FD8");
    private static readonly Color Glow = new("#FFD23F");

    public override void _Draw()
    {
        if (!_begun) return;
        float grown = _growFor > 0f ? Mathf.Clamp(1f - _grow / _growFor, 0f, 1f) : 1f;
        float r = Radius * Mathf.Lerp(0.2f, 1f, grown);
        if (_done)
        {
            // Split: two halves and the light inside.
            DrawCircle(new Vector2(-r * 0.6f, 0f), r * 0.6f, RindDeep);
            DrawCircle(new Vector2(r * 0.6f, 0f), r * 0.6f, RindDeep);
            DrawCircle(Vector2.Zero, r * 0.45f, new Color(Glow, 0.8f));
            return;
        }
        if (Growing) DrawLine(new Vector2(0f, -r), new Vector2(0f, -r - Units.Px(0.6f)), Stem, 2f);   // the vine it hangs from
        DrawCircle(Vector2.Zero, r, Rind);
        DrawArc(Vector2.Zero, r * 0.55f, -1.2f, 1.2f, 8, RindDeep, 1.5f);
        DrawArc(Vector2.Zero, r * 0.55f, Mathf.Pi - 1.2f, Mathf.Pi + 1.2f, 8, RindDeep, 1.5f);
        DrawArc(Vector2.Zero, r, 0f, Mathf.Tau, 20, Colors.Black, 1.5f);
        DrawLine(new Vector2(0f, -r), new Vector2(r * 0.2f, -r * 1.35f), Stem, 2.5f);
        DrawColoredPolygon(new[] { new Vector2(-r * 0.35f, -r * 0.1f), new Vector2(-r * 0.1f, -r * 0.1f), new Vector2(-r * 0.22f, -r * 0.35f) }, Glow);
        DrawColoredPolygon(new[] { new Vector2(r * 0.1f, -r * 0.1f), new Vector2(r * 0.35f, -r * 0.1f), new Vector2(r * 0.22f, -r * 0.35f) }, Glow);
    }
}
