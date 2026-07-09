using System;
using Godot;

namespace Fableland.Missions;

/// <summary>
/// The Condensed Wonder Core (NODES §4.4): a stationary entity with its own HP pool that the
/// player must defend. Foes chase the player (Q10 default), but any foe that ends up within
/// contact range of the core damages it on the core's own contact cooldown — so ignoring the
/// fight around the core still loses it. Not healable (S8). HP 0 ⇒ mission fails immediately.
/// </summary>
public partial class ProtectCore : Node2D
{
    /// <summary>Fired on every damage tick (cur, max) — HUD bar consumer (via the mission).</summary>
    public event Action<float, float> HpChanged;

    public float MaxHp { get; private set; } = 150f;
    public float CurrentHp { get; private set; } = 150f;
    public bool IsDestroyed { get; private set; }

    [Export] public float ContactRange = 64f;
    private float _damagePerHit = 18f;
    private const float HitCooldown = 0.9f;
    private float _cd;

    /// <summary>Set HP pool + the per-hit damage (foe-level scaled). Call after AddChild.</summary>
    public void Configure(float maxHp, float damagePerHit)
    {
        MaxHp = maxHp;
        CurrentHp = maxHp;
        _damagePerHit = damagePerHit;
        HpChanged?.Invoke(CurrentHp, MaxHp);
    }

    public override void _Process(double delta)
    {
        if (IsDestroyed) return;
        float dt = (float)delta;
        if (_cd > 0f) { _cd -= dt; return; }

        foreach (Node n in GetTree().GetNodesInGroup("foe"))
        {
            if (n is BaseFoe f && IsInstanceValid(f) &&
                (f.GlobalPosition - GlobalPosition).Length() < ContactRange)
            {
                _cd = HitCooldown;
                CurrentHp = Mathf.Max(0f, CurrentHp - _damagePerHit);
                HpChanged?.Invoke(CurrentHp, MaxHp);
                DamageNumberManager.Instance?.Pop(GlobalPosition + new Vector2(0f, -40f), _damagePerHit, false);
                if (CurrentHp <= 0f) IsDestroyed = true;
                return;
            }
        }
    }
}
