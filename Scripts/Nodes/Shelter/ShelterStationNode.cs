using System.Collections.Generic;
using Godot;
using Fableland.Data;

namespace Fableland.Shelter;

/// <summary>
/// One station in a shelter's room (NODES §5.0): where it stands, the stretch of floor where
/// the player uses it, and — until its art exists — a picture drawn in code. Its origin is
/// its centre on the floor. The shelter screen tells it what it offers now and whether the
/// player stands at it; it draws a closed station dimmed and the station in reach lit, with
/// the interact key over it.
///
/// TODO(art): every picture below is a placeholder (the list is in NODES §5.0); a world's
/// own shelter replaces them station by station, keeping each station's width.
/// </summary>
public partial class ShelterStationNode : Node2D
{
    public ShelterStationDef Def { get; private set; }
    public ShelterStationBehaviour Behaviour { get; private set; }

    /// <summary>A spot the layout keeps for a station whose behaviour does not exist yet:
    /// drawn as an outline in debug mode only, never usable.</summary>
    public bool Reserved { get; private set; }

    /// <summary>What it offers in this visit (set by the shelter screen).</summary>
    public StationOffer Offer { get; private set; }

    /// <summary>True while the player stands at it.</summary>
    public bool Current { get; private set; }

    public float WidthPx => Units.Px(Def?.WidthM ?? 3f);
    public bool Usable => !Reserved && Behaviour != null;
    public bool InReach(float x) => Mathf.Abs(x - Position.X) <= WidthPx / 2f;

    private Color _accent = Colors.White;
    private string _keyGlyph = "E";
    private IReadOnlyList<Texture2D> _faces = System.Array.Empty<Texture2D>();
    private float _time;

    public static ShelterStationNode Create(ShelterStationDef def, ShelterStationBehaviour behaviour, float xPx, Color accent)
    {
        var node = new ShelterStationNode
        {
            Name = "Station_" + def.Id,
            Def = def,
            Behaviour = behaviour,
            Reserved = behaviour == null,
            Position = new Vector2(xPx, 0f),
            _accent = accent,
        };
        // Only the fire and the lantern move.
        node.SetProcess(def.Id is ShelterTable.Campfire or ShelterTable.Lantern);
        return node;
    }

    public void Show(StationOffer offer, bool current, string keyGlyph)
    {
        Offer = offer;
        Current = current;
        _keyGlyph = string.IsNullOrEmpty(keyGlyph) ? "E" : keyGlyph;
        Modulate = Reserved || offer.Enabled ? Colors.White : new Color(0.62f, 0.62f, 0.66f, 0.85f);
        QueueRedraw();
    }

    /// <summary>The team board's faces: the next battle's team, in order (empty slots as +).</summary>
    public void ShowFaces(IReadOnlyList<Texture2D> faces)
    {
        _faces = faces ?? System.Array.Empty<Texture2D>();
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        _time += (float)delta;
        QueueRedraw();
    }

    private static float M => Units.PixelsPerMeter;
    private static readonly Color Wood = new(0.45f, 0.30f, 0.18f);
    private static readonly Color DarkWood = new(0.28f, 0.18f, 0.11f);
    private static readonly Color Iron = new(0.30f, 0.31f, 0.36f);
    private static readonly Color Stone = new(0.55f, 0.55f, 0.58f);

    public override void _Draw()
    {
        if (Def == null) return;
        if (Reserved)
        {
            DrawReserved();
            return;
        }
        switch (Def.Id)
        {
            case ShelterTable.Road: DrawRoad(); break;
            case ShelterTable.TeamBoard: DrawTeamBoard(); break;
            case ShelterTable.Lantern: DrawLantern(); break;
            case ShelterTable.Campfire: DrawCampfire(); break;
            case ShelterTable.Whetstone: DrawWhetstone(); break;
            case ShelterTable.Anvil: DrawAnvil(); break;
            case ShelterTable.Plantation: DrawPlantation(); break;
            case ShelterTable.EnchantingAltar: DrawAltar(); break;
            default: DrawRect(new Rect2(-WidthPx / 2f, -1.5f * M, WidthPx, 1.5f * M), Wood); break;
        }
        DrawName();
        if (Current) DrawReach();
    }

