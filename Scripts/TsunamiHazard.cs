using Godot;
using System.Collections.Generic;

/// <summary>
/// A one-shot moving wave: a pyramid-shaped hitbox that sweeps leftward from its
/// spawn point, hits each body at most once with a heavy shove and a hard,
/// fixed-duration no-control window, then despawns once past the arena.
/// </summary>
public partial class TsunamiHazard : Area2D
{
    [Export] public float Width = 576f;          // ~half a 1152px-wide screen
    [Export] public float Height = 320f;         // ~10m tall crest
    [Export] public float Speed = 480f;          // leftward sweep speed
    [Export] public float DespawnX = -300f;      // freed once swept past here
    [Export] public float DamagePercentMaxHP = 0.35f;
    [Export] public float NoControlDuration = 1f; // static, fixed hitstun (not damage-scaled)

    private readonly HashSet<ulong> _hit = new();

    public override void _Ready()
    {
        SetCollisionLayerValue(Units.LayerHazard, true);
        SetCollisionMaskValue(Units.LayerPlayer, true);
        SetCollisionMaskValue(Units.LayerFoes, true);

        AddChild(new CollisionPolygon2D { Polygon = TrianglePoints() });

        BodyEntered += OnBodyEntered;
        QueueRedraw();
    }

    private Vector2[] TrianglePoints() => new[]
    {
        new Vector2(-Width * 0.5f, 0f),
        new Vector2(Width * 0.5f, 0f),
        new Vector2(0f, -Height),
    };

    public override void _PhysicsProcess(double delta)
    {
        Position += new Vector2(-Speed * (float)delta, 0f);
        if (GlobalPosition.X < DespawnX) QueueFree();
    }

    private void OnBodyEntered(Node2D body)
    {
        ulong id = body.GetInstanceId();
        if (_hit.Contains(id)) return;
        _hit.Add(id);

        Vector2 dir = new Vector2(-1f, -0.15f).Normalized();   // swept left, slightly up
        float damping = body switch
        {
            CharacterController c => c.ExternalDamping,
            BaseFoe e => e.ExternalDamping,
            _ => 900f,
        };
        // Solve v0 from d = v0² / (2·damping) so the shove travels ~15m before the
        // target's own knockback friction eats it, regardless of that rate.
        float v0 = Mathf.Sqrt(2f * damping * Units.Px(15f));
        Vector2 impulse = dir * v0;

        if (body is CharacterController cc)
            cc.TakeHit(new HitInfo(cc.MaxHP * DamagePercentMaxHP, impulse, NoControlDuration));
        else if (body is BaseFoe en)
            en.TakeHit(new HitInfo(en.MaxHP * DamagePercentMaxHP, impulse, NoControlDuration), GlobalPosition);
    }

    public override void _Draw()
    {
        Vector2[] pts = TrianglePoints();
        DrawColoredPolygon(pts, new Color(0.2f, 0.5f, 0.85f, 0.6f));
        DrawPolyline(new[] { pts[0], pts[1], pts[2], pts[0] }, new Color(0.6f, 0.85f, 1f, 0.95f), 3f);
    }
}
