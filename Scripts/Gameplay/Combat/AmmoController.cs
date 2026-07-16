using System;
using System.Collections.Generic;
using Fableland.Data;
using Fableland.Run;

/// <summary>
/// One owner for a protagonist's basic-attack magazine, ready interval, and single
/// restartable reload timer. Characters decide when to request a reload and when to
/// begin their attack interval; this class owns all resulting mutable state.
/// </summary>
public sealed class AmmoController
{
    private readonly Dictionary<string, float> _attackIntervalMultipliers = new();

    public CharacterAmmoDef Definition { get; }
    public int Current { get; private set; }
    public float AttackCooldownRemaining { get; private set; }
    public bool ReloadActive { get; private set; }
    public float ReloadRemaining { get; private set; }

    public int Capacity => Definition.Capacity;
    public bool CanUse => Current > 0 && AttackCooldownRemaining <= 0f;

    public event Action<int> ReloadCompleted;
    public event Action Changed;

    public AmmoController(CharacterAmmoDef definition)
    {
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        ResetForRespawn();
    }

    public bool TryConsume(int amount = 1)
    {
        if (amount <= 0 || !CanUse || Current < amount) return false;
        Current -= amount;
        Changed?.Invoke();
        return true;
    }

    /// <summary>Start the BA-ready interval at the character-defined moment. The
    /// effective value is modified by source-keyed attack-speed multipliers.</summary>
    public void StartAttackInterval()
    {
        AttackCooldownRemaining = EffectiveAttackInterval();
        Changed?.Invoke();
    }

    /// <summary>Arm or restart the one reload timer. Requests never queue reloads.</summary>
    public void RequestReload()
    {
        if (Current >= Capacity) return;
        ReloadActive = true;
        ReloadRemaining = Definition.ReloadDelaySec;
        Changed?.Invoke();
    }

    public void SetAttackIntervalMultiplier(string source, float multiplier)
    {
        if (string.IsNullOrWhiteSpace(source)) return;
        _attackIntervalMultipliers[source] = Math.Max(0.01f, multiplier);
    }

    public void ClearAttackIntervalMultiplier(string source)
    {
        if (!string.IsNullOrWhiteSpace(source)) _attackIntervalMultipliers.Remove(source);
    }

    public void Tick(float dt)
    {
        if (dt <= 0f) return;
        bool changed = false;
        if (AttackCooldownRemaining > 0f)
        {
            float next = Math.Max(0f, AttackCooldownRemaining - dt);
            changed |= next != AttackCooldownRemaining;
            AttackCooldownRemaining = next;
        }

        if (ReloadActive)
        {
            ReloadRemaining -= dt;
            changed = true;
            while (ReloadActive && ReloadRemaining <= 0f)
            {
                int before = Current;
                Current = Math.Min(Capacity, Current + Definition.ReloadSize);
                int restored = Current - before;
                if (restored > 0) ReloadCompleted?.Invoke(restored);

                if (Definition.RepeatReloadWhileMissing && Current < Capacity)
                    ReloadRemaining += Definition.ReloadDelaySec;
                else
                {
                    ReloadActive = false;
                    ReloadRemaining = 0f;
                }
            }
        }

        if (changed) Changed?.Invoke();
    }

    public void ResetForRespawn()
    {
        AttackCooldownRemaining = 0f;
        Current = Definition.RespawnMode == AmmoRespawnMode.Full ? Capacity : 0;
        ReloadActive = Current < Capacity && Definition.RepeatReloadWhileMissing;
        ReloadRemaining = ReloadActive ? Definition.ReloadDelaySec : 0f;
        Changed?.Invoke();
    }

    public void Save(ProtagonistState state)
    {
        if (state == null) return;
        state.AmmoInitialized = true;
        state.AmmoCurrent = Current;
        state.AmmoAttackCooldownRemaining = AttackCooldownRemaining;
        state.AmmoReloadActive = ReloadActive;
        state.AmmoReloadRemaining = ReloadRemaining;
    }

    public void Load(ProtagonistState state)
    {
        if (state == null || !state.AmmoInitialized) return;
        Current = Math.Clamp(state.AmmoCurrent, 0, Capacity);
        AttackCooldownRemaining = Math.Max(0f, state.AmmoAttackCooldownRemaining);
        ReloadActive = state.AmmoReloadActive && Current < Capacity;
        ReloadRemaining = ReloadActive ? Math.Max(0f, state.AmmoReloadRemaining) : 0f;
        Changed?.Invoke();
    }

    /// <summary>Advance a benched protagonist's persisted ammo state without creating
    /// a scene node. Unknown/uninitialized protagonists stay untouched.</summary>
    public static void TickPersisted(ProtagonistState state, float dt)
    {
        if (state == null || !state.AmmoInitialized || dt <= 0f) return;
        if (!CharacterTable.TryGetAmmo(state.Id, out CharacterAmmoDef def)) return;

        if (state.AmmoAttackCooldownRemaining > 0f)
            state.AmmoAttackCooldownRemaining = Math.Max(0f, state.AmmoAttackCooldownRemaining - dt);
        if (!state.AmmoReloadActive || state.AmmoCurrent >= def.Capacity) return;

        state.AmmoReloadRemaining -= dt;
        while (state.AmmoReloadActive && state.AmmoReloadRemaining <= 0f)
        {
            state.AmmoCurrent = Math.Min(def.Capacity, state.AmmoCurrent + def.ReloadSize);
            if (def.RepeatReloadWhileMissing && state.AmmoCurrent < def.Capacity)
                state.AmmoReloadRemaining += def.ReloadDelaySec;
            else
            {
                state.AmmoReloadActive = false;
                state.AmmoReloadRemaining = 0f;
            }
        }
    }

    private float EffectiveAttackInterval()
    {
        float multiplier = 1f;
        foreach (float value in _attackIntervalMultipliers.Values) multiplier *= value;
        return Math.Max(0f, Definition.BaseAttackIntervalSec * multiplier);
    }
}
