using System.Collections.Generic;
using Godot;

/// <summary>
/// Which side of a fight a body is on. A protagonist is on the <see cref="Player"/> side
/// when the player wears it and on the <see cref="Foe"/> side when it is the BOSS of a
/// realm's 4-1 (FOES.gdd §8): same kit, other side.
/// </summary>
public enum CombatSide
{
    Player,
    Foe,
}

/// <summary>
/// Anything an attack can land on. Crabs, seagulls and mission objectives
/// (<see cref="BaseFoe"/>) are targets on the Foe side; a <see cref="CharacterController"/>
/// is a target on whichever side it fights for. Attack code asks
/// <see cref="CombatTargets.HostileTo"/> for its targets and talks to this interface, so a
/// kit written for the player works unchanged when a boss turns it against the player.
///
/// The members are exactly what the kits already used on <see cref="BaseFoe"/>; adding a
/// reaction (a new status, a new shove) means adding it here once, not at every attack site.
/// </summary>
public interface ICombatTarget
{
    CombatSide Side { get; }
    Vector2 GlobalPosition { get; }

    /// <summary>Radius of the circle hit tests treat this target as (KNOWLEDGE v0.1.5).</summary>
    float HitRadius { get; }

    float MaxHP { get; }
    float CurrentHp { get; }
    bool Invincible { get; }

    /// <summary>Stable id for "has this wave already hit that target" registries.</summary>
    ulong TargetId { get; }

    /// <summary>Applies an authored hit (T10 §4 pipeline) and returns the damage it dealt
    /// after mitigation. <paramref name="sourcePos"/> feeds directional passives.</summary>
    float TakeHit(HitInfo hit, Vector2 sourcePos);

    void SetBurning(float duration);
    void ApplyTrapped(float duration);
    void AddImpulse(Vector2 impulse);

    /// <summary>Removes the target without another damage event (authored executes).</summary>
    void Execute();

    void SetContinuousExternalVelocity(string source, Vector2 velocity);
    void ClearContinuousExternalVelocity(string source);

    // ── Crowd control (PumpKing's Black Prison — Docs/PumpKing.gdd) ─────────────────────
    // What a skill may lay on a body of either side. Each is held by a named source (golden
    // rule 7), so a skill that ends clears only its own: a scale of 1, `false` or a cleared
    // limit releases that source and leaves every other. A target that cannot move, jump,
    // use a skill or look (a mirror image) takes them as nothing.

    /// <summary>Share of its move speed this body keeps while <paramref name="source"/> holds it
    /// (sources multiply; 1 clears). Walking, running, swimming and flying, not a skill's own
    /// fixed speed (a dash).</summary>
    void SetMoveSpeedScale(string source, float scale);

    /// <summary>Share of its jump's HEIGHT this body keeps (sources multiply; 1 clears); the
    /// launch speed scales by the root, so 0.5 is a jump half as high.</summary>
    void SetJumpHeightScale(string source, float scale);

    /// <summary>Silenced while any source holds it: no skill starts. The basic attack still goes
    /// (a fighter's BA, a foe's body blow and the seagull's poop), and a skill already under way
    /// runs its course.</summary>
    void SetSilenced(string source, bool on);

    /// <summary>Caps how far this body sees, in metres from its centre (0 = blind); the nearest
    /// cap any source holds wins. A foe or a boss brain cannot find the player past it.</summary>
    void SetSightLimit(string source, float metres);
    void ClearSightLimit(string source);
}

/// <summary>
/// "Who may I hit?" — the one answer every attack uses. Group membership stays the
/// registry (KNOWLEDGE v0.5.0: groups are contracts): <c>"enemy"</c> holds everything the
/// player's side may hit, <c>"player"</c> holds the body the player wears.
/// </summary>
public static class CombatTargets
{
    /// <summary>The body the player wears. Foes and a boss's attacks look here.</summary>
    public const string PlayerGroup = "player";

    /// <summary>Everything the player's attacks iterate: foes, objectives, a boss.</summary>
    public const string EnemyGroup = "enemy";

    /// <summary>Every live target the given side may hit. Safe to hit while iterating:
    /// the group is snapshotted, exactly as the kits' own loops always did.</summary>
    public static IEnumerable<ICombatTarget> HostileTo(SceneTree tree, CombatSide attacker)
    {
        if (tree == null) yield break;
        string group = attacker == CombatSide.Player ? EnemyGroup : PlayerGroup;
        foreach (Node node in tree.GetNodesInGroup(group))
        {
            if (node is ICombatTarget target && target.Side != attacker && GodotObject.IsInstanceValid(node))
                yield return target;
        }
    }

    /// <summary>Is this collider something the given side may hit? For projectiles that
    /// find their targets by touching them.</summary>
    public static bool TryGetHostile(GodotObject body, CombatSide attacker, out ICombatTarget target)
    {
        target = body as ICombatTarget;
        if (target != null && target.Side != attacker) return true;
        target = null;
        return false;
    }

    /// <summary>Points a projectile's collision mask at the bodies its side may hit: the
    /// Foes layer for the player's side, the Player layer for a boss's. Terrain bits are
    /// left as the scene authored them.</summary>
    public static void AimMask(CollisionObject2D projectile, CombatSide attacker)
    {
        if (projectile == null) return;
        projectile.SetCollisionMaskValue(Units.LayerFoes, attacker == CombatSide.Player);
        projectile.SetCollisionMaskValue(Units.LayerPlayer, attacker == CombatSide.Foe);
    }

    /// <summary>A target reference kept across frames may have been freed since.</summary>
    public static bool IsAlive(ICombatTarget target) =>
        target is GodotObject node && GodotObject.IsInstanceValid(node);
}
