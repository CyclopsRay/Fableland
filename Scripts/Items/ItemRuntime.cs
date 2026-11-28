using System;
using System.Collections.Generic;
using Godot;
using Fableland.Run;

namespace Fableland.Items;

/// <summary>Combat-local interpreter for one protagonist's held item — or, passive only, for a
/// <c>Possession</c> item the team owns elsewhere (ITEMS.gdd §4.4). RunState owns durable
/// location/cooldown state; this object owns only live subscriptions and temporary modifiers and
/// is disposed before a body switch or scene exit.</summary>
public sealed class ItemRuntime : IDisposable
{
    private static readonly Dictionary<string, Func<ICombatItemBehavior>> Behaviors = new()
    {
        [ItemBehaviorId.FanChensHeart] = () => new FanChensHeartBehavior(),
        [ItemBehaviorId.YukaisRope] = () => new YukaisRopeBehavior(),
        [ItemBehaviorId.ForgottenKashaya] = () => new ForgottenKashayaBehavior(),
        [ItemBehaviorId.PomesBravery] = () => new PomesBraveryBehavior(),
        [ItemBehaviorId.PomesSeed] = () => new PomesSeedBehavior(),
        [ItemBehaviorId.Dd] = () => new DdBehavior(),
        [ItemBehaviorId.WeirdMushroom] = () => new WeirdMushroomBehavior(),
        [ItemBehaviorId.PixolotlsFeather] = () => new PixolotlsFeatherBehavior(),
        [ItemBehaviorId.PixolotlsWish] = () => new PixolotlsWishBehavior(),
        [ItemBehaviorId.Shieldon] = () => new ShieldonBehavior(),
        [ItemBehaviorId.TheVoid] = () => new TheVoidBehavior(),
    };

    public CharacterController Holder { get; }
    /// <summary>The holder's run state; null for a passive-only (Possession) runtime.</summary>
    public ProtagonistState State { get; }
    public ItemDef Definition { get; private set; }
    public string InstanceId { get; private set; } = "";
    public string SourceKey { get; private set; }
    /// <summary>A Possession item working from elsewhere: its passive only, never its skill.</summary>
    public bool PassiveOnly { get; }
    /// <summary>Set after a single-use conversion (or an item used up). The arena rebinds
    /// immediately so the new item's passive is active on the same body without waiting for a
    /// scene transition.</summary>
    public bool RebindRequested { get; private set; }
    /// <summary>What the last use asks of the run (the arena carries it to RunState).</summary>
    public ItemRunVerb PendingVerb { get; private set; }

    private ICombatItemBehavior _behavior;

    public ItemRuntime(CharacterController holder, ProtagonistState state)
    {
        Holder = holder;
        State = state;
        if (holder == null || state == null || !ItemCatalog.TryGet(state.HeldItemDefId, out ItemDef definition)) return;
        Bind(definition, state.HeldItemInstanceId);
    }

    private ItemRuntime(CharacterController holder, string defId, string instanceId)
    {
        Holder = holder;
        PassiveOnly = true;
        if (holder == null || !ItemCatalog.TryGet(defId, out ItemDef definition)) return;
        Bind(definition, instanceId);
    }

    /// <summary>A Possession item's passive on the body in play, from wherever the item lies
    /// (ITEMS.gdd §4.4: party-wide, applied once — never with the holder's own runtime of the
    /// same instance).</summary>
    public static ItemRuntime ForPossession(CharacterController holder, string defId, string instanceId) =>
        new(holder, defId, instanceId);

    private void Bind(ItemDef definition, string instanceId)
    {
        Definition = definition;
        InstanceId = instanceId ?? "";
        string id = string.IsNullOrWhiteSpace(instanceId) ? "ephemeral" : instanceId;
        SourceKey = $"item:{definition.Id}#{id}";
        if (!string.IsNullOrEmpty(definition.CombatBehaviorId)
            && Behaviors.TryGetValue(definition.CombatBehaviorId, out Func<ICombatItemBehavior> factory))
        {
            _behavior = factory();
            _behavior.OnEquip(this);
        }
    }

    public bool IsUsable => Definition != null && _behavior != null && !PassiveOnly;

