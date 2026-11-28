namespace Fableland.Realms;

using Godot;

/// <summary>
/// Putting a body somewhere it fits (T50 §5): the search PumpKing's blink has used since v0.12.11,
/// for any body — the nearest spot to a point, within a distance, where the body's collision shape
/// overlaps no solid ground: the point itself, then up, sideways, down.
/// </summary>
public static class BodyPlacement
{
    private const float Step = 8f;

    private static readonly Vector2[] Search =
    {
        Vector2.Up, Vector2.Left, Vector2.Right, new(-0.7071f, -0.7071f), new(0.7071f, -0.7071f), Vector2.Down,
    };

    /// <summary>The nearest free spot; the point itself when the body has no shape or world to ask;
    /// null when nothing within <paramref name="within"/> px is free.</summary>
    public static Vector2? FreeSpotNear(Node2D body, Vector2 point, float within)
    {
        CollisionShape2D shape = null;
        foreach (Node child in body.GetChildren())
        {
            if (child is CollisionShape2D { Shape: not null, Disabled: false } found) { shape = found; break; }
        }
        PhysicsDirectSpaceState2D space = body.IsInsideTree() ? body.GetWorld2D()?.DirectSpaceState : null;
        if (shape == null || space == null) return point;

        var query = new PhysicsShapeQueryParameters2D { Shape = shape.Shape, CollisionMask = 1u << (Units.LayerGround - 1) };
        Transform2D placed = shape.GlobalTransform;
        Vector2 offset = placed.Origin - body.GlobalPosition;
        for (float d = 0f; d <= within; d += Step)
        {
            foreach (Vector2 dir in Search)
            {
                Vector2 spot = point + dir * d;
                placed.Origin = spot + offset;
                query.Transform = placed;
                if (space.IntersectShape(query, 1).Count == 0) return spot;
                if (d == 0f) break;   // the point is one spot, not six
            }
        }
        return null;
    }

    /// <summary>Moves a body, keeping its velocity (`Realms.gdd` §5: momentum is kept).</summary>
    public static void Teleport(Node2D body, Vector2 to)
    {
        body.GlobalPosition = to;
        body.ResetPhysicsInterpolation();
    }
}
