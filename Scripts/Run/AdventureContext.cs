using Fableland.Map;

namespace Fableland.Run;

/// <summary>
/// Immutable snapshot of the node the player just entered — the ONLY thing an Adventure
/// scene reads to configure itself (T10 §3 handshake). Null ⇒ the scene was launched
/// directly (F5) and must fall back to debug defaults.
/// </summary>
public sealed class AdventureContext
{
    public string NodeId;
    public int NodeLevel;          // 1..6 (map difficulty axis)
    public MissionType Mission;    // rolled at mapgen (combat nodes); Boss for LV4/LV6
    public NodeKind Kind;          // Combat/Boss/Shelter/QuestionMark
    public int Day;                // day the adventure started (foe difficulty axis)
    public bool IsRevisitCombat;   // re-attempt of a previously-failed combat node (NODES §1.3)
    public string Terrain;         // combat-map selection terrain from the entered node's altitude label
    public string CombatMapPath;   // absolute selected authored map path; empty => legacy Arena fallback
    /// <summary>True when this fight is in zone 6 on a map borrowed from one of the run's
    /// worlds (MapGDD §7): <see cref="CombatMapPath"/> is that world's map, and the arena
    /// draws it as the VOID has left it — drained and dark (<c>VoidTreatment</c>). False in
    /// the outer worlds, and in zone 6 when nothing matched and the legacy arena opens.</summary>
    public bool VoidTouched;
    /// <summary><c>BossTable</c> id of the boss holding this node: a realm's 4-1 (its own boss
    /// or the stand-in, <c>BossTable.ForWorld</c>), a zone-6 LV5 node (the variant's mini-boss
    /// for that gate) or the zone-6 core (the variant's final boss) — Docs/Zone6.gdd §3.
    /// Empty ⇒ the arena fields the placeholder (FOES §8.1).</summary>
    public string BossId = "";
    /// <summary>The protagonist beating this node recruits (NODES §3.1, §4.5): a realm's 4-1
    /// recruits that realm's home protagonist — whichever boss kit holds it (the stand-in
    /// included), because the recruit belongs to the realm, not the kit. Empty for every other
    /// node, for a realm with no protagonist yet, and in zone 6. Looked up by
    /// <see cref="RunState"/> from the world data, so the arena never asks which world it is in.</summary>
    public string RecruitId = "";
    /// <summary>Frozen party order captured exactly once at combat entry. The Arena
    /// owns its live switch cursor; shelter edits affect the next battle only.</summary>
    public BattleTeamSnapshot BattleTeam;
    /// <summary>The rules of this place (Docs/Zone6.gdd §7): the realm's law, zone 6's gravity
    /// and role, the seals the run carries. Computed by <see cref="RunState"/> at entry; the
    /// arena applies it to every body and never asks the run why.</summary>
    public ArenaRules Rules = ArenaRules.None;
}
