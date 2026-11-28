namespace Fableland.Realms;

using System.Collections.Generic;
using Godot;
using Fableland.Data;

/// <summary>Why a body came out of a sub-realm (`Realms.gdd` §7).</summary>
public enum RealmExit { Border, Close, Timer }

/// <summary>
/// One open sub-realm (`Docs/Realms.gdd`, T50 §5). It projects the battlefield into its space by
/// the tuple (main anchor, sub anchor, ratio): <c>sub = subAnchor + ratio × (main − mainAnchor)</c>,
/// and back. A node beside whoever opened it, so it closes — everyone inside coming out — when its
/// opener closes it or the arena goes, whatever happens to the opener.
///
/// Every physics tick, in this order (T00 rule 3): the re-entry graces count down; a member moved
/// out of the space by other means (a respawn) is let go where it is; members whose ride is over
/// come out; members whose hit circle has cleared the exit border come out; a body standing in
/// the space that is not a member is adopted; then, for a continuous realm, anyone whose hit
/// circle touches the region is taken in.
/// </summary>
public partial class RealmInstance : Node
{
    /// <summary>A body coming in lands at least this far inside the exit border, so one step back
    /// does not throw it out.</summary>
    public const float EntryNudgeM = 0.5f;
    /// <summary>A body coming out over the border lands at least this far outside the region.</summary>
    public const float ExitNudgeM = 0.5f;
    /// <summary>After coming out, the same instance cannot take a body back for this long.</summary>
    public const float ReentryGraceSeconds = 0.5f;

    /// <summary>Every open instance (the lookup and checks read it).</summary>
    public static readonly List<RealmInstance> Active = new();

    public RealmDef Def { get; private set; }
    public RealmSpace Space { get; private set; }
    /// <summary>World px. May move while it stands — a wormhole: a body comes out relative to
    /// where it is now (`Realms.gdd` §4).</summary>
    public Vector2 MainAnchor { get; set; }
    public float RadiusPx { get; private set; }
    public float Ratio => Def.Ratio;
    /// <summary>The exit border, px from the space's anchor: the region's radius times the ratio.</summary>
    public float ExitRadiusPx => RadiusPx * Ratio;
    public CombatSide CasterSide { get; private set; }
    /// <summary>The name every status this instance lays is held under.</summary>
    public string Source { get; private set; } = "";
    public bool IsOpen { get; private set; }
    public int MemberCount => _members.Count;
    public bool Holds(Node2D body) => body != null && _members.ContainsKey(body.GetInstanceId());
    /// <summary>The last body to come out, and how (checks read them).</summary>
    public RealmExit LastExit { get; private set; }

    private IRealmSpaces _spaces;
    private readonly Dictionary<ulong, Node2D> _members = new();
    private readonly Dictionary<ulong, float> _grace = new();
    private readonly Dictionary<ulong, float> _ride = new();
    private readonly List<ulong> _scratch = new();

    public Vector2 ToSub(Vector2 main) => Space.Anchor + Ratio * (main - MainAnchor);
    public Vector2 ToMain(Vector2 sub) => MainAnchor + (sub - Space.Anchor) / Ratio;

    internal void Begin(RealmDef def, RealmSpace space, IRealmSpaces spaces, Vector2 mainAnchor, float radiusPx,
                        CombatSide casterSide, string source)
    {
        Def = def;
        Space = space;
        _spaces = spaces;
        MainAnchor = mainAnchor;
        RadiusPx = radiusPx;
        CasterSide = casterSide;
        Source = string.IsNullOrWhiteSpace(source) ? def.Id + "#" + GetInstanceId() : source;
        IsOpen = true;
        Active.Add(this);
        PullIn();   // everyone in scope as it opens, whatever its pull mode
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!IsOpen) return;
        float dt = (float)delta;

        _scratch.Clear();
        foreach (KeyValuePair<ulong, float> pair in _grace) _scratch.Add(pair.Key);
        foreach (ulong id in _scratch)
        {
            float left = _grace[id] - dt;
            if (left <= 0f) _grace.Remove(id);
            else _grace[id] = left;
        }