    /// <summary>True while Yukai's Rope is pulling its holder (checks read it).</summary>
    public bool RopeDragging => _behavior is YukaisRopeBehavior { Dragging: true };

    public void Tick(float dt)
    {
        if (Definition == null) return;
        if (State != null && State.HeldItemSecondCooldownRemaining > 0f)
            State.HeldItemSecondCooldownRemaining = Mathf.Max(0f, State.HeldItemSecondCooldownRemaining - dt);
        _behavior?.Tick(this, dt);
    }

    /// <summary>A wonder core was picked up in this fight (DD's passive heals on it).</summary>
    public void NotifyCorePickedUp()
    {
        if (_behavior is ICoreListener listener) listener.OnCorePickedUp(this);
    }

    public bool TryUse(out string reason)
    {
        reason = null;
        if (Definition == null || _behavior == null || PassiveOnly || State == null) { reason = "This held item has no combat skill."; return false; }
        // A silenced body uses no item (the owner, v0.12.13): the press is gone, nothing is
        // spent. Passives keep working — a silence stops what you do, not what you carry.
        if (Holder != null && GodotObject.IsInstanceValid(Holder) && Holder.Silenced)
        {
            reason = $"Silenced: {Definition.DisplayName} can't be used until it lifts.";
            return false;
        }
        if (Definition.DayCooldownDays > 0 && State.HeldItemDayCooldownRemaining > 0)
        {
            int days = State.HeldItemDayCooldownRemaining;
            reason = $"{Definition.DisplayName} is ready in {days} day{(days == 1 ? "" : "s")}.";
            return false;
        }
        if (State.HeldItemSecondCooldownRemaining > 0.05f)
        {
            reason = $"{Definition.DisplayName} is ready in {Mathf.CeilToInt(State.HeldItemSecondCooldownRemaining)} s.";
            return false;
        }
        if (!_behavior.TryUse(this, out reason)) return false;
        if (!RebindRequested)
        {
            State.HeldItemSecondCooldownRemaining = Definition.SecondCooldownSeconds;
            // A day-cooldown skill used in a fight (DD, THE VOID) waits out its days (ITEMS.gdd §3.1).
            if (Definition.DayCooldownDays > 0) State.HeldItemDayCooldownRemaining = Definition.DayCooldownDays;
        }
        return true;
    }

    /// <summary>The held item is used up by something the run must do (eaten): the arena
    /// performs <paramref name="verb"/> on RunState, then rebinds.</summary>
    internal void RequestRunVerb(ItemRunVerb verb)
    {
        PendingVerb = verb;
        RebindRequested = true;
    }

    /// <summary>Replace this held instance's definition in place. Its identity stays stable,
    /// preserving inventory/save ownership while a single-use item becomes its authored target.</summary>
    public bool ConvertHeldItem(out string reason)
    {
        reason = null;
        if (Definition == null || string.IsNullOrWhiteSpace(Definition.ConversionTargetDefId)
            || !ItemCatalog.TryGet(Definition.ConversionTargetDefId, out _))
        {
            reason = "This item has no valid conversion target.";
            return false;
        }
        State.HeldItemDefId = Definition.ConversionTargetDefId;
        State.HeldItemDayCooldownRemaining = 0;
        State.HeldItemSecondCooldownRemaining = 0f;
        RebindRequested = true;
        return true;
    }

    public void Dispose()
    {
        _behavior?.OnUnequip(this);
    }
}

internal interface ICombatItemBehavior
{
    void OnEquip(ItemRuntime runtime);
    void Tick(ItemRuntime runtime, float dt);
    bool TryUse(ItemRuntime runtime, out string reason);
    void OnUnequip(ItemRuntime runtime);
}

/// <summary>A behaviour that answers a wonder-core pickup (DD's passive).</summary>
internal interface ICoreListener
{
    void OnCorePickedUp(ItemRuntime runtime);
}

internal sealed class FanChensHeartBehavior : ICombatItemBehavior
{
    private CharacterController _holder;

