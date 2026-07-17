using System;
using System.Collections.Generic;
using Godot;
using Fableland.Run;

namespace Fableland.Items;

/// <summary>Combat-local interpreter for one protagonist's held item. RunState owns durable
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
    };

    public CharacterController Holder { get; }
    public ProtagonistState State { get; }
    public ItemDef Definition { get; }
    public string SourceKey { get; }
    /// <summary>Set after a single-use conversion. The arena rebinds immediately so the new
    /// item's passive is active on the same body without waiting for a scene transition.</summary>
    public bool RebindRequested { get; private set; }

    private readonly ICombatItemBehavior _behavior;

    public ItemRuntime(CharacterController holder, ProtagonistState state)
    {
        Holder = holder;
        State = state;
        if (holder == null || state == null || !ItemCatalog.TryGet(state.HeldItemDefId, out ItemDef definition)) return;

        Definition = definition;
        string id = string.IsNullOrWhiteSpace(state.HeldItemInstanceId) ? "ephemeral" : state.HeldItemInstanceId;
        SourceKey = $"item:{definition.Id}#{id}";
        if (!string.IsNullOrEmpty(definition.CombatBehaviorId)
            && Behaviors.TryGetValue(definition.CombatBehaviorId, out Func<ICombatItemBehavior> factory))
        {
            _behavior = factory();
            _behavior.OnEquip(this);
        }
    }

    public bool IsUsable => Definition != null && _behavior != null;

    public void Tick(float dt)
    {
        if (State == null || Definition == null) return;
        if (State.HeldItemSecondCooldownRemaining > 0f)
            State.HeldItemSecondCooldownRemaining = Mathf.Max(0f, State.HeldItemSecondCooldownRemaining - dt);
        _behavior?.Tick(this, dt);
    }

    public bool TryUse(out string reason)
    {
        reason = null;
        if (Definition == null || _behavior == null) { reason = "This held item has no combat skill."; return false; }
        if (State.HeldItemSecondCooldownRemaining > 0.05f)
        {
            reason = $"{Definition.DisplayName} is ready in {Mathf.CeilToInt(State.HeldItemSecondCooldownRemaining)} s.";
            return false;
        }
        if (!_behavior.TryUse(this, out reason)) return false;
        if (!RebindRequested)
            State.HeldItemSecondCooldownRemaining = Definition.SecondCooldownSeconds;
        return true;
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
    private Vector2 _anchor;
    private bool _dragging;

    public void OnEquip(ItemRuntime runtime) { }

    public void Tick(ItemRuntime runtime, float dt)
    {
        bool airborne = !runtime.Holder.IsOnFloor();
        if (airborne) runtime.Holder.SetDefenseSource(runtime.SourceKey, 30f);
        else runtime.Holder.ClearDefenseSource(runtime.SourceKey);

        if (!_dragging) return;
        Vector2 delta = _anchor - runtime.Holder.GlobalPosition;
        if (delta.LengthSquared() <= Units.Px(0.35f) * Units.Px(0.35f))
        {
            _dragging = false;
            runtime.Holder.ClearContinuousExternalVelocity(runtime.SourceKey + ":rope");
            return;
        }
        float speed = Mathf.Min(Units.Px(42f), delta.Length() * 9f);
        runtime.Holder.SetContinuousExternalVelocity(runtime.SourceKey + ":rope", delta.Normalized() * speed);
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

    private void OnMeleeDamageDealt(BaseFoe target, float dealt)
    {
        if (target == null || dealt <= 0f) return;
        target.TakeHit(new HitInfo(dealt * 0.5f, Vector2.Zero, 0f), _holder?.GlobalPosition ?? target.GlobalPosition);
    }

    public void Tick(ItemRuntime runtime, float dt) { }

    public bool TryUse(ItemRuntime runtime, out string reason)
    {
        float range = Units.Px(4f);
        foreach (Node node in runtime.Holder.GetTree().GetNodesInGroup("enemy"))
        {
            if (node is not BaseFoe foe) continue;
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
        reason = "Pome's Seed has no combat skill.";
        return false;
    }
}
