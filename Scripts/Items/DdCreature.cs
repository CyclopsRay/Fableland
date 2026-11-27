using System;
using System.Collections.Generic;
using Godot;

namespace Fableland.Items;

/// <summary>
/// DD, let go (ITEMS.gdd §7.3): a small bug that crouches on the ground and crawls along it at
/// <see cref="SpeedMps"/>, devouring every hostile target it touches for <see cref="Damage"/>
/// (each once per release), and turning around when it meets a wall. The body the player wears
/// reclaims it by walking into it — not before <see cref="ArmSeconds"/>, so it is not taken
/// back the instant it is let go. One still <see cref="Loose"/> when the player leaves the fight
/// is set free: the arena asks for the list and the run loses the item.
///
/// Built in code (no scene): a capsule that collides only with Ground and Platform, so nothing
/// stands on it and it pushes nothing. If it falls for <see cref="LostFallSeconds"/> (off the
/// map's edge) it is put back where it was let go, still loose.
///
/// TODO(art): DD's sprite — a crouching beetle-like creature — and its crawl and devour
/// animations; the placeholder is a drawn shell with two eyes.
/// </summary>
public partial class DdCreature : CharacterBody2D
{
    public const float SpeedMps = 4f;
    public const float Damage = 200f;
    public const float ArmSeconds = 0.6f;
    public const float ReclaimRadiusM = 1f;
    public const float HalfWidthM = 0.4f;
    public const float HalfHeightM = 0.25f;
    public const float LostFallSeconds = 3f;

    /// <summary>Every DD loose in the arena now (added on entering the tree, removed on leaving).</summary>
    public static readonly List<DdCreature> Loose = new();

    /// <summary>The player walked into a loose DD (the arena says so).</summary>
    public static event Action<DdCreature> Reclaimed;

    public string InstanceId { get; private set; } = "";
    public CombatSide Side { get; private set; } = CombatSide.Player;
    /// <summary>−1 crawling left, +1 right.</summary>
    public float Direction { get; private set; } = 1f;
    /// <summary>Targets devoured this release (checks read it).</summary>
    public int Devoured => _devoured.Count;

    private readonly HashSet<ulong> _devoured = new();
    private Vector2 _releasedAt;
    private float _age, _falling;
    private bool _reclaimed;

    /// <summary>Let a DD go at <paramref name="at"/> under <paramref name="parent"/>.</summary>
    public static DdCreature Release(Node parent, Vector2 at, float direction, CombatSide side, string instanceId)
    {
        var dd = new DdCreature
        {
            Name = "DD",
            InstanceId = instanceId ?? "",
            Side = side,
            Direction = direction < 0f ? -1f : 1f,
        };
        parent.AddChild(dd);
        dd.GlobalPosition = at;
        dd._releasedAt = at;
        return dd;
    }

    /// <summary>Is the DD of this item instance out in the arena?</summary>
    public static bool IsLoose(string instanceId)
    {
        foreach (DdCreature dd in Loose)
            if (IsInstanceValid(dd) && !dd._reclaimed && dd.InstanceId == instanceId) return true;
        return false;
    }

    public override void _Ready()
    {
        CollisionLayer = 0;
        CollisionMask = (1u << (Units.LayerGround - 1)) | (1u << (Units.LayerPlatform - 1));
        FloorSnapLength = Units.Px(0.3f);
        var shape = new CollisionShape2D
        {
            Shape = new RectangleShape2D { Size = new Vector2(Units.Px(HalfWidthM * 2f), Units.Px(HalfHeightM * 2f)) },
        };
        AddChild(shape);
        Loose.Add(this);
        QueueRedraw();
    }

    public override void _ExitTree() => Loose.Remove(this);

    public override void _PhysicsProcess(double delta)
    {
        if (_reclaimed) return;
        float dt = (float)delta;
        _age += dt;

        Vector2 velocity = Velocity;
        velocity.X = Direction * Units.Px(SpeedMps);
        velocity.Y = IsOnFloor() ? 0f : velocity.Y + Units.Gravity * dt;
        Velocity = velocity;
        MoveAndSlide();
        if (IsOnWall())
        {
            Direction = -Direction;
            QueueRedraw();
        }

        _falling = IsOnFloor() ? 0f : _falling + dt;
        if (_falling >= LostFallSeconds)
        {
            GlobalPosition = _releasedAt;
            Velocity = Vector2.Zero;
            _falling = 0f;
        }

        Devour();
        TryBeReclaimed();
    }

    private void Devour()
    {
        float reach = Units.Px(HalfWidthM);
        foreach (ICombatTarget target in CombatTargets.HostileTo(GetTree(), Side))
        {
            if (target.Invincible || _devoured.Contains(target.TargetId)) continue;
            if (target.GlobalPosition.DistanceTo(GlobalPosition) > target.HitRadius + reach) continue;
            _devoured.Add(target.TargetId);
            var knock = new Vector2(Direction * Units.Px(3f), -Units.Px(4f));
            target.TakeHit(new HitInfo(Damage, knock, 0.25f) { Ranged = true }, GlobalPosition);
        }
    }

    private void TryBeReclaimed()
    {
        if (_age < ArmSeconds || Side != CombatSide.Player) return;
        if (GetTree().GetFirstNodeInGroup(CombatTargets.PlayerGroup) is not Node2D player) return;
        if (player.GlobalPosition.DistanceTo(GlobalPosition) > Units.Px(ReclaimRadiusM)) return;
        _reclaimed = true;
        Reclaimed?.Invoke(this);
        QueueFree();
    }

    public override void _Draw()
    {
        // TODO(art): placeholder shell — replace with DD's sprite and crawl animation.
        float w = Units.Px(HalfWidthM), h = Units.Px(HalfHeightM);
        var shell = new Vector2[12];
        for (int i = 0; i < shell.Length; i++)
        {
            float a = Mathf.Pi + Mathf.Pi * i / (shell.Length - 1);   // the upper half of an ellipse
            shell[i] = new Vector2(Mathf.Cos(a) * w, Mathf.Sin(a) * h * 1.6f + h);
        }
        DrawColoredPolygon(shell, new Color(0.16f, 0.12f, 0.22f));
        DrawLine(new Vector2(-w, h), new Vector2(w, h), new Color(0.45f, 0.35f, 0.55f), 2f);
        float eyeX = Direction * w * 0.55f;
        DrawCircle(new Vector2(eyeX, h * 0.1f), 2.5f, Colors.White);
        DrawCircle(new Vector2(eyeX + Direction * 5f, h * 0.1f), 2.5f, Colors.White);
    }
}
