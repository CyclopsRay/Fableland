using Godot;
using System;
using System.Collections.Generic;
using Fableland.Data;

// The small parts both kinds of body — CharacterController and BaseFoe — are built from, so
// the two hit intakes cannot drift apart: the hit-stop clock, the merged "hurt.tick" cue, the
// status loops and the hit flash (Docs/Feedback.gdd §2, §4, §5).

/// <summary>
/// One body's hit-stop: how long it still holds, and the rules that keep hit-stop from
/// turning into a stun lock (<c>Docs/Feedback.gdd</c> §4). Pure arithmetic on world seconds —
/// a body ticks it once per physics tick, before it moves.
///
///   • Stops take the max, never add: a request while held only ever lengthens the hold.
///   • A body that just left a stop cannot be stopped again for
///     <see cref="FeedbackTable.StopRearmSeconds"/>.
///   • Every granted second is taken from a budget that refills slowly, so a stream of hits
///     holds a body a bounded share of the time (<see cref="FeedbackTable.StopBudgetSeconds"/>).
///
/// Gameplay state, not presentation: a held body does not move, so nothing outside the body
/// itself (and the melee helper acting for it) may request a stop.
/// </summary>
public struct HitStopClock
{
    /// <summary>A grant shorter than this would not hold a single physics tick's worth.</summary>
    private const float SmallestGrant = 0.008f;
    private const float Epsilon = 0.0005f;

    private float _remaining;
    private float _rearm;
    private float _spent;

    /// <summary>True while the body must hold still.</summary>
    public readonly bool Active => _remaining > 0f;
    /// <summary>Seconds of hold left.</summary>
    public readonly float Remaining => _remaining;
    /// <summary>Whether the last <see cref="Tick"/> held the body.</summary>
    public bool Held { get; private set; }

    /// <summary>Ask for a stop. Returns the seconds the body is held from now — 0 when the
    /// request was refused and the body is free.</summary>
    public float Request(float seconds)
    {
        if (seconds <= 0f) return _remaining;
        float room = FeedbackTable.StopBudgetSeconds - _spent;
        if (_remaining > 0f)
        {
            float extra = MathF.Min(seconds - _remaining, room);
            if (extra > 0f) { _remaining += extra; _spent += extra; }
            return _remaining;
        }
        if (_rearm > 0f) return 0f;
        float granted = MathF.Min(seconds, room);
        if (granted < SmallestGrant) return 0f;
        _remaining = granted;
        _spent += granted;
        return _remaining;
    }

    /// <summary>Advance one physics tick of WORLD time (never the held body's own time, or it
    /// would never wake). True when the body is held for this tick.</summary>
    public bool Tick(float worldDelta)
    {
        if (_spent > 0f) _spent = MathF.Max(0f, _spent - FeedbackTable.StopBudgetRecoveryPerSecond * worldDelta);
        if (_remaining > 0f)
        {
            _remaining -= worldDelta;
            if (_remaining <= Epsilon)
            {
                _remaining = 0f;
                _rearm = FeedbackTable.StopRearmSeconds;
            }
            return Held = true;
        }
        if (_rearm > 0f) _rearm -= worldDelta;
        return Held = false;
    }

    /// <summary>Let go at once (death, respawn). The budget is kept.</summary>
    public void Clear()
    {
        _remaining = 0f;
        _rearm = 0f;
        Held = false;
    }

