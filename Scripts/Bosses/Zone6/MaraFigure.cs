using System.Collections.Generic;
using Godot;
using Fableland.Data;

namespace Fableland.Bosses;

/// <summary>
/// Māra's emanation (化身, Docs/Mara.gdd §9.2), drawn until his sheet is painted: 4 m tall,
/// black-blue, three eyes, a tiger skin at the waist, bare claws, a noose at the belt, a small
/// flame aureole and the crown of five skulls — a sealed skull dark under a closed gold lotus,
/// an unsealed one burning. A mirror image (§5.5) is the same figure with a cold crown and no
/// shadow on the fire: the tell.
///
/// It is a child of the body's <c>Sprite2D</c> and borrows its material, so the hit flash and
/// the status tints reach it as they reach a sprite; it faces the way the sprite's FlipH says.
/// NOTE(art): placeholder — the keyed sheet replaces it (Mara.gdd §13 step 3).
/// </summary>
public partial class MaraFigure : Node2D
{
    /// <summary>Feet at +32 px from the body's origin (the class shape's convention); 4 m tall.</summary>
    private const float FeetY = 32f;
    private const float HeightPx = 128f;

    private readonly HashSet<string> _sealed = new();
    private float _age;

    /// <summary>A mirror image: cold crown, no shadow.</summary>
    public bool Image { get; set; }
    /// <summary>He fell in the Interval: the claws open and the crown goes out.</summary>
    public bool Freed { get; set; }
    /// <summary>The claws glow while an attack is in its tell.</summary>
    public bool Telling { get; set; }

    public void SetSealed(IEnumerable<string> poisons)
    {
        _sealed.Clear();
        if (poisons != null) foreach (string id in poisons) _sealed.Add(id);
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        _age += (float)delta;
        QueueRedraw();
    }

