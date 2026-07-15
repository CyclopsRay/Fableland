using Godot;
using System.Collections.Generic;

/// <summary>
/// An enterable "go-inside" volume — the tree/bush/cloud/water archetype, as
/// opposed to the thin one-way <b>platform</b> you can only land on or cross.
///
/// A body falls directly into this Area2D; it has no standable one-way top. While
/// inside, it preserves normal movement, gravity, and jumps, then gradually applies
/// two indices — both of which affect players and foes alike:
///
///   • StagnationIndex (scene fallback 0.5) — velocity is pulled toward index·maxSpeed,
///     never overwritten on entry. A map tile kind may override this before instantiation.
///   • GravityIndex   (0.1) — a constant index·maxSpeed downward drift.
/// </summary>
public partial class SoftVolume : Area2D
{
    [Export] public float StagnationIndex = 0.5f;
    [Export] public float GravityIndex = 0.1f;
    // Multiplies a body's external-velocity damping while inside — knockback still
    // shoves you in, but the viscosity eats it quickly.
    [Export] public float ExternalDampingMult = 3f;

    /// <summary>
    /// Configures an authored runtime tile before it enters the tree. The map
    /// orchestrator supplies collision shapes in this volume's local/world-stable space;
    /// gameplay receives only Godot shapes and therefore remains independent of map data.
    /// The sprite in this reusable scene is hidden because the map owns the parallaxed art.
    /// </summary>
    public void ConfigureRuntimeCollision(IReadOnlyList<Shape2D> shapes,
        IReadOnlyList<Vector2> shapePositions)
    {
        if (shapes == null || shapePositions == null || shapes.Count != shapePositions.Count)
        {
            GD.PushError("SoftVolume: runtime collision configuration is invalid.");
            return;
        }

        var interior = GetNodeOrNull<CollisionShape2D>("Interior");
        if (interior == null)
        {
            GD.PushError("SoftVolume: scene is missing Interior CollisionShape2D.");
            return;
        }

        // This scene is normally instantiated once, but cleanup keeps the setup
        // idempotent for editor-preview rebuilds.
        foreach (Node child in GetChildren())
            if (child.Name.ToString().StartsWith("RuntimeInterior")) child.Free();

        if (shapes.Count == 0)
        {
            interior.Disabled = true;
        }
        else
        {
            interior.Disabled = false;
            interior.Shape = shapes[0];
            interior.Position = shapePositions[0];
            for (int i = 1; i < shapes.Count; i++)
            {
                var extra = new CollisionShape2D
                {
                    Name = "RuntimeInterior" + i,
                    Shape = shapes[i],
                    Position = shapePositions[i],
                };
                AddChild(extra);
            }
        }

        var sprite = GetNodeOrNull<Sprite2D>("Sprite2D");
        if (sprite != null) sprite.Visible = false;
    }

    public override void _Ready()
    {
        BodyEntered += OnBodyEntered;
        BodyExited += OnBodyExited;
    }

    private void OnBodyEntered(Node2D body)
    {
        if (body is CharacterController c) c.EnterSoftVolume(this);
        else if (body is BaseFoe e) e.EnterSoftVolume(this);
    }

    private void OnBodyExited(Node2D body)
    {
        if (body is CharacterController c) c.ExitSoftVolume(this);
        else if (body is BaseFoe e) e.ExitSoftVolume(this);
    }

    /// <summary>
    /// Gradually pulls a body's current intent velocity toward the SoftVolume cap.
    /// Normal gravity and a jump launch happen first; this method contributes a
    /// resistance delta-v rather than replacing either. Its default resistance is
    /// derived from the physical gravity constant, while <see cref="StagnationIndex"/>
    /// and <see cref="GravityIndex"/> retain their designer-facing meaning.
    /// </summary>
    public Vector2 ApplyVelocityResistance(Vector2 velocity, float baseMaxSpeed, float delta)
    {
        float maxInside = StagnationIndex * baseMaxSpeed;
        float drift = GravityIndex * baseMaxSpeed;
        float resistance = Units.Gravity * Mathf.Clamp(1f - StagnationIndex, 0f, 1f);
        return new Vector2(
            Mathf.MoveToward(velocity.X, Mathf.Clamp(velocity.X, -maxInside, maxInside), resistance * delta),
            Mathf.MoveToward(velocity.Y, Mathf.Clamp(velocity.Y, -maxInside, maxInside) + drift, resistance * delta));
    }
}