    /// <summary>Checks the rules above with nothing but arithmetic.</summary>
    public static List<string> SelfTest()
    {
        var failures = new List<string>();
        const float tick = 1f / 60f;

        var clock = new HitStopClock();
        if (clock.Request(0.044f) < 0.043f || !clock.Active) failures.Add("a stop is not granted to a free body");
        int held = 0;
        for (int i = 0; i < 30; i++) if (clock.Tick(tick)) held++;
        if (held != 3) failures.Add($"a 44 ms stop holds {held} ticks, expected 3");

        clock = new HitStopClock();
        clock.Request(0.100f);
        clock.Request(0.050f);
        if (MathF.Abs(clock.Remaining - 0.100f) > 0.0001f) failures.Add("a shorter stop shortened a longer one");
        clock.Request(0.150f);
        if (MathF.Abs(clock.Remaining - 0.150f) > 0.0001f) failures.Add("a longer stop did not replace a shorter one (max, not sum)");

        clock = new HitStopClock();
        clock.Request(0.033f);
        while (clock.Tick(tick)) { }
        if (clock.Request(0.033f) != 0f) failures.Add("a body just out of a stop was stopped again (no re-arm)");
        for (int i = 0; i < 7; i++) clock.Tick(tick);      // 0.117 s later: past the 0.10 s re-arm
        if (clock.Request(0.033f) <= 0f) failures.Add("a body was still refused after the re-arm");

        // A hit every 0.2 s for 20 s: the share of time held must settle near the budget's
        // recovery rate, not near stop / (stop + re-arm).
        clock = new HitStopClock();
        int heldTicks = 0, ticks = 0;
        for (int hit = 0; hit < 100; hit++)
        {
            clock.Request(0.052f);
            for (int i = 0; i < 12; i++, ticks++) if (clock.Tick(tick)) heldTicks++;
        }
        float share = heldTicks / (float)ticks;
        if (share > FeedbackTable.StopBudgetRecoveryPerSecond + 0.06f)
            failures.Add($"a stream of hits holds a body {share:P0} of the time (budget {FeedbackTable.StopBudgetRecoveryPerSecond:P0})");
        if (share < 0.05f) failures.Add($"a stream of hits barely stops a body at all ({share:P0})");
        return failures;
    }
}

/// <summary>Where damage that is not a discrete hit comes from — the tag of "hurt.tick".</summary>
public enum HurtSource
{
    Hazard = 0,
    Burn = 1,
    OnFire = 2,
    Frozen = 3,
}

/// <summary>
/// Merges hazard and damage-over-time ticks into one <c>hurt.tick</c> cue per source kind at
/// most every <see cref="FeedbackTable.HurtTickCueSeconds"/>, carrying the damage since the
/// last one — a standing burn deals damage every frame and must not raise a cue every frame.
/// </summary>
public sealed class HurtTickMeter
{
    public const string Action = "hurt.tick";
    public static readonly string[] Tags = { "hazard", "burn", "on_fire", "frozen" };

    private readonly float[] _damage = new float[Tags.Length];
    private readonly float[] _wait = new float[Tags.Length];

    /// <summary>Report damage of one kind. Raises the cue now if that kind is due.</summary>
    public void Add(IFeedbackBody body, HurtSource source, float damage)
    {
        if (damage <= 0f) return;
        int i = (int)source;
        _damage[i] += damage;
        if (_wait[i] > 0f) return;
        CueBus.Emit(body, Action, magnitude: _damage[i], tag: Tags[i]);
        _damage[i] = 0f;
        _wait[i] = FeedbackTable.HurtTickCueSeconds;
    }

    public void Tick(float worldDelta)
    {
        for (int i = 0; i < _wait.Length; i++)
            if (_wait[i] > 0f) _wait[i] -= worldDelta;
    }

    public void Clear()
    {
        Array.Clear(_damage);
        Array.Clear(_wait);
    }
}

/// <summary>
/// The five statuses a body shows for as long as they last. Each is a loop on the cue bus:
/// <c>status.burning</c>, <c>status.on_fire</c>, <c>status.frozen</c>, <c>status.trapped</c>,
/// <c>status.stunned</c>. A body calls <see cref="Sync"/> with what it is under now and keeps
/// what comes back; the difference is started and stopped here.
/// </summary>
public static class StatusLoops
{
    public const FeedbackState Mask = FeedbackState.Burning | FeedbackState.OnFire | FeedbackState.Frozen
                                    | FeedbackState.Trapped | FeedbackState.Stunned;

    private static readonly FeedbackState[] Bits =
    {
        FeedbackState.Burning, FeedbackState.OnFire, FeedbackState.Frozen,
        FeedbackState.Trapped, FeedbackState.Stunned,
    };

    public static readonly string[] Actions =
    {
        "status.burning", "status.on_fire", "status.frozen", "status.trapped", "status.stunned",
    };

    /// <summary>Start the loops that began and stop the ones that ended. Returns the loops
    /// now running — the caller's next <paramref name="running"/>.</summary>
    public static FeedbackState Sync(IFeedbackBody body, FeedbackState running, FeedbackState now)
    {
        now &= Mask;
        FeedbackState changed = running ^ now;
        if (changed == FeedbackState.None) return now;
        for (int i = 0; i < Bits.Length; i++)
        {
            if ((changed & Bits[i]) == 0) continue;
            if ((now & Bits[i]) != 0) CueBus.Start(body, Actions[i]);
            else CueBus.Stop(body, Actions[i]);
        }
        return now;
    }
}

