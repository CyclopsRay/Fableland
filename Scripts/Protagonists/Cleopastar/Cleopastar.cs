using Godot;
using System.Collections.Generic;

/// <summary>
/// Cleopastar — precision sniper. Her five shared-ammo Glows become independent
/// stars; Shift changes their movement axis and E changes their effect axis.
/// See Docs/Cleopastar.gdd for the complete interaction contract.
/// </summary>
public partial class Cleopastar : CharacterController
{
    [ExportGroup("Star Shot (BA)")]
    [Export] public PackedScene CleoStarScene;
    [Export] public float StarSpeed = Units.Px(12f);
    [Export] public float StarMinDistance = Units.Px(8f);
    [Export] public float StarMaxDistance = Units.Px(60f);

    [ExportGroup("Nut's Decree (Shift)")]
    [Export] public float ShiftCooldownSeconds = 3f;

    [ExportGroup("Apep's Maw (E)")]
    [Export] public float BlackholeCooldownSeconds = 12f;

    private readonly List<CleoStar> _stars = new();
    private CleoStar _flightStar;
    private float _shiftCooldown;
    private float _blackholeCooldown;

    public override (float Remaining, float Max) ShiftCooldown => (_shiftCooldown, ShiftCooldownSeconds);
    public override (float Remaining, float Max) ESkillCooldown => (_blackholeCooldown, BlackholeCooldownSeconds);

    protected override void InitCharacter()
    {
        ConfigureAmmo("Cleopastar");
        // Stars are player-owned projectiles, so they survive a Tab switch. A fresh
        // Cleo body adopts them when she returns and can command them again.
        foreach (Node node in GetTree().GetNodesInGroup("cleopastar_star"))
            if (node is CleoStar star) RegisterStar(star);
    }

    public override void _Process(double delta)
    {
        base._Process(delta);
        float dt = (float)delta;
        if (_shiftCooldown > 0f) _shiftCooldown = Mathf.Max(0f, _shiftCooldown - dt);
        if (_blackholeCooldown > 0f) _blackholeCooldown = Mathf.Max(0f, _blackholeCooldown - dt);

        // The base dispatches BA on press. Release is intentionally handled here so a
        // click before 8 m becomes the documented buffered release, rather than a
        // special short shot.
        if (Input.IsActionJustReleased("attack") && IsInstanceValid(_flightStar))
            _flightStar.ReleaseFlight();
    }

    protected override void HandleBA()
    {
        PruneStars();
        if (CleoStarScene == null || _stars.Count >= 5 || IsInstanceValid(_flightStar)) return;
        if (!TryConsumeAmmo()) return;

        Vector2 origin = FirePoint != null ? FirePoint.GlobalPosition : GlobalPosition;
        Vector2 direction = GetGlobalMousePosition() - origin;
        if (direction.LengthSquared() < 0.0001f) direction = new Vector2(Facing, 0f);
        else direction = direction.Normalized();

        var star = CleoStarScene.Instantiate<CleoStar>();
        GetParent().AddChild(star);
        star.GlobalPosition = origin;
        star.Init(direction, StarSpeed, StarMinDistance, StarMaxDistance, DamageDealtMultiplier, Ammo);
        RegisterStar(star);
        _flightStar = star;

        // A shot restarts the one universal reload timer immediately. Its separate
        // attack interval begins only when this star's flight phase actually ends.
        RequestAmmoReload();
    }

    protected override void HandleSkill1()
    {
        if (_shiftCooldown > 0f) return;
        PruneStars();

        _shiftCooldown = ShiftCooldownSeconds;
        Vector2 capturedAim = GetGlobalMousePosition();
        var volley = new CleoGlideVolley();

        foreach (CleoStar star in _stars)
            if (IsInstanceValid(star) && star.IsStationary)
                star.BeginGlide(capturedAim, volley);
    }

    protected override void HandleSkill2()
    {
        if (_blackholeCooldown > 0f) return;
        PruneStars();
        if (_stars.Count == 0) return;

        Vector2 aim = GetGlobalMousePosition() - GlobalPosition;
        if (aim.LengthSquared() < 0.0001f) aim = new Vector2(Facing, 0f);
        else aim = aim.Normalized();

        CleoStar best = null;
        float bestAngle = float.MaxValue;
        float bestDistance = float.MaxValue;
        foreach (CleoStar star in _stars)
        {
            if (!IsInstanceValid(star) || star.IsResolving) continue;
            Vector2 toStar = star.GlobalPosition - GlobalPosition;
            float distance = toStar.Length();
            float angle = distance > 0.001f ? Mathf.Abs(aim.AngleTo(toStar / distance)) : 0f;
            if (angle < bestAngle - 0.0001f || (Mathf.IsEqualApprox(angle, bestAngle) && distance < bestDistance))
            {
                best = star;
                bestAngle = angle;
                bestDistance = distance;
            }
        }

        if (best == null || !best.ToggleBlackhole()) return;
        _blackholeCooldown = BlackholeCooldownSeconds;
    }

    protected override void HandleSkillUlt()
    {
        // Deferred by design: Constellation Reign needs the project-wide ultimate pass.
    }

    protected override bool TryUseExhaustedJumpSupport()
    {
        foreach (CleoStar star in _stars)
            if (IsInstanceValid(star) && star.TryConsumeForExhaustedJump(this))
                return true;
        return false;
    }

    private void OnStarFlightEnded(CleoStar star)
    {
        if (_flightStar != star) return;
        _flightStar = null;
        StartAmmoAttackInterval();
    }

    private void OnStarRemoved(CleoStar star)
    {
        _stars.Remove(star);
        if (_flightStar == star) _flightStar = null;
    }

    private void PruneStars()
    {
        _stars.RemoveAll(star => !IsInstanceValid(star));
        if (!IsInstanceValid(_flightStar)) _flightStar = null;
    }

    private void RegisterStar(CleoStar star)
    {
        if (star == null || _stars.Contains(star)) return;
        star.FlightEnded += OnStarFlightEnded;
        star.Removed += OnStarRemoved;
        _stars.Add(star);
        if (star.IsInFlight) _flightStar = star;
    }

    protected override void DrawDebug()
    {
        Vector2 aim = GetGlobalMousePosition() - GlobalPosition;
        if (aim.LengthSquared() < 0.0001f) aim = new Vector2(Facing, 0f);
        else aim = aim.Normalized();
        DrawLine(Vector2.Zero, aim * StarMaxDistance, new Color(0.25f, 0.95f, 0.95f, 0.28f), 2f);
        DrawCircle(aim * StarMinDistance, 4f, new Color(0.25f, 0.95f, 0.95f, 0.7f));
    }
}
