using Godot;
using System;
using System.Collections.Generic;
using Fableland.Data;

/// <summary>
/// The cue bus — the one place gameplay tells the rest of the game that something worth
/// seeing, hearing or feeling just happened (<c>Docs/Feedback.gdd</c> §2, T10 §4b).
///
/// GAMEPLAY RAISES, PRESENTATION LISTENS. A kit, a foe, a hazard or a mission calls
/// <c>CueBus.Emit</c> with a semantic name ("ba.swing", "jump", "death") and the numbers the
/// moment already has (where, which way, how big, how hard). It never names a particle, a
/// sound or a shake: the presentation directors (feel, VFX, audio) each look the cue up in
/// their own table. Adding an effect to a moment that already raises a cue touches no
/// gameplay file; adding a moment is one line here and a row there (T20 §1, "additive over
/// invasive").
///
/// A cue is keyed by two strings so nothing is concatenated per event:
///   Owner  — whose look and sound this is: a content id ("pomegraknight", "crab"), or a
///            system (<see cref="CueOwners"/>).
///   Action — what happened, dotted and lower-case: "ba.swing", "s1.cast", "hurt.tick", "land".
/// A director resolves (owner, action), then (the owner's class, action), then ("any", action)
/// — <see cref="CueTable{T}"/> does exactly that — so a new character sounds and looks like
/// *a* fighter before it has a row of its own.
///
/// Rules for whoever raises a cue:
///   • Raise it where the gameplay moment happens, with that moment's real geometry
///     (<see cref="Cue.Size"/> is the reach in px, <see cref="Cue.Extents"/> a box's
///     half-extents) — the effect and the hitbox must come from one number (50-ART-AUDIO §1).
///   • Never read anything back. Nothing a listener does may change a fight; listeners draw
///     their own randomness and never touch a run's <c>DetRandom</c> streams.
///   • One-shots are <c>Emit</c>; anything that lasts is <c>Start</c> … <c>Stop</c> with the
///     same source, owner and action (a status, a charge, a field). A loop whose source
///     leaves the tree is ended by the listeners; stopping it explicitly is still the rule.
///   • No per-event allocation: pass constant strings, never build one (T40 §2).
/// </summary>
public readonly struct Cue
{
    /// <summary>Content id or system whose style applies.</summary>
    public readonly string Owner;
    /// <summary>The class the lookup falls back to when the owner has no row
    /// (<see cref="CueOwners"/>: "fighter", "foe", "projectile"…); null goes straight to "any".</summary>
    public readonly string OwnerClass;
    /// <summary>What happened ("ba.swing", "jump"…).</summary>
    public readonly string Action;
    /// <summary>Stage or kind within the action (combo stage 1–3, wave index…); 0 if none.</summary>
    public readonly int Variant;
    /// <summary>Optional free label (a realm code, a medium, "burning").</summary>
    public readonly string Tag;
    /// <summary>The node the moment belongs to; may be null (a cue at a bare position), and
    /// may be freed by the time a listener acts later — check
    /// <c>GodotObject.IsInstanceValid</c> before calling into it, and match loops with
    /// <c>ReferenceEquals</c>.</summary>
    public readonly Node2D Source;
    /// <summary>World position in px.</summary>
    public readonly Vector2 Position;
    /// <summary>Unit direction the moment faces or travels, or zero.</summary>
    public readonly Vector2 Direction;
    /// <summary>The moment's real reach or radius in px; 0 if it has none.</summary>
    public readonly float Size;
    /// <summary>Half-extents in px when the moment is a box (a field, a rectangular blow);
    /// zero when <see cref="Size"/> says it all.</summary>
    public readonly Vector2 Extents;
    /// <summary>Free scalar: damage, a count, a speed in px/s. 0 if unused.</summary>
    public readonly float Magnitude;
    public readonly CombatSide Side;

    public Cue(string owner, string action, Node2D source, Vector2 position, CombatSide side,
               Vector2 direction = default, float size = 0f, float magnitude = 0f,
               int variant = 0, string tag = null, string ownerClass = null, Vector2 extents = default)
    {
        Owner = owner ?? CueOwners.Any;
        OwnerClass = ownerClass;
        Action = action ?? "";
        Source = source;
        Position = position;
        Side = side;
        Direction = direction;
        Size = size;
        Extents = extents;
        Magnitude = magnitude;
        Variant = variant;
        Tag = tag;
    }

    /// <summary>For logs and checks only — it allocates.</summary>
    public override string ToString() =>
        Variant != 0 ? $"{Owner}/{Action}#{Variant}" : $"{Owner}/{Action}";
}

