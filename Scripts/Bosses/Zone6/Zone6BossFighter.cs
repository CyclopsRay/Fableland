using Godot;
using Fableland.Data;

namespace Fableland.Bosses;

/// <summary>
/// The part of a zone-6 boss's own kit that every one of them shares (Docs/Zone6.gdd §4–§5,
/// FOES.gdd §8.7): a Foe-side fighter that is nobody's protagonist — it has no player kit to
/// inherit — driven by a rule-based brain through a virtual pad, sized by its own
/// <see cref="BossTable"/> row. No animation yet: one static sprite (drawn facing left; the
/// scene mirrors it so the body's facing flips it the usual way), the body's own hit flash.
/// A subclass gives its row, its buttons (<c>HandleBA</c> / <c>HandleSkill1/2</c>) and its
/// rules; this class does the rest.
/// </summary>
public abstract partial class Zone6BossFighter : CharacterController, IBossFighter
{
    public override CombatSide Side => CombatSide.Foe;

    /// <summary>The boss's row — its magazine is set from it before any fight.</summary>
    protected abstract BossDef Row { get; }

    protected BossBrain Brain;
    protected BossArena Arena;
    protected BossDef Def;
    protected readonly BossSenses Senses = new();

    protected override void InitCharacter()
    {
        MaxJumps = 1;
        ConfigureAmmo(Row.Ammo, Row.Id);
    }

    // ── IBossFighter ──────────────────────────────────────────────────────────

    public void BeginFight(BossDef def, int foeLevel, BossArena arena)
    {
        Def = def;
        BossSetup.ApplyRow(this, def, foeLevel);
        Arena = arena;
        Brain = BossBrain.Attach(this, def.DecisionSeconds);
        Brain.Sense = dt =>
        {
            Senses.Sample(this, dt);
            Sense(dt);
        };
        BuildRules(Brain.Rules, Brain.Pad);
    }

    public void StandDown()
    {
        Invincible = true;
        Brain?.Sleep();
        OnStandDown();
    }

    public string BrainState => Brain?.Describe() ?? "no brain";

    /// <summary>The perch the fight opened on — where it holds when nothing else calls.</summary>
    public Vector2 HomePoint => Arena != null ? Arena.Perches[Arena.HomeIndex] : GlobalPosition;

    // ── For the subclass ──────────────────────────────────────────────────────

    protected abstract void BuildRules(BossRuleSet rules, VirtualPad pad);

    /// <summary>Called every decision after the senses are sampled.</summary>
    protected virtual void Sense(float dt) { }

    /// <summary>The mission ended with the boss standing: end whatever it was doing.</summary>
    protected virtual void OnStandDown() { }

    /// <summary>Metres to the player (∞ with none).</summary>
    protected float TargetMetres => Senses.HasTarget ? Senses.Distance / Units.PixelsPerMeter : float.PositiveInfinity;

    /// <summary>The player's body, when there is one.</summary>
    protected CharacterController TargetBody =>
        GetTree().GetFirstNodeInGroup(CombatTargets.PlayerGroup) is CharacterController player
        && Fableland.Realms.Realm.SameAs(this, player) ? player : null;   // another realm is not here

    /// <summary>Turns toward the player through the pad (the body flips on its next tick).</summary>
    protected void FaceTarget(VirtualPad pad)
    {
        if (!Senses.HasTarget) return;
        pad.Facing = Senses.ToTarget.X >= 0f ? 1f : -1f;
        pad.Aim = Senses.TargetPosition;
    }

    /// <summary>Walks toward <paramref name="x"/>, stopping within <paramref name="deadZonePx"/>.</summary>
    protected void WalkToward(VirtualPad pad, float x, float deadZonePx)
    {
        float dx = x - GlobalPosition.X;
        pad.Hold(ControlAction.MoveLeft, dx < -deadZonePx);
        pad.Hold(ControlAction.MoveRight, dx > deadZonePx);
    }

    protected static void StopWalking(VirtualPad pad)
    {
        pad.Hold(ControlAction.MoveLeft, false);
        pad.Hold(ControlAction.MoveRight, false);
    }
}
