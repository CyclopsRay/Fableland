using Godot;

/// <summary>
/// Crab foe (FOES.gdd §3) — the grounded melee bruiser. Wall-turn patrol, walks toward
/// the player when aggro'd, deals contact damage. Passive Soft Shell doubles damage
/// from a source in the upward cone. Skill 1 = Spawn-on-death (lv4+), Skill 2 = Jump
/// (lv6+), both gated in <see cref="BaseFoe"/> via <see cref="FoeStats"/>.
/// </summary>
public partial class CrabFoe : BaseFoe
{
    // Set by the spawner (GameManager) and propagated to babies, so a crab can spawn
    // more crabs without a self-referential .tscn (which Godot would reject as cyclic).
    public PackedScene BabyCrabScene;

    protected override void InitFoe()
    {
        // FOES §3 base stats (verbatim).
        BaseHP = 70f;
        BaseDamage = 18f;
        BaseMoveSpeed = 90f;
        Gravity = Units.Gravity;
        UseGravity = true;
        ContactRange = 44f;
        ContactCooldown = 0.9f;
        ContactKnockback = 260f;
        ContactStun = 0.25f;
        HasContactDamage = true;
        HitRadius = 26f;
        ExternalDamping = 1200f;
        BurnDamagePerSecond = 14f;
        FootOffset = 22f;          // 44 px tall collision → center is 22 px above feet
        SightInterval = 1f;
    }

    // ── Passive — Soft Shell: 2× damage from a source within a 60° cone above ────────
    protected override float IncomingDamageMult(Vector2 sourcePos)
    {
        Vector2 d = sourcePos - GlobalPosition;
        if (d.LengthSquared() < 0.01f) return 1f;
        // Vector2.Up is (0,-1) in Godot; half-angle 30° → cone spans 60° centered on up.
        if (d.Normalized().Dot(Vector2.Up) >= Mathf.Cos(Mathf.DegToRad(30f))) return 2f;
        return 1f;
    }

    // ── Patrol: wall-turn ───────────────────────────────────────────────────────────
    protected override void UpdatePatrol(float dt)
    {
        if (IsOnWall()) Dir = -Dir;
        IntentVel.X = Dir * CurrentMoveSpeed;
        FacingDir = Dir;
    }

    // ── Aggro: walk toward player's X ────────────────────────────────────────────────
    protected override void UpdateAggro(float dt, CharacterController player)
    {
        float dx = player.GlobalPosition.X - GlobalPosition.X;
        Dir = Mathf.Sign(dx == 0f ? Dir : dx);
        IntentVel.X = Dir * CurrentMoveSpeed;
        FacingDir = Dir;
    }

    // ── Skill 2 — Jump (lv6+, 5 s cd): reposition to the player's current height ─────
    protected override void UpdateSkills(float dt, CharacterController player)
    {
        if (!CanAct || player == null || !IsAggro) return;
        if (!Skill2Ready || !IsOnFloor()) return;

        float dist = (player.GlobalPosition - GlobalPosition).Length();
        bool playerJumping = player.Velocity.Y < -Units.JumpSpeed * 0.5f;   // rising fast
        if (dist <= Units.Px(3f) || playerJumping)
        {
            StartSkill2Cooldown(5f);
            // Launch so the apex reaches the player's current height above the crab.
            float h = Mathf.Max(Units.Px(1f), GlobalPosition.Y - player.GlobalPosition.Y);
            IntentVel.Y = -Mathf.Sqrt(2f * Gravity * h);   // no landing damage (pure reposition)
        }
    }

    // ── Skill 1 — Spawn-on-death (lv4+): 2 crabs at level-2 (≥2), no grandchildren ───
    protected override void OnDeath()
    {
        if (!SpawnedByDeath && FoeStats.HasSkill1(CurrentLevel) && BabyCrabScene != null)
        {
            int babyLevel = Mathf.Max(2, CurrentLevel - 2);
            Node parent = GetParent();
            for (int i = 0; i < 2; i++)
            {
                var baby = BabyCrabScene.Instantiate<CrabFoe>();
                parent.AddChild(baby);                 // ignores the arena cap (direct spawn)
                float ox = (float)Rng.RandfRange(-24f, 24f);
                baby.GlobalPosition = GlobalPosition + new Vector2(ox, -8f);
                baby.BabyCrabScene = BabyCrabScene;    // propagate the scene ref
                baby.SpawnedByDeath = true;            // babies cannot themselves spawn
                baby.Init(babyLevel);                  // Init after AddChild
            }
        }
        base.OnDeath();   // loot hook (content TBD)
    }

    // ── Sight: ground-anchored rectangle, 8×3 m (12×5 m improved) ───────────────────
    protected override bool CanSeePlayer(CharacterController player)
    {
        bool improved = FoeStats.HasImprovedSight(CurrentLevel);
        float w = Units.Px(improved ? 12f : 8f);
        float h = Units.Px(improved ? 5f : 3f);
        float feetY = GlobalPosition.Y + FootOffset;
        Vector2 p = player.GlobalPosition;
        return p.X >= GlobalPosition.X - w * 0.5f && p.X <= GlobalPosition.X + w * 0.5f
            && p.Y <= feetY && p.Y >= feetY - h;
    }

    protected override void DrawSight()
    {
        bool improved = FoeStats.HasImprovedSight(CurrentLevel);
        float w = Units.Px(improved ? 12f : 8f);
        float h = Units.Px(improved ? 5f : 3f);
        float feetY = FootOffset;   // local space (origin = crab center)
        var rect = new Rect2(new Vector2(-w * 0.5f, feetY - h), new Vector2(w, h));
        Color c = IsAggro ? new Color(1f, 0.4f, 0.3f, 0.6f) : new Color(0.4f, 0.8f, 1f, 0.4f);
        DrawRect(rect, c, false, 2f);
    }

    // NOTE(animation): empty AnimationPlayer sits in CrabFoe.tscn; drive Walk/Aggro/Jump
    // clips + move contact-damage onto a call-method track once real animations land.
}
