using Godot;

/// <summary>
/// An enterable "go-inside" volume — the tree/bush/cloud/water archetype, as
/// opposed to the thin one-way <b>platform</b> you can only land on or cross.
///
/// A body can stand on its one-way top (a child StaticBody on the Platform layer)
/// or press-down to sink inside. While inside, this volume rewrites the body's
/// movement: it halts falling, lets you move up/down like left/right, and applies
/// two indices — both of which affect players and foes alike:
///
///   • StagnationIndex (0.5) — intuitive movement is capped at index·maxSpeed.
///   • GravityIndex   (0.1) — a constant index·maxSpeed downward drift.
/// </summary>
public partial class SoftVolume : Area2D
{
    [Export] public float StagnationIndex = 0.5f;
    [Export] public float GravityIndex = 0.1f;
    // Multiplies a body's external-velocity damping while inside — knockback still
    // shoves you in, but the viscosity eats it quickly.
    [Export] public float ExternalDampingMult = 3f;

    public override void _Ready()
    {
        BodyEntered += OnBodyEntered;
        BodyExited += OnBodyExited;
    }

    private void OnBodyEntered(Node2D body)
    {
        if (body is CharacterController c) c.EnterSoftVolume(this);
        else if (body is Enemy e) e.EnterSoftVolume(this);
    }

    private void OnBodyExited(Node2D body)
    {
        if (body is CharacterController c) c.ExitSoftVolume(this);
        else if (body is Enemy e) e.ExitSoftVolume(this);
    }

    /// <summary>
    /// Velocity for a body inside the volume. <paramref name="horiz"/> and
    /// <paramref name="vert"/> are intent in [-1, 1] (vert: up = -1, down = +1).
    /// The intent is clamped to index·maxSpeed; the drift is added on top so the
    /// body always sinks gently when not actively climbing.
    /// </summary>
    public Vector2 ComputeVelocity(float baseMaxSpeed, float horiz, float vert)
    {
        float maxInside = StagnationIndex * baseMaxSpeed;
        float drift = GravityIndex * baseMaxSpeed;
        return new Vector2(
            Mathf.Clamp(horiz, -1f, 1f) * maxInside,
            Mathf.Clamp(vert, -1f, 1f) * maxInside + drift);
    }
}
