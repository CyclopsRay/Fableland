using Godot;

namespace Fableland.Missions;

/// <summary>
/// Placeholder boss (S3): a Crab scaled up — HP ×8, a roughly 2× sprite/collision scale, and
/// beefier contact. Real boss kits (FOES §8) are TBD; this keeps the LV4/LV6 fight structurally
/// real (boss HP bar, timer, victory/permadeath) until then. Still a CrabFoe, so at level 4+ it
/// keeps Soft Shell / Jump / spawn-on-death from its base (adds flavor to the placeholder).
/// </summary>
public partial class BossCrab : CrabFoe
{
    protected override void InitFoe()
    {
        base.InitFoe();
        BaseHP *= 8f;              // boss bulk (applied before Init scales by foe level)
        ContactRange = 80f;
        ContactKnockback = 380f;
        ContactStun = 0.35f;
        HitRadius = 52f;
        SightInterval = 0.8f;
    }

    public override void _Ready()
    {
        base._Ready();
        Scale = new Vector2(2f, 2f);   // ~2× visual + collision footprint
    }
}