    public void OnEquip(ItemRuntime runtime)
    {
        _holder = runtime.Holder;
        runtime.Holder.SetFrozenDurationExtension(runtime.SourceKey, 0.2f);
        runtime.Holder.DirectDamageTaken += OnDirectDamageTaken;
    }

    private void OnDirectDamageTaken(float dealt)
    {
        if (_holder != null && _holder.HasFrozenStatus) _holder.Heal(dealt * 0.30f);
    }

    public void Tick(ItemRuntime runtime, float dt) { }

    public bool TryUse(ItemRuntime runtime, out string reason)
    {
        runtime.Holder.AddFrozenStack(60f);
        reason = null;
        return true;
    }

    public void OnUnequip(ItemRuntime runtime)
    {
        runtime.Holder.DirectDamageTaken -= OnDirectDamageTaken;
        runtime.Holder.ClearFrozenDurationExtension(runtime.SourceKey);
        _holder = null;
    }
}

internal sealed class YukaisRopeBehavior : ICombatItemBehavior
{
    /// <summary>The longest a drag lasts, and how long it may stop closing in before it lets go.</summary>
    public const float MaxDragSeconds = 1.5f;
    public const float StallSeconds = 0.2f;

    private Vector2 _anchor;
    private bool _dragging;
    private float _dragTime, _stalled, _lastDistance;

    /// <summary>True while the rope is pulling (checks read it).</summary>
    public bool Dragging => _dragging;

    public void OnEquip(ItemRuntime runtime) { }

    public void Tick(ItemRuntime runtime, float dt)
    {
        bool airborne = !runtime.Holder.IsOnFloor();
        if (airborne) runtime.Holder.SetDefenseSource(runtime.SourceKey, 30f);
        else runtime.Holder.ClearDefenseSource(runtime.SourceKey);

        if (!_dragging) return;
        Vector2 delta = _anchor - runtime.Holder.GlobalPosition;
        float distance = delta.Length();
        // The anchor is a point on a surface and the body has a width, so its centre may never
        // reach it: let go when the body stops closing in (it has hit the wall), or at the time
        // limit — not only on arrival (audit v0.11.1 #12).
        _dragTime += dt;
        _stalled = _lastDistance - distance < Units.Px(0.05f) ? _stalled + dt : 0f;
        _lastDistance = distance;
        if (distance <= Units.Px(0.35f) || _stalled >= StallSeconds || _dragTime >= MaxDragSeconds)
        {
            Release(runtime);
            return;
        }
        float speed = Mathf.Min(Units.Px(42f), distance * 9f);
        runtime.Holder.SetContinuousExternalVelocity(runtime.SourceKey + ":rope", delta / Mathf.Max(0.001f, distance) * speed);
    }

    private void Release(ItemRuntime runtime)
    {
        _dragging = false;
        runtime.Holder.ClearContinuousExternalVelocity(runtime.SourceKey + ":rope");
    }

    public bool TryUse(ItemRuntime runtime, out string reason)
    {
        Vector2 from = runtime.Holder.GlobalPosition;
        Vector2 to = from + runtime.Holder.FacingDirection * Units.Px(20f);
        var query = PhysicsRayQueryParameters2D.Create(from, to);
        query.CollisionMask = (1u << (Units.LayerGround - 1)) | (1u << (Units.LayerPlatform - 1));
        var exclude = new Godot.Collections.Array<Rid>();
        exclude.Add(runtime.Holder.GetRid());
        query.Exclude = exclude;
        Godot.Collections.Dictionary hit = runtime.Holder.GetWorld2D().DirectSpaceState.IntersectRay(query);
        if (hit.Count == 0)
        {
            reason = "Yukai's Rope found no obstacle within 20 m.";
            return false;
        }
        _anchor = hit["position"].AsVector2();
        _dragging = true;
        _dragTime = 0f;
        _stalled = 0f;
        _lastDistance = (_anchor - from).Length();
        reason = null;
        return true;
    }

    public void OnUnequip(ItemRuntime runtime)
    {
        runtime.Holder.ClearDefenseSource(runtime.SourceKey);
        runtime.Holder.ClearContinuousExternalVelocity(runtime.SourceKey + ":rope");
    }
}

internal sealed class ForgottenKashayaBehavior : ICombatItemBehavior
{
    private CharacterController _holder;