/// <summary>
/// A hit that landed — raised once per hit by the two hit intakes
/// (<c>CharacterController.TakeHit</c>, <c>BaseFoe.TakeHit</c>), after damage is applied and
/// before the victim's death hook. Carries both ends: the attack decides the impact's style,
/// the victim its reaction. A hit that was refused (dead, invincible, i-frames) raises nothing.
/// </summary>
public readonly struct HitCue
{
    /// <summary>The body that was hit.</summary>
    public readonly Node2D Victim;
    /// <summary>The victim's content id ("crab") and class ("foe") — for its reaction.</summary>
    public readonly string VictimOwner;
    public readonly string VictimClass;
    /// <summary>Who swung, if the hit named one (<see cref="HitInfo.Attacker"/>); else null.</summary>
    public readonly Node2D Attacker;
    /// <summary>Content id of the attack's style ("pomegraknight"); <see cref="CueOwners.Any"/> if unnamed.</summary>
    public readonly string AttackOwner;
    /// <summary>The class the attack falls back to: the attacker's own, "projectile" for a
    /// ranged hit with no body at hand, null for an unnamed hit.</summary>
    public readonly string AttackClass;
    /// <summary>Which attack ("ba", "s2.seed"…); "" if unnamed.</summary>
    public readonly string AttackAction;
    public readonly int AttackVariant;
    /// <summary>Resolved feedback tier — never <see cref="HitWeight.Auto"/>.</summary>
    public readonly HitWeight Weight;
    /// <summary>Damage after mitigation (what the number shows).</summary>
    public readonly float Damage;
    /// <summary>The shove actually applied, px/s.</summary>
    public readonly Vector2 Knockback;
    /// <summary>World position of the impact (the victim's centre today).</summary>
    public readonly Vector2 Position;
    /// <summary>Unit direction the blow travels: from its source toward the victim, else
    /// along the shove, else zero.</summary>
    public readonly Vector2 Direction;
    public readonly bool Killed;
    /// <summary>True when the victim is the body the player wears.</summary>
    public readonly bool VictimIsPlayer;
    /// <summary>True for projectiles, fields and ticks: the attacker is not held.</summary>
    public readonly bool Ranged;
    /// <summary>Seconds the victim stays held by hit-stop from this moment (0 if it is not).</summary>
    public readonly float VictimStop;

    /// <summary>The victim as presentation may use it (flash, state, radius).</summary>
    public IFeedbackBody VictimBody => Victim as IFeedbackBody;

    public HitCue(IFeedbackBody victim, in HitInfo hit, HitWeight weight, float damage, Vector2 knockback,
                  Vector2 sourcePosition, bool killed, float victimStop)
    {
        Victim = victim as Node2D;
        VictimOwner = victim?.FeedbackId ?? CueOwners.Any;
        VictimClass = victim?.FeedbackClass;
        Attacker = hit.Attacker;
        AttackOwner = hit.CueOwner ?? CueOwners.Any;
        AttackClass = hit.Attacker is IFeedbackBody attacker ? attacker.FeedbackClass
            : hit.Ranged && hit.CueOwner != null ? CueOwners.Projectile : null;
        AttackAction = hit.CueAction ?? "";
        AttackVariant = hit.CueVariant;
        Weight = weight;
        Damage = damage;
        Knockback = knockback;
        Position = victim?.FeedbackCenter ?? sourcePosition;
        Vector2 travel = Position - sourcePosition;
        if (travel.LengthSquared() < 1f) travel = knockback;
        Direction = travel.LengthSquared() > 0.0001f ? travel.Normalized() : Vector2.Zero;
        Killed = killed;
        VictimIsPlayer = victim != null && victim.Side == CombatSide.Player;
        Ranged = hit.Ranged;
        VictimStop = victimStop;
    }
}

