using Godot;

namespace Fableland.Missions;

/// <summary>
/// A Destroy-mission target (NODES §4.2, report Q8). It subclasses <see cref="BaseFoe"/> purely
/// so the player's existing attacks hit it — <see cref="CharacterController.MeleeCone"/> and
/// Pomegraknight's seeds iterate group "enemy" and type-check <c>BaseFoe</c>, so a shared base
/// is the cleanest way in without touching the attack code. Everything foe-like is switched off:
/// no movement (speed 0), no contact damage, no sight/aggro, and evolution suppressed
/// (<see cref="CanEvolve"/>) so a target can't level up mid-fight and grow its HP. HP is set
/// table-driven via <see cref="BaseFoe.OverrideMaxHp"/>, not foe-level scaling.
/// </summary>
public partial class DestroyObjective : BaseFoe
{
    protected override void InitFoe()
    {
        BaseHP = 60f;
        BaseMoveSpeed = 0f;         // stationary
        BaseDamage = 0f;
        UseGravity = true;          // settles onto ground/platform
        HasContactDamage = false;
        HitRadius = 34f;            // generous — easy to clip
        ContactRange = 0f;
        SightInterval = 999f;
        FootOffset = 22f;
    }

    protected override bool CanEvolve => false;

    public override void _Ready()
    {
        base._Ready();
        AddToGroup("objective");
        // Stay in group "enemy" (player attacks iterate it) but leave group "foe":
        // "foe" is the ambient-spawner cap count (FoeSpawner.LiveFoeCount) and the mission
        // sweep group — 5 objectives counting against a cap of 6 would starve the Destroy
        // mission of the hostile foes meant to harass the player (review finding, v0.5.0).
        RemoveFromGroup("foe");
    }
}
