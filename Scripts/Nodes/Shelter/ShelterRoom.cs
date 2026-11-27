using Godot;
using Fableland.Data;

namespace Fableland.Shelter;

/// <summary>
/// A shelter's room (NODES §5.0): a floor to walk on, two walls and a ceiling that keep the
/// fighter in, and — until its art exists — a room drawn in code. The floor is at y = 0 and
/// the room runs from x = 0 to <see cref="WidthPx"/>; stations stand on the floor in front of
/// it (they are its siblings, so they draw over it — a node's own <c>_Draw</c> is behind its
/// children: KNOWLEDGE v0.6.7).
///
/// TODO(art): the whole room is a placeholder — a paper-coloured back wall tinted toward the
/// world's map colour, plank floor, beams, two windows onto the world's colour. Each world's
/// shelter will be its own picture (NODES §5.0, the art list there).
/// </summary>
public partial class ShelterRoom : Node2D
{
    public float WidthPx { get; private set; }
    public float HeightPx { get; private set; }
    private Color _accent;
    private ShelterKind _kind;

    /// <summary>The room for a layout, accented with a world's colour.</summary>
    public static ShelterRoom Build(ShelterLayoutDef layout, Color accent, ShelterKind kind)
    {
        var room = new ShelterRoom
        {
            Name = "Room",
            WidthPx = Units.Px(layout.WidthM),
            HeightPx = Units.Px(layout.HeightM),
            _accent = accent,
            _kind = kind,
        };
        float w = room.WidthPx, h = room.HeightPx, thick = Units.Px(6f);
        room.AddSolid("Floor", new Rect2(-thick, 0f, w + 2f * thick, thick));
        room.AddSolid("WallLeft", new Rect2(-thick, -h - thick, thick, h + 2f * thick));
        room.AddSolid("WallRight", new Rect2(w, -h - thick, thick, h + 2f * thick));
        room.AddSolid("Ceiling", new Rect2(-thick, -h - thick, w + 2f * thick, thick));
        return room;
    }

    /// <summary>Solid ground (layer Ground): fighters stand on it and cannot pass it.</summary>
    private void AddSolid(string name, Rect2 rect)
    {
        var body = new StaticBody2D { Name = name, Position = rect.GetCenter(), CollisionLayer = 0, CollisionMask = 0 };
        body.SetCollisionLayerValue(Units.LayerGround, true);
        body.AddChild(new CollisionShape2D { Shape = new RectangleShape2D { Size = rect.Size } });
        AddChild(body);
    }

    private Color WallColour => _kind switch
    {
        ShelterKind.Last => new Color(0.22f, 0.21f, 0.26f),                       // drained: past the singularity
        ShelterKind.Eidolon => new Color(0.66f, 0.62f, 0.80f),                    // between two realities
        _ => new Color(0.86f, 0.80f, 0.68f).Lerp(_accent, 0.18f),                 // paper, toward the world
    };

    public override void _Draw()
    {
        float w = WidthPx, h = HeightPx, m = Units.PixelsPerMeter;
        // Outside the room — wide enough for any window shape (the stretch aspect is "expand").
        DrawRect(new Rect2(-w, -h * 3f, w * 3f, h * 5f), new Color(0.06f, 0.06f, 0.09f));
        // The back wall, and a wainscot band at hip height.
        Color wall = WallColour;
        DrawRect(new Rect2(0f, -h, w, h), wall);
        DrawRect(new Rect2(0f, -1.4f * m, w, 1.4f * m), wall.Darkened(0.18f));
        DrawLine(new Vector2(0f, -1.4f * m), new Vector2(w, -1.4f * m), wall.Darkened(0.35f), 3f);
        // Two windows onto the world outside (its map colour; the VOID's black past the singularity).
        Color outside = _kind == ShelterKind.Last ? new Color(0.02f, 0.02f, 0.03f) : _accent.Darkened(0.15f);
        foreach (float share in new[] { 0.36f, 0.78f })
        {
            var frame = new Rect2(w * share - 2.5f * m, -11.5f * m, 5f * m, 4.5f * m);
            DrawRect(frame.Grow(0.25f * m), new Color(0.32f, 0.22f, 0.14f));
            DrawRect(frame, outside);
            DrawLine(new Vector2(frame.GetCenter().X, frame.Position.Y), new Vector2(frame.GetCenter().X, frame.End.Y), new Color(0.32f, 0.22f, 0.14f), 4f);
        }
        // Beams every 8 m, and the ceiling's.
        Color beam = new(0.36f, 0.25f, 0.16f);
        for (float x = 0f; x <= w + 1f; x += 8f * m) DrawRect(new Rect2(x - 0.3f * m, -h, 0.6f * m, h), beam);
        DrawRect(new Rect2(0f, -h, w, 0.8f * m), beam);
        // The floor: planks, then earth below.
        DrawRect(new Rect2(-w, 0f, w * 3f, h), new Color(0.20f, 0.14f, 0.10f));
        DrawRect(new Rect2(0f, 0f, w, 0.45f * m), new Color(0.47f, 0.33f, 0.21f));
        for (float x = 0f; x < w; x += 2.2f * m) DrawLine(new Vector2(x, 0f), new Vector2(x, 0.45f * m), new Color(0.30f, 0.20f, 0.12f), 2f);
    }
}