        _scratch.Clear();
        foreach (KeyValuePair<ulong, Node2D> pair in _members) _scratch.Add(pair.Key);
        foreach (ulong id in _scratch)
        {
            if (!_members.TryGetValue(id, out Node2D body)) continue;
            if (!GodotObject.IsInstanceValid(body) || !body.IsInsideTree()) { Forget(id); continue; }
            if (!Space.Bounds.HasPoint(body.GlobalPosition))
            {
                // Moved out by other means (a respawn, a teleport): it is where it is now. Let go
                // of it there — never project it back out of the border (KNOWLEDGE v0.12.21).
                if (body is ICombatTarget gone) gone.SetSilenced(Source, false);
                Forget(id);
                continue;
            }
            if (_ride.TryGetValue(id, out float ride))
            {
                ride -= dt;
                if (ride <= 0f) { Exit(body, RealmExit.Timer); continue; }
                _ride[id] = ride;
            }
            if (PastBorder(body)) Exit(body, RealmExit.Border);
        }

        AdoptStrays();
        if (Def.Pull == RealmPull.Continuous) PullIn();
    }

    /// <summary>A body standing in the space that is not yet a member — a protagonist switched in
    /// by Tab where the one before stood — is one now, so it comes out with everyone.</summary>
    private void AdoptStrays()
    {
        SceneTree tree = GetTree();
        if (tree == null) return;
        foreach (string group in new[] { CombatTargets.PlayerGroup, CombatTargets.EnemyGroup })
        {
            foreach (Node node in tree.GetNodesInGroup(group))
            {
                if (node is not Node2D body || node is not ICombatTarget target || node is IRealmImmune) continue;
                ulong id = node.GetInstanceId();
                if (_members.ContainsKey(id) || !Space.Bounds.HasPoint(body.GlobalPosition)) continue;
                _members[id] = body;
                if (Def.SilenceCastersEnemies && target.Side != CasterSide) target.SetSilenced(Source, true);
            }
        }
    }

    /// <summary>
    /// Brings a body in at a chosen spot in the space (world px) — PumpKing's recast blinking into
    /// his prison's mouth. A member already is moved there; a body from the main realm becomes a
    /// member, under this realm's rules. Lands on the nearest open spot. False when it cannot.
    /// </summary>
    public bool Admit(Node2D body, Vector2 spot)
    {
        if (!IsOpen || body == null || !GodotObject.IsInstanceValid(body) || body is not ICombatTarget target) return false;
        RealmInstance elsewhere = Realm.Of(body);
        if (elsewhere != null && elsewhere != this) return false;   // no nesting (v1)
        spot = BodyPlacement.FreeSpotNear(body, spot, ExitRadiusPx) ?? spot;
        BodyPlacement.Teleport(body, spot);
        ulong id = body.GetInstanceId();
        if (!_members.ContainsKey(id))
        {
            _members[id] = body;
            _grace.Remove(id);
            if (Def.RideSeconds > 0f) _ride[id] = Def.RideSeconds;
            if (Def.SilenceCastersEnemies && target.Side != CasterSide) target.SetSilenced(Source, true);
        }
        return true;
    }

    /// <summary>Everyone comes out at their projected spots; the space is given back.</summary>
    public void Close()
    {
        if (!IsOpen) return;
        _scratch.Clear();
        foreach (ulong id in _members.Keys) _scratch.Add(id);
        foreach (ulong id in _scratch)
            if (_members.TryGetValue(id, out Node2D body)) Exit(body, RealmExit.Close);
        _members.Clear();
        IsOpen = false;
        Active.Remove(this);
        _spaces?.Release(Space);
        QueueFree();
    }

    public override void _ExitTree()
    {
        if (!IsOpen) return;
        // The arena is going: nobody keeps a status from a realm that is gone.
        foreach (KeyValuePair<ulong, Node2D> pair in _members)
            if (GodotObject.IsInstanceValid(pair.Value) && pair.Value is ICombatTarget target) target.SetSilenced(Source, false);
        _members.Clear();
        IsOpen = false;
        Active.Remove(this);
        _spaces?.Release(Space);
    }

    // ── Who is in scope ───────────────────────────────────────────────────────

    private void PullIn()
    {
        SceneTree tree = GetTree();
        if (tree == null) return;
        foreach (string group in new[] { CombatTargets.PlayerGroup, CombatTargets.EnemyGroup })
        {
            foreach (Node node in tree.GetNodesInGroup(group))
            {
                if (node is not Node2D body || node is not ICombatTarget target || node is IRealmImmune) continue;
                if (!GodotObject.IsInstanceValid(node) || target.CurrentHp <= 0f) continue;
                ulong id = node.GetInstanceId();
                if (_members.ContainsKey(id) || _grace.ContainsKey(id)) continue;
                if (Realm.Of(body) != null) continue;   // already in a realm: no nesting (v1)
                if (body.GlobalPosition.DistanceTo(MainAnchor) - target.HitRadius > RadiusPx) continue;
                Enter(body, target);
            }
        }
    }

    private bool PastBorder(Node2D body)
    {
        float hit = body is ICombatTarget target ? target.HitRadius : 0f;
        return body.GlobalPosition.DistanceTo(Space.Anchor) - hit > ExitRadiusPx;
    }

    // ── In and out (T50 §5) ───────────────────────────────────────────────────

    private void Enter(Node2D body, ICombatTarget target)
    {
        Vector2 spot = ToSub(body.GlobalPosition);
        Vector2 fromAnchor = spot - Space.Anchor;
        float deepest = Mathf.Max(0f, ExitRadiusPx - Units.Px(EntryNudgeM));
        if (fromAnchor.Length() > deepest)
            spot = Space.Anchor + (fromAnchor.LengthSquared() > 1e-6f ? fromAnchor.Normalized() : Vector2.Zero) * deepest;
        spot = BodyPlacement.FreeSpotNear(body, spot, ExitRadiusPx) ?? spot;

        BodyPlacement.Teleport(body, spot);
        ulong id = body.GetInstanceId();
        _members[id] = body;
        if (Def.RideSeconds > 0f) _ride[id] = Def.RideSeconds;
        if (Def.SilenceCastersEnemies && target.Side != CasterSide) target.SetSilenced(Source, true);
    }

    private void Exit(Node2D body, RealmExit reason)
    {
        ulong id = body.GetInstanceId();
        float hit = body is ICombatTarget target ? target.HitRadius : 0f;
        Vector2 spot = ToMain(body.GlobalPosition);
        // Out over the border or at the end of a ride, the region may still stand: land clear of
        // it — sideways, keeping the body's height (a side view: never pushed into the floor or up
        // into the air). When the realm closes there is no region left to clear.
        if (reason != RealmExit.Close)
        {
            Vector2 fromAnchor = spot - MainAnchor;
            float nearest = RadiusPx + hit + Units.Px(ExitNudgeM);
            if (fromAnchor.Length() < nearest)
            {
                float side = fromAnchor.X < 0f ? -1f : 1f;
                float across = Mathf.Sqrt(Mathf.Max(0f, nearest * nearest - fromAnchor.Y * fromAnchor.Y));
                spot = new Vector2(MainAnchor.X + side * across, spot.Y);
            }
        }
        Rect2 bounds = _spaces?.MainBounds ?? new Rect2(spot, Vector2.Zero);
        if (bounds.Size != Vector2.Zero)
            spot = new Vector2(Mathf.Clamp(spot.X, bounds.Position.X + hit, bounds.End.X - hit),
                               Mathf.Clamp(spot.Y, bounds.Position.Y + hit, bounds.End.Y - hit));
        spot = BodyPlacement.FreeSpotNear(body, spot, Units.Px(4f)) ?? spot;

        BodyPlacement.Teleport(body, spot);
        if (body is ICombatTarget left) left.SetSilenced(Source, false);
        Forget(id);
        if (reason != RealmExit.Close) _grace[id] = ReentryGraceSeconds;
        LastExit = reason;
    }

    private void Forget(ulong id)
    {
        _members.Remove(id);
        _ride.Remove(id);
    }
}