    private void DrawName()
    {
        // Light letters on a dark outline read on any wall (the Last Shelter's is dark).
        Font font = ThemeDB.FallbackFont;
        float width = Mathf.Max(WidthPx, 5f * M);
        var at = new Vector2(-width / 2f, -4.6f * M);
        DrawStringOutline(font, at, Def.Name, HorizontalAlignment.Center, width, 15, 5, new Color(0.10f, 0.07f, 0.05f, 0.9f));
        DrawString(font, at, Def.Name, HorizontalAlignment.Center, width, 15,
            Current ? new Color(1f, 0.90f, 0.55f) : new Color(0.96f, 0.92f, 0.84f));
    }

    /// <summary>The station in reach: a glow on the floor, and the interact key over it when
    /// it can be used.</summary>
    private void DrawReach()
    {
        DrawRect(new Rect2(-WidthPx / 2f, -0.12f * M, WidthPx, 0.24f * M), new Color(1f, 0.86f, 0.45f, 0.75f));
        if (!Offer.Enabled) return;
        var bubble = new Rect2(-0.55f * M, -6.2f * M, 1.1f * M, 1.1f * M);
        DrawRect(bubble, new Color(0.10f, 0.09f, 0.12f, 0.92f));
        DrawRect(bubble, new Color(1f, 0.86f, 0.45f), false, 2f);
        DrawString(ThemeDB.FallbackFont, new Vector2(bubble.Position.X, bubble.End.Y - 0.3f * M), _keyGlyph,
            HorizontalAlignment.Center, bubble.Size.X, 18, Colors.White);
    }

    private void DrawReserved()
    {
        if (Fableland.Debug.DebugManager.Instance?.Enabled != true) return;
        var spot = new Rect2(-WidthPx / 2f, -2.5f * M, WidthPx, 2.5f * M);
        Color line = new(0.30f, 0.25f, 0.20f, 0.6f);
        for (float x = spot.Position.X; x < spot.End.X; x += 0.5f * M)
        {
            DrawLine(new Vector2(x, spot.Position.Y), new Vector2(Mathf.Min(x + 0.25f * M, spot.End.X), spot.Position.Y), line, 2f);
            DrawLine(new Vector2(x, 0f), new Vector2(Mathf.Min(x + 0.25f * M, spot.End.X), 0f), line, 2f);
        }
        float width = Mathf.Max(WidthPx, 6f * M);
        DrawString(ThemeDB.FallbackFont, new Vector2(-width / 2f, -3f * M), $"(reserved: {Def.Name})",
            HorizontalAlignment.Center, width, 13, line);
    }

    // ── Placeholders (TODO(art)) ─────────────────────────────────────────────────────

    /// <summary>A doorway in the left wall with an arrow over it.</summary>
    private void DrawRoad()
    {
        var door = new Rect2(-1.1f * M, -3.4f * M, 2.2f * M, 3.4f * M);
        DrawRect(door.Grow(0.3f * M), DarkWood);
        DrawRect(door, _accent.Darkened(0.55f));
        DrawRect(new Rect2(door.Position.X, door.End.Y - 0.6f * M, door.Size.X, 0.6f * M), _accent.Darkened(0.3f));
        var arrow = new[] { new Vector2(-0.9f * M, -4.0f * M), new Vector2(0.3f * M, -4.45f * M), new Vector2(0.3f * M, -3.55f * M) };
        DrawColoredPolygon(arrow, new Color(0.95f, 0.88f, 0.70f));
    }

    /// <summary>A notice board on two posts, pinned with the team's faces.</summary>
    private void DrawTeamBoard()
    {
        DrawRect(new Rect2(-1.5f * M, -3.6f * M, 0.25f * M, 3.6f * M), DarkWood);
        DrawRect(new Rect2(1.25f * M, -3.6f * M, 0.25f * M, 3.6f * M), DarkWood);
        var board = new Rect2(-1.65f * M, -3.7f * M, 3.3f * M, 2.2f * M);
        DrawRect(board.Grow(0.12f * M), DarkWood);
        DrawRect(board, new Color(0.72f, 0.56f, 0.36f));
        for (int i = 0; i < 3; i++)
        {
            var slot = new Rect2(board.Position.X + 0.2f * M + i * 1.0f * M, board.Position.Y + 0.55f * M, 0.9f * M, 0.9f * M);
            Texture2D face = i < _faces.Count ? _faces[i] : null;
            if (face != null) DrawTextureRect(face, slot, false);
            else DrawRect(slot, new Color(0.40f, 0.30f, 0.20f), false, 2f);
        }
    }