/// <summary>The owner names that are systems rather than content, and the class names the
/// lookup falls back to.</summary>
public static class CueOwners
{
    /// <summary>Last fallback: a row for ("any", action) answers every owner.</summary>
    public const string Any = "any";
    // Classes — what an owner falls back to when it has no row of its own.
    public const string Fighter = "fighter";      // a protagonist's body on the player's side
    public const string Boss = "boss";            // the same kit on the Foe side
    public const string Foe = "foe";              // a BaseFoe
    public const string Objective = "objective";  // mission objects that take hits
    public const string Projectile = "projectile";
    // Systems.
    public const string Hazard = "hazard";        // tag = the hazard kind ("fire", "lava"…)
    public const string World = "world";          // soft volumes, terrain, weather
    public const string Flow = "flow";            // missions, scenes, the run
    public const string Ui = "ui";
}

/// <summary>What a body is going through right now, for effects that change with state.
/// A snapshot the body computes on request; listeners never cache it.</summary>
[Flags]
public enum FeedbackState
{
    None = 0,
    Burning = 1 << 0,       // kit-level burn (Blush, burning seeds)
    OnFire = 1 << 1,        // the hazard stack
    Frozen = 1 << 2,
    Trapped = 1 << 3,
    Stunned = 1 << 4,       // gain-no window
    Shielded = 1 << 5,
    LowHp = 1 << 6,         // at or under FeedbackTable.LowHpRatio
    Airborne = 1 << 7,
    Submerged = 1 << 8,     // inside a soft volume that is a medium (water, foliage, cloud)
    Invulnerable = 1 << 9,
    Dead = 1 << 10,
}

/// <summary>
/// The narrow view presentation has of a body that takes part in fights. Implemented by
/// <c>CharacterController</c> and <c>BaseFoe</c>. Everything here is either read-only or
/// purely visual — nothing a listener calls can change the outcome of a fight. (Hit-stop is
/// gameplay: a body applies it to itself, and it is deliberately not on this interface.)
/// </summary>
public interface IFeedbackBody
{
    /// <summary>Content id whose look and sound this body has ("pomegraknight", "crab").</summary>
    string FeedbackId { get; }
    /// <summary>The class its cues fall back to (<see cref="CueOwners"/>).</summary>
    string FeedbackClass { get; }
    CombatSide Side { get; }
    FeedbackState FeedbackState { get; }
    /// <summary>World position effects on this body centre on.</summary>
    Vector2 FeedbackCenter { get; }
    /// <summary>Rough radius of the body in px, for sizing effects.</summary>
    float FeedbackRadius { get; }
    /// <summary>True while this body is held by hit-stop.</summary>
    bool InHitStop { get; }
    /// <summary>Show the whole body in one colour for a moment (a hit flash). The alpha is how
    /// strongly it is pulled toward that colour: 1 is a solid silhouette, 0.35 a tint. Visual
    /// only; the body restores its own resting look afterwards.</summary>
    void FlashTint(Color tint, float seconds);
}

/// <summary>
/// A director's table: rows keyed by (owner, action), resolved owner → class → "any". The
/// same lookup for every director, so "what does a crab's death look like" and "what does it
/// sound like" fall back the same way. Reads allocate nothing.
/// </summary>
public sealed class CueTable<T> where T : class
{
    private readonly Dictionary<(string Owner, string Action), T> _rows = new();

