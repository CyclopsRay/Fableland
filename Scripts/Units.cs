using Godot;

/// <summary>
/// The project's standard physical model. Everything is derived from three fixed
/// facts so numbers stay consistent across characters:
///
///   • A player, with no other indication, is 2 m tall.
///   • Jump height is always 8 m.
///   • A ground jump takes 1 s (0.5 s up, 0.5 s down).
///
/// From t_apex = 0.5 s and h = 8 m:  h = ½·g·t²  ⇒  g = 2h/t² = 64 m/s²
/// and launch speed v0 = g·t = 32 m/s.
///
/// At 32 px/m that is g = 2048 px/s² and v0 = 1024 px/s.
/// </summary>
public static class Units
{
    public const float PixelsPerMeter = 32f;

    public const float GravityMps2 = 64f;   // 2·8 / 0.5²
    public const float JumpSpeedMps = 32f;  // g · t_apex

    public const float Gravity = GravityMps2 * PixelsPerMeter;    // 2048 px/s²
    public const float JumpSpeed = JumpSpeedMps * PixelsPerMeter; // 1024 px/s

    public const float PlayerHeightM = 2f;
    public const float PlayerHeightPx = PlayerHeightM * PixelsPerMeter; // 64 px

    // Default gain-no (hitstun) window per point of damage, when a skill doesn't
    // specify its own: stun_seconds = StunPerDamage · damage.
    public const float StunPerDamage = 0.05f;

    /// <summary>Meters → pixels.</summary>
    public static float Px(float meters) => meters * PixelsPerMeter;

    // Collision layer numbers (1-based, matching the project's layer names).
    public const int LayerPlayer = 1;
    public const int LayerFoes = 2;
    public const int LayerGround = 3;
    public const int LayerPlatform = 4;
    public const int LayerProjectile = 5;
    public const int LayerHazard = 6;
}
