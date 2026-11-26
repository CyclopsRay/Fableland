using Godot;
using Fableland.Data;

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
///
/// The second half is FEEDBACK METADATA (<c>Docs/Feedback.gdd</c> §2): who swung, which attack
/// it was, how heavy it should feel. <b>Damage math never reads it</b> — it only decides how
/// the hit is shown and how long the two bodies hold still (hit-stop). An attack site names
/// itself in one call:
/// <code>
/// e.TakeHit(new HitInfo(dmg, knock).From(this, "ba", stage), origin);         // a body's own blow
/// e.TakeHit(new HitInfo(dmg, knock).FromRanged(owner, "s2.seed"), position);  // a projectile, a field, a tick
/// </code>
/// A hit that names nothing still works: it is felt by its damage, in the style "any".
/// </summary>
public struct HitInfo
{
    public float Damage;
    public Vector2 Knockback;
    public float Stun;

    // ── Feedback metadata — never read by damage math ───────────────────────────────
    /// <summary>The body that swung, if the attack names one. May be null (a hazard, a
    /// projectile whose owner has left the arena).</summary>
    public Node2D Attacker;
    /// <summary>Content id whose style this attack has ("pomegraknight"); null = "any".</summary>
    public string CueOwner;
    /// <summary>Which attack ("ba", "s2.seed"); null = unnamed.</summary>
    public string CueAction;
    /// <summary>Stage or kind within the attack (combo stage 1–3…); 0 if none.</summary>
    public int CueVariant;
    /// <summary>How heavy the hit should feel. <see cref="HitWeight.Auto"/> (the default)
    /// derives it from the damage dealt; a named weight holds the hit in that tier
    /// whatever buffs do to its damage.</summary>
    public HitWeight Weight;
    /// <summary>True for a projectile, a field or a tick: the victim is held, the owner
    /// never is (<c>Docs/Feedback.gdd</c> §4).</summary>
    public bool Ranged;

    public HitInfo(float damage, Vector2 knockback = default, float stun = -1f)
    {
        Damage = damage;
        Knockback = knockback;
        Stun = stun;
        Attacker = null;
        CueOwner = null;
        CueAction = null;
        CueVariant = 0;
        Weight = HitWeight.Auto;
        Ranged = false;
    }

    /// <summary>Resolve the gain-no window, applying the damage-linked default.</summary>
    public readonly float ResolveStun() => Stun < 0f ? Units.StunPerDamage * Damage : Stun;

    /// <summary>Name a blow a body lands itself (a slash, a palm, a foe's contact): the hit
    /// takes the attacker's style, and a melee helper may hold the attacker for it.</summary>
    public readonly HitInfo From(IFeedbackBody attacker, string action, int variant = 0,
                                 HitWeight weight = HitWeight.Auto)
    {
        HitInfo hit = this;
        hit.Attacker = LiveNode(attacker);
        hit.CueOwner = attacker?.FeedbackId;
        hit.CueAction = action;
        hit.CueVariant = variant;
        hit.Weight = weight;
        hit.Ranged = false;
        return hit;
    }

    /// <summary>Name a hit that travels on its own — a projectile, a field, a tick — while its
    /// owner is still at hand. The owner is credited and never held.</summary>
    public readonly HitInfo FromRanged(IFeedbackBody owner, string action, int variant = 0,
                                       HitWeight weight = HitWeight.Auto)
    {
        HitInfo hit = From(owner, action, variant, weight);
        hit.Ranged = true;
        return hit;
    }

    /// <summary>The same for a projectile that outlives its owner (a Tab switch frees the
    /// body; the seed flies on): it keeps the owner's id as a string from its own
    /// <c>Init</c>, and passes the body only while it is still valid.</summary>
    public readonly HitInfo FromRanged(string cueOwner, string action, Node2D attacker = null,
                                       int variant = 0, HitWeight weight = HitWeight.Auto)
    {
        HitInfo hit = this;
        hit.Attacker = attacker != null && GodotObject.IsInstanceValid(attacker) ? attacker : null;
        hit.CueOwner = cueOwner;
        hit.CueAction = action;
        hit.CueVariant = variant;
        hit.Weight = weight;
        hit.Ranged = true;
        return hit;
    }

    private static Node2D LiveNode(IFeedbackBody body) =>
        body is Node2D node && GodotObject.IsInstanceValid(node) ? node : null;
}