    public int Count => _rows.Count;
    public IEnumerable<(string Owner, string Action)> Keys => _rows.Keys;

    /// <summary>Add (or replace) the row for an owner — a content id, a class, a system, or
    /// <see cref="CueOwners.Any"/>.</summary>
    public CueTable<T> Add(string owner, string action, T row)
    {
        _rows[(owner ?? CueOwners.Any, action ?? "")] = row;
        return this;
    }

    /// <summary>The row for this owner's action, else its class's, else "any"'s, else null.</summary>
    public T Resolve(string owner, string ownerClass, string action)
    {
        if (action == null) return null;
        if (owner != null && _rows.TryGetValue((owner, action), out T row)) return row;
        if (ownerClass != null && _rows.TryGetValue((ownerClass, action), out row)) return row;
        return _rows.TryGetValue((CueOwners.Any, action), out row) ? row : null;
    }

    public T Resolve(in Cue cue) => Resolve(cue.Owner, cue.OwnerClass, cue.Action);

    /// <summary>The row for the attack a hit came from ("pomegraknight"/"ba" → "fighter"/"ba" → "any"/"ba").</summary>
    public T ResolveAttack(in HitCue hit) => Resolve(hit.AttackOwner, hit.AttackClass, hit.AttackAction);
}

/// <summary>
/// The bus itself. Static, main-thread, and allocation-free per event (listeners are kept in
/// plain lists, so raising a cue creates no garbage — T40 §2). A listener that throws is
/// logged and skipped: a broken effect must never cost a run (T10 §5).
///
/// Subscribe in <c>_EnterTree</c>, unsubscribe in <c>_ExitTree</c> — always (T10 §1): the bus
/// outlives every scene.
/// </summary>
public static class CueBus
{
    private static readonly List<Action<Cue>> RaisedListeners = new();
    private static readonly List<Action<HitCue>> HitListeners = new();
    private static readonly List<Action<Cue>> LoopStartListeners = new();
    private static readonly List<Action<Cue>> LoopStopListeners = new();

    /// <summary>A one-shot moment.</summary>
    public static event Action<Cue> Raised
    {
        add { if (value != null) RaisedListeners.Add(value); }
        remove { RaisedListeners.Remove(value); }
    }

    /// <summary>A hit landed (after damage is applied, before the victim's death hook).</summary>
    public static event Action<HitCue> HitLanded
    {
        add { if (value != null) HitListeners.Add(value); }
        remove { HitListeners.Remove(value); }
    }

    /// <summary>Something that lasts began. The same source + owner + action ends it.</summary>
    public static event Action<Cue> LoopStarted
    {
        add { if (value != null) LoopStartListeners.Add(value); }
        remove { LoopStartListeners.Remove(value); }
    }

    public static event Action<Cue> LoopStopped
    {
        add { if (value != null) LoopStopListeners.Add(value); }
        remove { LoopStopListeners.Remove(value); }
    }

    /// <summary>Master switch. Off, nothing is raised — for tooling that must not be
    /// disturbed. Gameplay never reads it.</summary>
    public static bool Enabled = true;

    /// <summary>Totals since boot (one-shots, hits, loop starts, loop stops) — they let a
    /// check prove a moment raised a cue without subscribing.</summary>
    public static int RaisedCount { get; private set; }
    public static int HitCount { get; private set; }
    public static int LoopStartCount { get; private set; }
    public static int LoopStopCount { get; private set; }

