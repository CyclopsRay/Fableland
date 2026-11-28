namespace Fableland.Data;

using System;
using System.Collections.Generic;

/// <summary>Is a sub-realm a small map made for it, or a whole interactive map (`Realms.gdd` §2)?</summary>
public enum RealmKind { Patch, Map }

/// <summary>When a sub-realm takes bodies in (`Realms.gdd` §5): only as it opens, or also
/// whoever walks into its region while it stands.</summary>
public enum RealmPull { OnOpen, Continuous }

/// <summary>
/// What a sub-realm is (`Docs/Realms.gdd`, `Docs/Tech/T50-REALMS.md` §2). Pure data: the arena
/// builds a definition's space once, before a fight (T50 §8), and every opening of it is an
/// instance that projects the battlefield into that space by its <see cref="Ratio"/>.
/// </summary>
public sealed class RealmDef
{
    /// <summary>Stable id, e.g. <c>realm.kh.black_prison</c>.</summary>
    public string Id { get; init; }
    public RealmKind Kind { get; init; } = RealmKind.Patch;
    /// <summary>The map document the space is built from (made in the map editor, any world's
    /// tiles). Null: whoever prepares spaces builds it another way (a check's flat patch).</summary>
    public string MapPath { get; init; }
    /// <summary>Sub-realm metres per main-realm metre. 2 = everything inside is twice as far apart,
    /// so leaving takes twice as long — the ratio replaces a slow (`Realms.gdd` §4).</summary>
    public float Ratio { get; init; } = 1f;
    /// <summary>How far the space's ground and art go from its anchor, in sub metres. At least the
    /// exit border (the region's radius × the ratio), so the border cannot be seen from inside.</summary>
    public float ArtExtentM { get; init; }
    public RealmPull Pull { get; init; } = RealmPull.Continuous;
    /// <summary>0, or how long a body rides before it comes out by itself (the tsunami).</summary>
    public float RideSeconds { get; init; }
    /// <summary>Inside, the caster's enemies are silenced — skills and items (the Black Prison).</summary>
    public bool SilenceCastersEnemies { get; init; }
    /// <summary>A world whose laws apply inside (`RealmLawTable`), or null.</summary>
    public string LawsWorld { get; init; }
}

/// <summary>The sub-realm definitions. A check may register its own; ids are append-only.</summary>
public static class RealmTable
{
    /// <summary>PumpKing's Black Prison as a sub-realm (`Realms.gdd` §12): a patch of his horror
    /// world at ratio 2 — the 8 m circle's border is 16 m inside a patch that goes on to 24 m.</summary>
    public const string BlackPrisonId = "realm.kh.black_prison";

    private static readonly Dictionary<string, RealmDef> Defs = new(StringComparer.Ordinal)
    {
        [BlackPrisonId] = new RealmDef
        {
            Id = BlackPrisonId,
            Kind = RealmKind.Patch,
            MapPath = "res://Maps/Realms/kh_black_prison.json",
            Ratio = 2f,
            ArtExtentM = 24f,
            Pull = RealmPull.Continuous,
            SilenceCastersEnemies = true,
            LawsWorld = "KH",
        },
    };

    public static bool TryGet(string id, out RealmDef def)
    {
        def = null;
        return id != null && Defs.TryGetValue(id, out def);
    }

    /// <summary>Adds or replaces a definition (checks register their own test realms).</summary>
    public static void Register(RealmDef def)
    {
        if (def?.Id == null) throw new ArgumentException("a realm definition needs an id");
        Defs[def.Id] = def;
    }

    public static IEnumerable<RealmDef> All => Defs.Values;
}
