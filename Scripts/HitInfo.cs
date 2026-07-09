using Godot;

/// <summary>
/// A single hit delivered by a BA/skill/hazard. Each skill defines its own
/// knockback and "gain-no" (hitstun) window, so damage, shove, and freeze are
/// authored together rather than hardcoded on the receiver.
///
/// - <see cref="Knockback"/> is a delta-v impulse (px/s) added to the receiver's
///   external velocity (which then decays). (Force-over-time effects are modelled
///   as per-tick impulses — see the tornado.)
/// - <see cref="Stun"/> is the gain-no window in seconds. Negative means "use the
///   default", which is <c>Units.StunPerDamage · Damage</c> (0.005·dmg). During it the receiver
///   cannot act and its animation is frozen; knockback/gravity still move it.
/// </summary>
public struct HitInfo
{
    public float Damage;
    public Vector2 Knockback;
    public float Stun;

    public HitInfo(float damage, Vector2 knockback = default, float stun = -1f)
    {
        Damage = damage;
        Knockback = knockback;
        Stun = stun;
    }

    /// <summary>Resolve the gain-no window, applying the damage-linked default.</summary>
    public readonly float ResolveStun() => Stun < 0f ? Units.StunPerDamage * Damage : Stun;
}