    public override void _Draw()
    {
        bool left = GetParent() is Sprite2D sprite ? sprite.FlipH : false;
        float d = left ? -1f : 1f;
        Vector2 P(float x, float y) => new(x * d, y);

        var skin = new Color(0.10f, 0.14f, 0.32f);
        var skinLight = new Color(0.18f, 0.24f, 0.48f);
        var gold = new Color(0.95f, 0.78f, 0.30f);
        var vermilion = new Color(0.86f, 0.22f, 0.10f);
        var bone = new Color(0.93f, 0.90f, 0.80f);
        float top = FeetY - HeightPx;   // −96

        // His shadow on the fire: only the true body casts one.
        if (!Image) DrawColoredPolygon(Ellipse(new Vector2(0f, FeetY), 40f, 7f), new Color(0f, 0f, 0f, 0.45f));

        // The aureole: a ring of flame behind him.
        Vector2 aureole = new(0f, top + 52f);
        for (int i = 0; i < 14; i++)
        {
            float a = Mathf.Pi + i * Mathf.Pi / 13f;
            float flick = 1f + 0.08f * Mathf.Sin(_age * 9f + i * 1.7f);
            Vector2 at = aureole + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 54f;
            Vector2 tip = aureole + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 66f * flick;
            Vector2 side = new Vector2(-Mathf.Sin(a), Mathf.Cos(a)) * 6f;
            DrawColoredPolygon(new[] { at - side, at + side, tip }, Image ? new Color(0.5f, 0.2f, 0.12f, 0.6f) : vermilion);
        }

        // Legs, the tiger skin, the torso.
        DrawRect(new Rect2(-18f, FeetY - 40f, 12f, 40f), skin);
        DrawRect(new Rect2(6f, FeetY - 40f, 12f, 40f), skin);
        DrawColoredPolygon(new[] { P(-24f, FeetY - 46f), P(24f, FeetY - 46f), P(20f, FeetY - 30f), P(-20f, FeetY - 30f) }, new Color(0.88f, 0.55f, 0.16f));
        for (int k = 0; k < 4; k++) DrawLine(P(-18f + k * 12f, FeetY - 45f), P(-14f + k * 12f, FeetY - 32f), new Color(0.1f, 0.06f, 0.02f), 2f);
        DrawColoredPolygon(new[] { P(-26f, FeetY - 46f), P(26f, FeetY - 46f), P(30f, top + 44f), P(-30f, top + 44f) }, skin);
        DrawLine(P(-26f, FeetY - 46f), P(-30f, top + 44f), gold, 1.5f);
        DrawLine(P(26f, FeetY - 46f), P(30f, top + 44f), gold, 1.5f);
        // The noose of craving at his belt: gold and red cord.
        DrawArc(P(-20f, FeetY - 40f), 7f, 0f, Mathf.Tau, 12, gold, 2f);
        DrawArc(P(-20f, FeetY - 40f), 4f, 0f, Mathf.Tau, 10, vermilion, 2f);

        // Arms and claws: the front arm reaches forward.
        Color claw = Telling ? new Color(1f, 0.86f, 0.4f) : bone;
        DrawLine(P(26f, top + 50f), P(48f, top + 72f), skinLight, 9f);
        DrawLine(P(-26f, top + 50f), P(-40f, top + 80f), skinLight, 9f);
        float open = Freed ? 6f : 0f;
        for (int k = -1; k <= 1; k++)
        {
            DrawLine(P(48f, top + 72f), P(60f + open, top + 70f + k * (5f + open)), claw, 2.5f);
            DrawLine(P(-40f, top + 80f), P(-50f - open, top + 86f + k * (4f + open)), claw, 2.5f);
        }

        // The head: three eyes, fangs.
        Vector2 head = new(0f, top + 30f);
        DrawCircle(head, 20f, skin);
        DrawArc(head, 20f, 0f, Mathf.Tau, 24, gold, 1.5f);
        var eye = Freed ? new Color(0.4f, 0.32f, 0.2f) : new Color(1f, 0.70f, 0.18f);
        DrawCircle(head + P(-7f, -2f), 3f, eye);
        DrawCircle(head + P(7f, -2f), 3f, eye);
        DrawCircle(head + new Vector2(0f, -11f), 2.6f, eye);                 // the third eye
        DrawColoredPolygon(new[] { head + P(-6f, 10f), head + P(-3f, 10f), head + P(-4.5f, 16f) }, bone);
        DrawColoredPolygon(new[] { head + P(3f, 10f), head + P(6f, 10f), head + P(4.5f, 16f) }, bone);

        // The crown of five skulls, each a poison in gate order.
        for (int i = 0; i < Zone6Table.Poisons.Length; i++)
        {
            Zone6PoisonDef poison = Zone6Table.Poisons[i];
            Vector2 skull = head + new Vector2((i - 2) * 8f * d, -22f + Mathf.Abs(i - 2) * 2f);
            bool isSealed = _sealed.Contains(poison.Id);
            DrawCircle(skull, 3.6f, isSealed ? new Color(0.35f, 0.33f, 0.3f) : bone);
            if (isSealed)
                DrawArc(skull + new Vector2(0f, 1f), 4.6f, Mathf.Pi, Mathf.Tau, 8, gold, 2f);   // a gold lotus closed over it
            else if (!Image && !Freed)
            {
                float flick = 1f + 0.2f * Mathf.Sin(_age * 11f + i);
                DrawColoredPolygon(new[] { skull + new Vector2(-2.6f, -3f), skull + new Vector2(2.6f, -3f), skull + new Vector2(0f, -3f - 7f * flick) }, vermilion);
            }
        }
    }

    private static Vector2[] Ellipse(Vector2 centre, float rx, float ry)
    {
        var points = new Vector2[16];
        for (int i = 0; i < points.Length; i++)
        {
            float a = i * Mathf.Tau / points.Length;
            points[i] = centre + new Vector2(Mathf.Cos(a) * rx, Mathf.Sin(a) * ry);
        }
        return points;
    }
}