    /// <summary>A lantern on a post, its glow breathing.</summary>
    private void DrawLantern()
    {
        DrawRect(new Rect2(-0.1f * M, -3.6f * M, 0.2f * M, 3.6f * M), DarkWood);
        DrawRect(new Rect2(-0.1f * M, -3.6f * M, 0.9f * M, 0.15f * M), DarkWood);
        Vector2 at = new(0.65f * M, -2.85f * M);
        float glow = 0.55f + 0.08f * Mathf.Sin(_time * 3.1f);
        DrawCircle(at, 0.75f * M, new Color(1f, 0.78f, 0.35f, 0.16f * glow / 0.55f));
        DrawRect(new Rect2(at.X - 0.25f * M, at.Y - 0.35f * M, 0.5f * M, 0.7f * M), Iron);
        DrawRect(new Rect2(at.X - 0.17f * M, at.Y - 0.25f * M, 0.34f * M, 0.5f * M), new Color(1f, 0.82f, 0.40f, glow + 0.3f));
    }

    /// <summary>Crossed logs, a flickering flame and a bedroll beside it.</summary>
    private void DrawCampfire()
    {
        DrawLine(new Vector2(-0.9f * M, -0.1f * M), new Vector2(0.9f * M, -0.5f * M), DarkWood, 0.35f * M);
        DrawLine(new Vector2(-0.9f * M, -0.5f * M), new Vector2(0.9f * M, -0.1f * M), Wood, 0.35f * M);
        float f = Mathf.Sin(_time * 9f) * 0.12f * M;
        DrawColoredPolygon(new[] { new Vector2(-0.6f * M, -0.4f * M), new Vector2(0.6f * M, -0.4f * M), new Vector2(f, -2.1f * M) }, new Color(0.95f, 0.42f, 0.12f));
        DrawColoredPolygon(new[] { new Vector2(-0.35f * M, -0.4f * M), new Vector2(0.35f * M, -0.4f * M), new Vector2(-f, -1.5f * M) }, new Color(1f, 0.78f, 0.25f));
        DrawRect(new Rect2(1.0f * M, -0.45f * M, 0.7f * M, 0.45f * M), new Color(0.55f, 0.30f, 0.30f));
    }

    /// <summary>A grinding wheel on a frame.</summary>
    private void DrawWhetstone()
    {
        DrawLine(new Vector2(-0.9f * M, 0f), new Vector2(-0.3f * M, -1.4f * M), DarkWood, 0.2f * M);
        DrawLine(new Vector2(0.9f * M, 0f), new Vector2(0.3f * M, -1.4f * M), DarkWood, 0.2f * M);
        Vector2 hub = new(0f, -1.5f * M);
        DrawCircle(hub, 0.8f * M, Stone.Darkened(0.2f));
        DrawCircle(hub, 0.62f * M, Stone);
        DrawLine(hub, hub + new Vector2(0.6f * M, 0f), Stone.Darkened(0.4f), 2f);
        DrawCircle(hub, 0.12f * M, Iron);
    }

    /// <summary>An anvil on a stump.</summary>
    private void DrawAnvil()
    {
        DrawRect(new Rect2(-0.5f * M, -0.9f * M, 1.0f * M, 0.9f * M), Wood);
        DrawColoredPolygon(new[]
        {
            new Vector2(-0.45f * M, -0.9f * M), new Vector2(0.45f * M, -0.9f * M), new Vector2(0.3f * M, -1.3f * M),
            new Vector2(0.75f * M, -1.6f * M), new Vector2(0.75f * M, -1.85f * M), new Vector2(-0.95f * M, -1.85f * M),
            new Vector2(-1.25f * M, -1.65f * M), new Vector2(-0.3f * M, -1.3f * M),
        }, Iron);
    }

    private void DrawPlantation()
    {
        foreach (float x in new[] { -1.3f, 1.3f })
        {
            DrawRect(new Rect2((x - 1f) * M, -0.5f * M, 2f * M, 0.5f * M), new Color(0.36f, 0.24f, 0.14f));
            DrawLine(new Vector2(x * M, -0.5f * M), new Vector2(x * M, -1.1f * M), new Color(0.35f, 0.62f, 0.25f), 3f);
        }
    }

    private void DrawAltar()
    {
        DrawRect(new Rect2(-1.0f * M, -1.2f * M, 2.0f * M, 1.2f * M), Stone);
        DrawRect(new Rect2(-1.2f * M, -1.45f * M, 2.4f * M, 0.25f * M), Stone.Lightened(0.15f));
        DrawArc(new Vector2(0f, -0.6f * M), 0.35f * M, 0f, Mathf.Tau, 24, _accent, 3f);
    }
}
