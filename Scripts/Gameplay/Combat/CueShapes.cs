using Godot;

/// <summary>
/// How a cue says the shape of an attack that is not a plain reach or a plain box
/// (<c>Docs/Feedback.gdd</c> §6). Gameplay packs the shape it already uses into the cue's
/// <see cref="Cue.Size"/> and <see cref="Cue.Extents"/>; a listener unpacks it — so what is
/// drawn and what hits come from one number (50-ART-AUDIO §1).
/// </summary>
public static class CueShapes
{
    /// <summary>
    /// A cone's extents: the half-extents of the box that bounds a cone of this reach and
    /// half-angle, pointing along its direction — (reach / 2, reach × sin(half-angle)). Raise
    /// the cue with <c>size: reach</c> and <c>extents: CueShapes.Cone(reach, halfAngleDeg)</c>.
    /// </summary>
    public static Vector2 Cone(float reach, float halfAngleDeg) =>
        new(reach * 0.5f, reach * Mathf.Sin(Mathf.DegToRad(Mathf.Clamp(halfAngleDeg, 0f, 90f))));

    /// <summary>The half-angle in radians of a cone packed by <see cref="Cone"/>, or 0 when
    /// the cue carries no cone (no reach, or no extents).</summary>
    public static float ConeHalfAngle(float reach, Vector2 extents) =>
        reach > 0.5f && extents.Y > 0.5f ? Mathf.Asin(Mathf.Clamp(extents.Y / reach, 0f, 1f)) : 0f;
}