    // ── One-shots ────────────────────────────────────────────────────────────────
    /// <summary>Raise a cue for a body, in that body's own style — at its centre, or at
    /// <paramref name="at"/> when the moment happens elsewhere on it (a muzzle, the feet).</summary>
    public static void Emit(IFeedbackBody body, string action, int variant = 0, float size = 0f,
                            float magnitude = 0f, Vector2? direction = null, string tag = null,
                            Vector2? at = null, Vector2 extents = default)
    {
        if (!Enabled || body == null) return;
        Emit(new Cue(body.FeedbackId, action, body as Node2D, at ?? body.FeedbackCenter, body.Side,
                     direction ?? Vector2.Zero, size, magnitude, variant, tag, body.FeedbackClass, extents));
    }

    /// <summary>Raise a cue at a position, for an owner that is not a body (a hazard, the
    /// world, the flow of the game, a projectile that is about to free itself).</summary>
    public static void EmitAt(string owner, string action, Vector2 position, Node2D source = null,
                              CombatSide side = CombatSide.Player, int variant = 0, float size = 0f,
                              float magnitude = 0f, Vector2 direction = default, string tag = null,
                              string ownerClass = null, Vector2 extents = default)
    {
        if (!Enabled) return;
        Emit(new Cue(owner, action, source, position, side, direction, size, magnitude, variant, tag,
                     ownerClass, extents));
    }

    public static void Emit(in Cue cue)
    {
        if (!Enabled) return;
        RaisedCount++;
        Dispatch(RaisedListeners, cue);
    }

    public static void EmitHit(in HitCue hit)
    {
        if (!Enabled) return;
        HitCount++;
        for (int i = 0; i < HitListeners.Count; i++)
        {
            try { HitListeners[i](hit); }
            catch (Exception e) { GD.PushError($"[CueBus] hit listener failed: {e}"); }
        }
    }

    // ── Loops ────────────────────────────────────────────────────────────────────
    public static void Start(IFeedbackBody body, string action, int variant = 0, float size = 0f,
                             float magnitude = 0f, string tag = null, Vector2 extents = default,
                             Vector2 direction = default)
    {
        if (!Enabled || body == null) return;
        LoopStartCount++;
        Dispatch(LoopStartListeners, new Cue(body.FeedbackId, action, body as Node2D,
                 body.FeedbackCenter, body.Side, direction, size, magnitude, variant, tag,
                 body.FeedbackClass, extents));
    }

    public static void Stop(IFeedbackBody body, string action, string tag = null)
    {
        if (!Enabled || body == null) return;
        LoopStopCount++;
        Dispatch(LoopStopListeners, new Cue(body.FeedbackId, action, body as Node2D,
                 body.FeedbackCenter, body.Side, tag: tag, ownerClass: body.FeedbackClass));
    }

    public static void StartAt(string owner, string action, Node2D source, Vector2 position,
                               CombatSide side = CombatSide.Player, int variant = 0, float size = 0f,
                               float magnitude = 0f, string tag = null, string ownerClass = null,
                               Vector2 extents = default, Vector2 direction = default)
    {
        if (!Enabled) return;
        LoopStartCount++;
        Dispatch(LoopStartListeners, new Cue(owner, action, source, position, side, direction,
                 size, magnitude, variant, tag, ownerClass, extents));
    }

    public static void StopAt(string owner, string action, Node2D source, string tag = null,
                              string ownerClass = null)
    {
        if (!Enabled) return;
        LoopStopCount++;
        Vector2 position = source != null && GodotObject.IsInstanceValid(source) && source.IsInsideTree()
            ? source.GlobalPosition : Vector2.Zero;
        Dispatch(LoopStopListeners, new Cue(owner, action, source, position, CombatSide.Player,
                 tag: tag, ownerClass: ownerClass));
    }

    private static void Dispatch(List<Action<Cue>> listeners, in Cue cue)
    {
        // Index loop, one listener at a time: no allocation, and a listener that throws
        // (or unsubscribes while being called) does not silence the others.
        for (int i = 0; i < listeners.Count; i++)
        {
            try { listeners[i](cue); }
            catch (Exception e) { GD.PushError($"[CueBus] listener failed on {cue}: {e}"); }
        }
    }
}