    public void OnEquip(ItemRuntime runtime)
    {
        _holder = runtime.Holder;
        runtime.Holder.MeleeDamageDealt += OnMeleeDamageDealt;
    }

    private void OnMeleeDamageDealt(ICombatTarget target, float dealt)
    {
        if (target == null || dealt <= 0f) return;
        target.TakeHit(new HitInfo(dealt * 0.5f, Vector2.Zero, 0f), _holder?.GlobalPosition ?? target.GlobalPosition);
    }

    public void Tick(ItemRuntime runtime, float dt) { }

    public bool TryUse(ItemRuntime runtime, out string reason)
    {
        float range = Units.Px(4f);
        foreach (ICombatTarget foe in CombatTargets.HostileTo(runtime.Holder.GetTree(), runtime.Holder.Side))
        {
            Vector2 delta = foe.GlobalPosition - runtime.Holder.GlobalPosition;
            float distance = delta.Length();
            if (distance - foe.HitRadius > range) continue;
            Vector2 knock = (distance > 0.01f ? delta / distance : runtime.Holder.FacingDirection) * Units.Px(12f);
            foe.TakeHit(new HitInfo(10f, knock, 0.15f), runtime.Holder.GlobalPosition);
        }
        reason = null;
        return true;
    }

    public void OnUnequip(ItemRuntime runtime)
    {
        runtime.Holder.MeleeDamageDealt -= OnMeleeDamageDealt;
        _holder = null;
    }
}

/// <summary>Shared Pome lineage passive. The 5% healing observes all currently reported player
/// damage, while the source-keyed duration extension affects every future OnFire application.</summary>
internal abstract class PomesLineageBehavior : ICombatItemBehavior
{
    private CharacterController _holder;

    public virtual void OnEquip(ItemRuntime runtime)
    {
        _holder = runtime.Holder;
        _holder.SetFireDurationExtension(runtime.SourceKey, 0.2f);
        _holder.DamageDealt += OnDamageDealt;
    }

    private void OnDamageDealt(float dealt)
    {
        if (_holder != null && _holder.HasOnFireStatus) _holder.Heal(dealt * 0.05f);
    }

    public virtual void Tick(ItemRuntime runtime, float dt) { }
    public abstract bool TryUse(ItemRuntime runtime, out string reason);

    public virtual void OnUnequip(ItemRuntime runtime)
    {
        runtime.Holder.DamageDealt -= OnDamageDealt;
        runtime.Holder.ClearFireDurationExtension(runtime.SourceKey);
        _holder = null;
    }
}

internal sealed class PomesBraveryBehavior : PomesLineageBehavior
{
    public override bool TryUse(ItemRuntime runtime, out string reason)
    {
        runtime.Holder.AddFireStack(60f);
        return runtime.ConvertHeldItem(out reason);
    }
}

internal sealed class PomesSeedBehavior : PomesLineageBehavior
{
    public override bool TryUse(ItemRuntime runtime, out string reason)
    {
        reason = "Pome's Seed has no combat skill — plant it at a Transportation Hub.";
        return false;
    }
}

/// <summary>
/// DD (ITEMS.gdd §7.3). Passive: a wonder core picked up heals 2 % of max HP. Skill (2-day
/// cooldown): let DD go at the holder's feet, facing the holder's way — the creature itself
/// (<see cref="DdCreature"/>) crawls, devours and waits to be reclaimed; the arena sets a DD
/// still loose free when the player leaves the fight.
/// </summary>
internal sealed class DdBehavior : ICombatItemBehavior, ICoreListener
{
    public const float CoreHealShare = 0.02f;

    public void OnEquip(ItemRuntime runtime) { }
    public void Tick(ItemRuntime runtime, float dt) { }

    public bool TryUse(ItemRuntime runtime, out string reason)
    {
        if (DdCreature.IsLoose(runtime.InstanceId))
        {
            reason = "DD is still out — walk into it to reclaim it.";
            return false;
        }
        Node parent = runtime.Holder.GetParent();
        if (parent == null)
        {
            reason = "There is nowhere to let DD go.";
            return false;
        }
        float direction = runtime.Holder.FacingDirection.X < 0f ? -1f : 1f;
        DdCreature.Release(parent, runtime.Holder.GlobalPosition, direction, runtime.Holder.Side, runtime.InstanceId);
        reason = null;
        return true;
    }

