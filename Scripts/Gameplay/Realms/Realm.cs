namespace Fableland.Realms;

using System.Collections.Generic;
using Godot;
using Fableland.Data;

/// <summary>A body that never goes into a sub-realm: mission objectives, pickups, props
/// (`Realms.gdd` §5).</summary>
public interface IRealmImmune { }

/// <summary>
/// A prepared space for one sub-realm instance (T50 §3): built before the fight, far from the
/// battlefield, so a body inside it is physically elsewhere — it touches only that space's ground,
/// and distance-based hits and senses already ignore every other realm.
/// </summary>
public sealed class RealmSpace
{
    public RealmDef Def { get; init; }
    /// <summary>World px: the point the main anchor projects onto (the patch's centre).</summary>
    public Vector2 Anchor { get; init; }
    /// <summary>World px: everything of the space lies inside; a body whose position is inside it
    /// is in this realm (T50 §4 — membership follows position, so there is no second record of it
    /// to fall out of step, T00 rule 2).</summary>
    public Rect2 Bounds { get; init; }
    /// <summary>World px: where the art is — the camera of someone inside never looks past it, so
    /// the world never visibly ends (`Realms.gdd` §6). Empty: <see cref="Bounds"/>.</summary>
    public Rect2 View { get; init; }
    /// <summary>The node the space was built under, or null.</summary>
    public Node2D Root { get; init; }
}

/// <summary>Whoever builds spaces — the arena, or a check — joined to the tree in
/// <see cref="Realm.SpacesGroup"/>, so a kit never has to name the arena (T50 §1).</summary>
public interface IRealmSpaces
{
    /// <summary>A free, prepared space for an instance of <paramref name="def"/>; null if none.</summary>
    RealmSpace Acquire(RealmDef def);
    /// <summary>The instance has closed: its space may serve again.</summary>
    void Release(RealmSpace space);
    /// <summary>The battlefield, world px: where a body coming out may land.</summary>
    Rect2 MainBounds { get; }
}

/// <summary>
/// "Which realm is this in?" and "open one" — the realm system's one entry for gameplay
/// (`Docs/Realms.gdd`, T50). A position inside an open instance's space is in that realm;
/// anywhere else is the main realm (null).
/// </summary>
public static class Realm
{
    public const string SpacesGroup = "realm_spaces";
    /// <summary>The draw layer of a realm's window on the battlefield: above its art (close view
    /// 20, effects 30). A realm's own rim (the Black Prison's) draws one above it.</summary>
    public const int WindowZ = 40;

    /// <summary>The open instance whose space holds <paramref name="position"/>; null = the main realm.</summary>
    public static RealmInstance Of(Vector2 position)
    {
        foreach (RealmInstance realm in RealmInstance.Active)
            if (realm.IsOpen && realm.Space.Bounds.HasPoint(position)) return realm;
        return null;
    }

    public static RealmInstance Of(Node2D body) =>
        body != null && GodotObject.IsInstanceValid(body) ? Of(body.GlobalPosition) : null;

    /// <summary>Both in the same realm (the main one counts).</summary>
    public static bool SameAs(Node2D a, Node2D b) => Of(a) == Of(b);

    /// <summary>The targets <paramref name="asker"/>'s side may hit in the asker's own realm —
    /// for whatever ignores distance (a darkness over every foe, a brain looking for the player).
    /// Distance-based hits need no filter: other realms are physically far away.</summary>
    public static IEnumerable<ICombatTarget> HostileTo(Node2D asker, CombatSide side)
    {
        if (asker == null || !GodotObject.IsInstanceValid(asker) || !asker.IsInsideTree()) yield break;
        RealmInstance realm = Of(asker);
        foreach (ICombatTarget target in CombatTargets.HostileTo(asker.GetTree(), side))
            if (target is Node2D node && Of(node) == realm) yield return target;
    }

    /// <summary>
    /// Opens an instance of <paramref name="defId"/>: a circle of <paramref name="radiusPx"/> around
    /// <paramref name="mainAnchor"/>, for <paramref name="casterSide"/>, its statuses held under
    /// <paramref name="source"/>. Refused (null) from inside a sub-realm (no nesting in v1,
    /// `Realms.gdd` §11), with no definition, or with no free space.
    /// </summary>
    public static RealmInstance Open(Node2D opener, string defId, Vector2 mainAnchor, float radiusPx,
                                     CombatSide casterSide, string source)
    {
        if (opener == null || !GodotObject.IsInstanceValid(opener) || !opener.IsInsideTree()) return null;
        if (Of(opener) != null) return null;
        if (!RealmTable.TryGet(defId, out RealmDef def)) return null;
        if (opener.GetTree().GetFirstNodeInGroup(SpacesGroup) is not IRealmSpaces spaces) return null;
        RealmSpace space = spaces.Acquire(def);
        if (space == null) return null;

        var instance = new RealmInstance { Name = "Realm" };
        Node parent = opener.GetParent() ?? opener;
        parent.AddChild(instance);
        instance.Begin(def, space, spaces, mainAnchor, radiusPx, casterSide, source);
        return instance;
    }
}