/// <summary>
/// The one hit-flash channel a body owns (<c>IFeedbackBody.FlashTint</c>): for a few frames
/// its sprites are drawn through a tiny shader that pulls them toward one colour and keeps
/// their alpha, so the body flashes as a silhouette — which <c>Modulate</c>, a multiply,
/// cannot do. The material is put on the sprites only while a flash runs and taken off when
/// it ends: at rest the body is drawn exactly as its scene authored it, and nothing here
/// touches <c>Modulate</c>, <c>SelfModulate</c> or <c>Visible</c> — those stay with the
/// status tints, the telegraph, the i-frame blink and the defeat fade.
/// </summary>
public sealed class BodyFlash
{
    private const string ShaderCode = @"shader_type canvas_item;
// Hit flash (Docs/Feedback.gdd 5): pull the sprite toward one colour, keep its alpha.
uniform vec4 flash_color : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform float flash_amount : hint_range(0.0, 1.0) = 0.0;
void fragment() {
	COLOR.rgb = mix(COLOR.rgb, flash_color.rgb, flash_amount);
}
";
    private static readonly StringName ColorParam = "flash_color";
    private static readonly StringName AmountParam = "flash_amount";
    private static Shader _shader;

    private readonly List<CanvasItem> _sprites = new();
    private readonly List<bool> _lent = new();     // which sprites currently wear our material
    private ShaderMaterial _material;
    private bool _collected;
    private float _left;

    /// <summary>True while the body is shown in the flash colour.</summary>
    public bool Active { get; private set; }
    /// <summary>The colour of the running flash; its alpha is the strength.</summary>
    public Color Tint { get; private set; }

    /// <summary>The sprites that make up a body: its own Sprite2D / AnimatedSprite2D children
    /// and theirs (a multi-part kit's extra pieces). Collected at the first flash, when every
    /// kit has finished building itself.</summary>
    private void Collect(Node node, int depth)
    {
        foreach (Node child in node.GetChildren())
        {
            if (child is not (Sprite2D or AnimatedSprite2D)) continue;
            _sprites.Add((CanvasItem)child);
            _lent.Add(false);
            if (depth < 1) Collect(child, depth + 1);
        }
    }

    public void Flash(Node body, Color tint, float seconds)
    {
        if (seconds <= 0f || tint.A <= 0f || body == null) return;
        if (!_collected)
        {
            _collected = true;
            Collect(body, 0);
        }
        if (_sprites.Count == 0) return;

        _shader ??= new Shader { Code = ShaderCode };
        _material ??= new ShaderMaterial { Shader = _shader };
        _material.SetShaderParameter(ColorParam, new Color(tint.R, tint.G, tint.B, 1f));
        _material.SetShaderParameter(AmountParam, Mathf.Clamp(tint.A, 0f, 1f));
        if (!Active)
        {
            for (int i = 0; i < _sprites.Count; i++)
            {
                CanvasItem sprite = _sprites[i];
                // A sprite with a material of its own keeps it: one owner per property.
                if (!GodotObject.IsInstanceValid(sprite) || sprite.Material != null) continue;
                sprite.Material = _material;
                _lent[i] = true;
            }
        }
        Active = true;
        Tint = tint;
        _left = Mathf.Max(_left, seconds);
    }

    /// <summary>Count the flash down in world time; it keeps running through a hit-stop.</summary>
    public void Tick(float worldDelta)
    {
        if (!Active) return;
        _left -= worldDelta;
        if (_left <= 0f) Clear();
    }

    /// <summary>End the flash now and hand the sprites back as they were.</summary>
    public void Clear()
    {
        _left = 0f;
        if (!Active) return;
        Active = false;
        for (int i = 0; i < _sprites.Count; i++)
        {
            if (!_lent[i]) continue;
            _lent[i] = false;
            if (GodotObject.IsInstanceValid(_sprites[i])) _sprites[i].Material = null;
        }
    }
}

/// <summary>Content ids are snake_case; a class name is the default for a body that states
/// none ("SifuPangda" → "sifu_pangda").</summary>
public static class FeedbackIds
{
    public static string FromTypeName(string typeName)
    {
        if (string.IsNullOrEmpty(typeName)) return CueOwners.Any;
        var id = new System.Text.StringBuilder(typeName.Length + 4);
        for (int i = 0; i < typeName.Length; i++)
        {
            char c = typeName[i];
            if (char.IsUpper(c) && i > 0 && !char.IsUpper(typeName[i - 1])) id.Append('_');
            id.Append(char.ToLowerInvariant(c));
        }
        return id.ToString();
    }
}