    public void OnCorePickedUp(ItemRuntime runtime) => runtime.Holder.Heal(runtime.Holder.MaxHP * CoreHealShare);

    public void OnUnequip(ItemRuntime runtime) { }
}

/// <summary>A Weird Mushroom (ITEMS.gdd §7.13): eating it is the run's business (max HP for
/// good, tomorrow's consequence) — the behaviour only asks the arena to carry it there.</summary>
internal sealed class WeirdMushroomBehavior : ICombatItemBehavior
{
    public void OnEquip(ItemRuntime runtime) { }
    public void Tick(ItemRuntime runtime, float dt) { }

    public bool TryUse(ItemRuntime runtime, out string reason)
    {
        runtime.RequestRunVerb(ItemRunVerb.EatMushroom);
        reason = null;
        return true;
    }

    public void OnUnequip(ItemRuntime runtime) { }
}

/// <summary>Pixolotl's Feather and Wish share a passive: gravity −20 % (ITEMS.gdd §7.8–7.9).</summary>
internal abstract class PixolotlsLineageBehavior : ICombatItemBehavior
{
    public const float GravityShare = 0.8f;

    public void OnEquip(ItemRuntime runtime) => runtime.Holder.SetGravityScale(runtime.SourceKey, GravityShare);
    public void Tick(ItemRuntime runtime, float dt) { }
    public abstract bool TryUse(ItemRuntime runtime, out string reason);
    public void OnUnequip(ItemRuntime runtime) => runtime.Holder.SetGravityScale(runtime.SourceKey, 1f);
}

internal sealed class PixolotlsFeatherBehavior : PixolotlsLineageBehavior
{
    public override bool TryUse(ItemRuntime runtime, out string reason)
    {
        reason = "Pixolotl's Feather turns back the day from the map, not in a fight.";
        return false;
    }
}

internal sealed class PixolotlsWishBehavior : PixolotlsLineageBehavior
{
    public override bool TryUse(ItemRuntime runtime, out string reason)
    {
        reason = "Pixolotl's Wish has no combat skill — plant it at a Transportation Hub.";
        return false;
    }
}

/// <summary>THE VOID (ITEMS.gdd §7.2): its passive is the run's (the phantoms); its skill, held
/// and on a 2-day cooldown, darkens the fight — every foe loses its sight for 10 s
/// (<see cref="VoidBlindness"/>, which outlives a switch of the body that cast it).</summary>
internal sealed class TheVoidBehavior : ICombatItemBehavior
{
    public void OnEquip(ItemRuntime runtime) { }
    public void Tick(ItemRuntime runtime, float dt) { }

    public bool TryUse(ItemRuntime runtime, out string reason)
    {
        Node parent = runtime.Holder.GetParent();
        if (parent == null)
        {
            reason = "There is nothing here for THE VOID to darken.";
            return false;
        }
        VoidBlindness.Cast(parent, runtime.Holder.Side, runtime.SourceKey + ":blind", runtime.Holder);
        reason = null;
        return true;
    }

    public void OnUnequip(ItemRuntime runtime) { }
}

/// <summary>The Blessing of Shieldon (ITEMS.gdd §7.1): +10 % move speed; its enchant is spent
/// at a Transportation Hub.</summary>
internal sealed class ShieldonBehavior : ICombatItemBehavior
{
    public const float SpeedShare = 1.1f;

    public void OnEquip(ItemRuntime runtime) => runtime.Holder.SetMoveSpeedScale(runtime.SourceKey, SpeedShare);
    public void Tick(ItemRuntime runtime, float dt) { }

    public bool TryUse(ItemRuntime runtime, out string reason)
    {
        reason = "The Blessing of Shieldon is spent at a Transportation Hub, not in a fight.";
        return false;
    }

    public void OnUnequip(ItemRuntime runtime) => runtime.Holder.SetMoveSpeedScale(runtime.SourceKey, 1f);
}
