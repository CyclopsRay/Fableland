using Godot;

/// <summary>
/// What the player sees from inside a Black Prison (<c>Docs/PumpKing.gdd</c>): only the cell
/// around them (<see cref="BlackPrison.SightMetres"/> from their centre); past it, the Dark
/// Wood's cardboard horror backdrop instead of the map. Shown by the prison only while the
/// player's own body is one of its prisoners — that is, only if a Foe-side PumpKing ever casts
/// it on them.
///
/// Drawn in world space around the body, above everything in the world and below the HUD
/// (a separate canvas layer), so the HP bar and the ring stay readable in the dark.
/// TODO(art): the backdrop below is a placeholder of flat cut-outs.
/// </summary>
public partial class PrisonVeil : Node2D
{
    /// <summary>Above every world layer (the map's close view is 20, effects 30, ambience 5) and
    /// under the damage numbers (100): a prisoner still sees what their blows did.</summary>
    public const int Z = 95;
    /// <summary>Far enough to cover the view at any zoom the arena uses.</summary>
    private const float Far = 6000f;
    private const float Feather = 20f;
    private const int Segments = 48;

    private static readonly Color Board = new("#070A14");
    private static readonly Color Card = new("#111B3D");
    private static readonly Color CardEdge = new("#1B2D6B");
    private static readonly Color Ember = new("#FFD23F");

    private float _hole = Units.Px(BlackPrison.SightMetres);
    private readonly Vector2[] _quad = new Vector2[4];
    private readonly Color[] _quadColours = new Color[4];

    public void Setup(float holeRadiusPx)
    {
        _hole = holeRadiusPx;
        QueueRedraw();
    }

    public override void _Draw()
    {
        // Past the feather: the board, solid. Inside the feather: from clear to board.
        Ring(_hole, _hole + Feather, new Color(Board, 0f), Board);
        Ring(_hole + Feather, Far, Board, Board);

        // The cardboard horror backdrop: dead trees and a crooked house cut from card, standing
        // clear of the hole so nothing in it is ever covered.
        float h = _hole + Feather;
        for (int side = -1; side <= 1; side += 2)
        {
            for (int k = 0; k < 3; k++)
            {
                float x = side * (h + 60f + k * 170f);
                float tall = 260f + 70f * ((k + (side > 0 ? 1 : 0)) % 2);
                Vector2[] tree =
                {
                    // Counter-clockwise round a trunk with one branch out to each side.
                    new(x - 14f, 220f), new(x + 14f, 220f), new(x + 9f, 220f - tall * 0.55f), new(x + 60f, 220f - tall * 0.8f),
                    new(x + 6f, 220f - tall * 0.62f), new(x + 4f, 220f - tall), new(x - 4f, 220f - tall),
                    new(x - 7f, 220f - tall * 0.6f), new(x - 50f, 220f - tall * 0.72f), new(x - 9f, 220f - tall * 0.5f),
                };
                DrawColoredPolygon(tree, Card);
                DrawPolyline(Closed(tree), CardEdge, 2f);
            }
            float hx = side * (h + 140f);
            Vector2[] house = { new(hx - 50f, 230f), new(hx + 50f, 230f), new(hx + 46f, 150f), new(hx + 4f, 100f), new(hx - 48f, 156f) };
            DrawColoredPolygon(house, Card);
            DrawPolyline(Closed(house), CardEdge, 2f);
            DrawRect(new Rect2(hx - 10f, 170f, 18f, 20f), new Color(Ember, 0.85f));
        }
    }

    /// <summary>An annulus as quads, each with its own corner colours.</summary>
    private void Ring(float r0, float r1, Color inner, Color outer)
    {
        for (int i = 0; i < Segments; i++)
        {
            float a0 = i * Mathf.Tau / Segments;
            float a1 = (i + 1) * Mathf.Tau / Segments;
            Vector2 d0 = new(Mathf.Cos(a0), Mathf.Sin(a0));
            Vector2 d1 = new(Mathf.Cos(a1), Mathf.Sin(a1));
            _quad[0] = d0 * r0;
            _quad[1] = d1 * r0;
            _quad[2] = d1 * r1;
            _quad[3] = d0 * r1;
            _quadColours[0] = _quadColours[1] = inner;
            _quadColours[2] = _quadColours[3] = outer;
            DrawPolygon(_quad, _quadColours);
        }
    }

    private static Vector2[] Closed(Vector2[] polygon)
    {
        var closed = new Vector2[polygon.Length + 1];
        polygon.CopyTo(closed, 0);
        closed[^1] = polygon[0];
        return closed;
    }
}
